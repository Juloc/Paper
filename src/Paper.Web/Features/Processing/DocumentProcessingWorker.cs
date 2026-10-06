using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Tags;

namespace Paper.Web.Features.Processing;

public sealed class DocumentProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var recoveryScope = scopeFactory.CreateScope())
        {
            var jobs = recoveryScope.ServiceProvider.GetRequiredService<ProcessingJobStore>();
            await jobs.RequeueInterruptedAsync(stoppingToken);
        }

        var workerCount = Math.Clamp(configuration.GetValue<int?>("Processing:OcrWorkers") ?? 1, 1, 4);
        var workers = Enumerable.Range(0, workerCount).Select(_ => RunWorkerAsync(stoppingToken)).ToArray();
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = Math.Clamp(configuration.GetValue<int?>("Processing:PollSeconds") ?? 3, 1, 60);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var jobStore = scope.ServiceProvider.GetRequiredService<ProcessingJobStore>();
                var job = await jobStore.ClaimNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                    continue;
                }

                await ProcessAsync(scope.ServiceProvider, job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The document processing worker failed while polling.");
                await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(IServiceProvider services, ProcessingJob job, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var jobStore = services.GetRequiredService<ProcessingJobStore>();
        var document = await db.Documents.Include(item => item.Tags).ThenInclude(item => item.Tag).SingleAsync(item => item.Id == job.DocumentId, cancellationToken);
        document.OcrStatus = OcrStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var ocr = services.GetRequiredService<TesseractOcrService>();
            var text = await ocr.ExtractAsync(document.FilePath, cancellationToken);
            var analysis = services.GetRequiredService<DocumentAnalyzer>().Analyze(document.Title, text);
            document.OcrText = text;
            document.OcrStatus = OcrStatus.Completed;
            document.OcrError = null;
            if (document.Title == Path.GetFileNameWithoutExtension(document.OriginalFileName))
            {
                document.Title = analysis.Title;
            }

            document.DocumentDate ??= analysis.DocumentDate;
            if (analysis.SuggestedCorrespondent is not null)
            {
                document.Correspondent = await db.Correspondents.SingleOrDefaultAsync(item => item.Name == analysis.SuggestedCorrespondent, cancellationToken)
                    ?? new Correspondent { Name = analysis.SuggestedCorrespondent };
            }

            if (analysis.SuggestedDocumentType is not null)
            {
                document.DocumentType = await db.DocumentTypes.SingleOrDefaultAsync(item => item.Name == analysis.SuggestedDocumentType, cancellationToken)
                    ?? new DocumentType { Name = analysis.SuggestedDocumentType };
            }

            await services.GetRequiredService<TagStore>().AddNamesAsync(document, analysis.SuggestedTags, cancellationToken);
            document.SearchText = TagStore.BuildSearchText(document);
            await db.SaveChangesAsync(cancellationToken);
            await jobStore.CompleteAsync(job.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            document.OcrStatus = OcrStatus.Failed;
            document.OcrError = exception.Message[..Math.Min(2000, exception.Message.Length)];
            await db.SaveChangesAsync(cancellationToken);
            await jobStore.FailAsync(job.Id, exception, cancellationToken);
            logger.LogWarning(exception, "Document processing failed for document {DocumentId}.", document.Id);
        }
    }
}
