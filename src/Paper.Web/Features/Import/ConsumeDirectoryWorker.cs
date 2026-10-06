using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Import;

public sealed class ConsumeDirectoryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    EmailAttachmentExtractor emailAttachments,
    ILogger<ConsumeDirectoryWorker> logger) : BackgroundService
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff"
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var root = Path.GetFullPath(configuration["Consume:RootPath"] ?? "/data/consume");
        var processing = Path.Combine(root, ".processing");
        var failed = Path.Combine(root, "failed");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(processing);
        Directory.CreateDirectory(failed);

        var pollSeconds = Math.Clamp(configuration.GetValue<int?>("Consume:PollSeconds") ?? 15, 5, 300);
        var importEmailAttachments = configuration.GetValue("Consume:EmailAttachments", true);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAvailableAsync(root, processing, failed, importEmailAttachments, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Consume directory processing failed.");
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
    }

    private async Task ConsumeAvailableAsync(
        string root,
        string processing,
        string failed,
        bool importEmailAttachments,
        CancellationToken cancellationToken)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(sourcePath);
            if ((!ContentTypes.ContainsKey(extension) && !(importEmailAttachments && extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))) ||
                Path.GetFileName(sourcePath).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsStable(sourcePath))
            {
                continue;
            }

            var processingPath = Path.Combine(processing, $"{Guid.NewGuid():N}_{SanitizeName(Path.GetFileName(sourcePath))}");
            try
            {
                File.Move(sourcePath, processingPath);
            }
            catch (IOException)
            {
                continue;
            }

            await ImportOneAsync(processingPath, failed, importEmailAttachments, cancellationToken);
        }

        foreach (var processingPath in Directory.EnumerateFiles(processing, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportOneAsync(processingPath, failed, importEmailAttachments, cancellationToken);
        }
    }

    private async Task ImportOneAsync(
        string processingPath,
        string failedDirectory,
        bool importEmailAttachments,
        CancellationToken cancellationToken)
    {
        var processingName = Path.GetFileName(processingPath);
        var separator = processingName.IndexOf('_');
        var originalFileName = separator >= 0 ? processingName[(separator + 1)..] : processingName;
        try
        {
            if (Path.GetExtension(originalFileName).Equals(".eml", StringComparison.OrdinalIgnoreCase))
            {
                await ImportEmailAsync(processingPath, originalFileName, failedDirectory, importEmailAttachments, cancellationToken);
                return;
            }

            ImportResult result;
            using (var scope = scopeFactory.CreateScope())
            await using (var file = new FileStream(processingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
            {
                var importer = scope.ServiceProvider.GetRequiredService<DocumentImportService>();
                result = await importer.ImportAsync(
                    file,
                    originalFileName,
                    ContentTypes[Path.GetExtension(originalFileName)],
                    file.Length,
                    cancellationToken);
            }

            if (result.Success)
            {
                File.Delete(processingPath);
                logger.LogInformation("Consumed {FileName} into document {DocumentId}.", originalFileName, result.DocumentId);
                return;
            }

            MoveFailed(processingPath, failedDirectory, originalFileName, result.Error ?? "Import fehlgeschlagen.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not consume {FileName}.", originalFileName);
            MoveFailed(processingPath, failedDirectory, originalFileName, exception.Message);
        }
    }

    private async Task ImportEmailAsync(
        string processingPath,
        string originalFileName,
        string failedDirectory,
        bool importEmailAttachments,
        CancellationToken cancellationToken)
    {
        if (!importEmailAttachments)
        {
            MoveFailed(processingPath, failedDirectory, originalFileName, "E-Mail-Anhänge sind deaktiviert.");
            return;
        }

        IReadOnlyList<EmailAttachment> attachments;
        await using (var message = new FileStream(processingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
        {
            attachments = await emailAttachments.ExtractAsync(message, cancellationToken);
        }

        if (attachments.Count == 0)
        {
            MoveFailed(processingPath, failedDirectory, originalFileName, "Die E-Mail enthält keine unterstützten Anhänge.");
            return;
        }

        var errors = new List<string>();
        var imported = 0;
        using var scope = scopeFactory.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<DocumentImportService>();
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
            else
            {
                errors.Add($"{attachment.FileName}: {result.Error}");
            }
        }

        if (errors.Count > 0)
        {
            MoveFailed(processingPath, failedDirectory, originalFileName, $"E-Mail teilweise importiert ({imported}): {string.Join(" | ", errors)}");
            return;
        }

        File.Delete(processingPath);
        logger.LogInformation("Consumed {FileName} with {AttachmentCount} attachment(s).", originalFileName, attachments.Count);
    }

    private void MoveFailed(string processingPath, string failedDirectory, string originalFileName, string error)
    {
        if (!File.Exists(processingPath))
        {
            return;
        }

        var safeName = SanitizeName(originalFileName);
        var destination = Path.Combine(failedDirectory, $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{safeName}");
        File.Move(processingPath, destination);
        File.WriteAllText(destination + ".error.txt", error[..Math.Min(2000, error.Length)]);
    }

    private static bool IsStable(string path)
    {
        var info = new FileInfo(path);
        if (info.Length == 0)
        {
            return false;
        }

        var age = DateTime.UtcNow - info.LastWriteTimeUtc;
        return age >= TimeSpan.FromSeconds(5);
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(character =>
            character < 32 || invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "document" : safe[..Math.Min(180, safe.Length)];
    }
}
