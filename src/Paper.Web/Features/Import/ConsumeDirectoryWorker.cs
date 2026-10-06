using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Import;

public sealed class ConsumeDirectoryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAvailableAsync(root, processing, failed, stoppingToken);
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
        CancellationToken cancellationToken)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(sourcePath);
            if (!ContentTypes.ContainsKey(extension) || Path.GetFileName(sourcePath).StartsWith(".", StringComparison.Ordinal))
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

            await ImportOneAsync(processingPath, failed, cancellationToken);
        }

        foreach (var processingPath in Directory.EnumerateFiles(processing, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportOneAsync(processingPath, failed, cancellationToken);
        }
    }

    private async Task ImportOneAsync(
        string processingPath,
        string failedDirectory,
        CancellationToken cancellationToken)
    {
        var processingName = Path.GetFileName(processingPath);
        var separator = processingName.IndexOf('_');
        var originalFileName = separator >= 0 ? processingName[(separator + 1)..] : processingName;
        try
        {
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
