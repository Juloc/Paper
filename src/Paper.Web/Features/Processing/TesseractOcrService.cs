using System.Diagnostics;
using Paper.Web.Features.Storage;

namespace Paper.Web.Features.Processing;

public sealed class TesseractOcrService(IConfiguration configuration, LocalDocumentStorage storage, ILogger<TesseractOcrService> logger)
{
    public async Task<string> ExtractAsync(string relativePath, CancellationToken cancellationToken)
    {
        var executable = configuration["Ocr:ExecutablePath"] ?? "tesseract";
        var language = configuration["Ocr:Language"] ?? "eng";
        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processStartInfo.ArgumentList.Add(storage.GetSafePath(relativePath));
        processStartInfo.ArgumentList.Add("stdout");
        processStartInfo.ArgumentList.Add("-l");
        processStartInfo.ArgumentList.Add(language);

        using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Tesseract konnte nicht gestartet werden.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Tesseract failed for {Path}: {Error}", relativePath, error.Trim());
            throw new InvalidOperationException("OCR konnte nicht abgeschlossen werden.");
        }

        return output.Trim();
    }
}
