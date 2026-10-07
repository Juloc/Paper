using System.Diagnostics;
using System.Globalization;
using System.Text;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Processing;

public sealed class TesseractOcrService(IConfiguration configuration, IStorageProvider storage, ILogger<TesseractOcrService> logger)
{
    private const int MaximumOcrTextCharacters = 2_000_000;

    private TimeSpan ProcessTimeout => TimeSpan.FromSeconds(
        Math.Clamp(configuration.GetValue<int?>("Ocr:ProcessTimeoutSeconds") ?? 300, 30, 1800));

    public async Task<string> ExtractAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (storage.TryGetLocalPath(relativePath, out var localPath))
        {
            return await ExtractFromLocalPathAsync(localPath, cancellationToken);
        }

        var temporaryDirectory = Directory.CreateTempSubdirectory("paper-ocr-source-");
        var temporaryPath = Path.Combine(temporaryDirectory.FullName, $"source{Path.GetExtension(relativePath)}");
        try
        {
            await using (var source = storage.OpenRead(relativePath))
            await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            return await ExtractFromLocalPathAsync(temporaryPath, cancellationToken);
        }
        finally
        {
            try
            {
                temporaryDirectory.Delete(recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Could not remove temporary OCR source directory.");
            }
        }
    }

    private Task<string> ExtractFromLocalPathAsync(string path, CancellationToken cancellationToken) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? ExtractPdfAsync(path, cancellationToken)
            : RunTesseractAsync(path, cancellationToken);

    private async Task<string> ExtractPdfAsync(string path, CancellationToken cancellationToken)
    {
        var textExecutable = configuration["Ocr:PdfTextExecutablePath"] ?? "pdftotext";
        var textResult = await RunProcessAsync(textExecutable, [path, "-"], cancellationToken);
        if (textResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(textResult.StandardOutput))
        {
            return LimitText(textResult.StandardOutput);
        }

        var renderExecutable = configuration["Ocr:PdfRenderExecutablePath"] ?? "pdftoppm";
        var dpi = Math.Clamp(configuration.GetValue<int?>("Ocr:PdfRenderDpi") ?? 200, 120, 300);
        var temporaryDirectory = Directory.CreateTempSubdirectory("paper-pdf-ocr-");
        try
        {
            var prefix = Path.Combine(temporaryDirectory.FullName, "page");
            var renderResult = await RunProcessAsync(
                renderExecutable,
                ["-r", dpi.ToString(CultureInfo.InvariantCulture), "-png", path, prefix],
                cancellationToken);
            if (renderResult.ExitCode != 0)
            {
                throw new InvalidOperationException("Das PDF konnte nicht für die OCR verarbeitet werden.");
            }

            var pages = Directory.EnumerateFiles(temporaryDirectory.FullName, "page-*.png")
                .OrderBy(page => page, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (pages.Length == 0)
            {
                throw new InvalidOperationException("Das PDF enthält keine lesbaren Seiten.");
            }

            var text = new StringBuilder();
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageText = await RunTesseractAsync(page, cancellationToken);
                if (pageText.Length == 0)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.AppendLine();
                    text.AppendLine();
                }

                text.Append(pageText);
                if (text.Length >= MaximumOcrTextCharacters)
                {
                    break;
                }
            }

            return LimitText(text.ToString());
        }
        finally
        {
            try
            {
                temporaryDirectory.Delete(recursive: true);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Could not remove temporary PDF OCR directory.");
            }
        }
    }

    private async Task<string> RunTesseractAsync(string path, CancellationToken cancellationToken)
    {
        var executable = configuration["Ocr:ExecutablePath"] ?? "tesseract";
        var language = configuration["Ocr:Language"] ?? "eng";
        var result = await RunProcessAsync(executable, [path, "stdout", "-l", language], cancellationToken);
        if (result.ExitCode != 0)
        {
            logger.LogWarning("Tesseract failed for {Path} with exit code {ExitCode}.", path, result.ExitCode);
            throw new InvalidOperationException("OCR konnte nicht abgeschlossen werden.");
        }

        return LimitText(result.StandardOutput);
    }

    private async Task<ProcessResult> RunProcessAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Tesseract konnte nicht gestartet werden.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProcessTimeout);
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"Der OCR-Prozess hat das Zeitlimit von {ProcessTimeout.TotalSeconds:0} Sekunden überschritten.");
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited while cancellation cleanup was running.
        }
    }

    private static string LimitText(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= MaximumOcrTextCharacters ? normalized : normalized[..MaximumOcrTextCharacters];
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
