using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Import;

public sealed class ImapMailImportWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    EmailAttachmentExtractor emailAttachments,
    ILogger<ImapMailImportWorker> logger) : BackgroundService
{
    private readonly SemaphoreSlim runGate = new(1, 1);

    public async Task<MailImportRunResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var options = LoadOptions();
        if (!options.Enabled)
        {
            return MailImportRunResult.Disabled;
        }

        if (!options.IsConfigured(out var configurationError))
        {
            throw new InvalidOperationException(configurationError);
        }

        await runGate.WaitAsync(cancellationToken);
        try
        {
            var result = await ImportAvailableAsync(options, cancellationToken);
            return new MailImportRunResult(true, result.Processed, result.Imported);
        }
        finally
        {
            runGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = LoadOptions();
        if (!options.Enabled)
        {
            logger.LogInformation("IMAP import is disabled.");
            return;
        }

        if (!options.IsConfigured(out var configurationError))
        {
            logger.LogError("IMAP import is enabled but not configured: {Error}", configurationError);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "IMAP import failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 30, 86400)), stoppingToken);
        }
    }

    private async Task<(int Processed, int Imported)> ImportAvailableAsync(MailAccountOptions options, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.MailImportStates.SingleOrDefaultAsync(item => item.AccountName == options.AccountName, cancellationToken);
        if (state is null)
        {
            state = new MailImportState { AccountName = options.AccountName };
            db.MailImportStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);
        }

        await using var client = new ImapClient(options, cancellationToken);
        await client.ConnectAsync();
        var uids = await client.SearchAsync(state.LastUid + 1, options.MaxMessagesPerRun, options.OnlyUnread, options.FromContains, options.SubjectContains);
        var importer = scope.ServiceProvider.GetRequiredService<DocumentImportService>();
        var allowedExtensions = options.AllowedAttachmentExtensions();
        var processed = 0;
        var importedTotal = 0;
        foreach (var uid in uids)
        {
            try
            {
                var message = await client.FetchMessageAsync(uid);
                var attachments = emailAttachments.Extract(message);
                var candidates = attachments
                    .Where(attachment => allowedExtensions.Count == 0 || allowedExtensions.Contains(Path.GetExtension(attachment.FileName)))
                    .ToArray();
                if (candidates.Length == 0)
                {
                    state.LastUid = uid;
                    state.LastSyncAt = DateTime.UtcNow;
                    state.LastError = null;
                    await db.SaveChangesAsync(cancellationToken);
                    if (options.MarkSeen)
                    {
                        await client.MarkSeenAsync(uid);
                    }

                    processed++;
                    continue;
                }

                var errors = new List<string>();
                var imported = 0;
                foreach (var attachment in candidates)
                {
                    await using var content = new MemoryStream(attachment.Content, writable: false);
                    var result = await importer.ImportAsync(
                        content,
                        SanitizeName(attachment.FileName),
                        attachment.ContentType,
                        attachment.Content.Length,
                        cancellationToken);
                    if (result.Success)
                    {
                        imported++;
                    }
                    else if (!string.Equals(result.Error, "Dieses Dokument ist bereits vorhanden.", StringComparison.Ordinal))
                    {
                        errors.Add($"{attachment.FileName}: {result.Error}");
                    }
                }

                state.LastSyncAt = DateTime.UtcNow;
                state.LastError = errors.Count == 0 ? null : $"UID {uid}: {string.Join(" | ", errors)}";
                if (errors.Count > 0)
                {
                    db.MailImportFailures.Add(new MailImportFailure
                    {
                        AccountName = options.AccountName,
                        Uid = uid,
                        Error = state.LastError ?? "Mail import failed.",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                if (errors.Count == 0)
                {
                    state.LastUid = uid;
                }

                await db.SaveChangesAsync(cancellationToken);
                if (errors.Count > 0)
                {
                    logger.LogWarning("IMAP UID {Uid} remains pending after an attachment error.", uid);
                    processed++;
                    break;
                }

                if (options.MarkSeen)
                {
                    await client.MarkSeenAsync(uid);
                }

                logger.LogInformation("Processed IMAP UID {Uid}: {ImportedCount} attachment(s) imported.", uid, imported);
                processed++;
                importedTotal += imported;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                state.LastSyncAt = DateTime.UtcNow;
                var error = $"UID {uid}: {exception.Message}";
                state.LastError = error[..Math.Min(2000, error.Length)];
                db.MailImportFailures.Add(new MailImportFailure
                {
                    AccountName = options.AccountName,
                    Uid = uid,
                    Error = state.LastError ?? error,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning(exception, "Could not process IMAP UID {Uid}.", uid);
                processed++;
                break;
            }
        }

        return (processed, importedTotal);
    }

    private MailAccountOptions LoadOptions() => configuration.GetSection("Mail").Get<MailAccountOptions>() ?? new MailAccountOptions();

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => character < 32 || invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "email-attachment" : safe[..Math.Min(180, safe.Length)];
    }
}

public sealed record MailImportRunResult(bool Executed, int Processed, int Imported)
{
    public static MailImportRunResult Disabled { get; } = new(false, 0, 0);
}
