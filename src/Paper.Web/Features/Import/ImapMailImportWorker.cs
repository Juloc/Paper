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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = configuration.GetSection("Mail").Get<MailAccountOptions>() ?? new MailAccountOptions();
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
                await ImportAvailableAsync(options, stoppingToken);
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

    private async Task ImportAvailableAsync(MailAccountOptions options, CancellationToken cancellationToken)
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
        var uids = await client.SearchAsync(state.LastUid + 1, options.MaxMessagesPerRun, options.OnlyUnread);
        var importer = scope.ServiceProvider.GetRequiredService<DocumentImportService>();
        foreach (var uid in uids)
        {
            try
            {
                var message = await client.FetchMessageAsync(uid);
                var attachments = emailAttachments.Extract(message);
                var errors = new List<string>();
                var imported = 0;
                foreach (var attachment in attachments)
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

                if (attachments.Count == 0)
                {
                    errors.Add("Die E-Mail enthält keine unterstützten Anhänge.");
                }

                state.LastUid = uid;
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

                await db.SaveChangesAsync(cancellationToken);
                if (errors.Count == 0 && options.MarkSeen)
                {
                    await client.MarkSeenAsync(uid);
                }

                logger.LogInformation("Processed IMAP UID {Uid}: {ImportedCount} attachment(s) imported.", uid, imported);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                state.LastUid = uid;
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
            }
        }
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character => character < 32 || invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "email-attachment" : safe[..Math.Min(180, safe.Length)];
    }
}
