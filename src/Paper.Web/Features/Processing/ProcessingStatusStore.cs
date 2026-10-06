using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Processing;

public sealed class ProcessingStatusStore(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<ProcessingSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var counts = await db.ProcessingJobs.AsNoTracking()
            .GroupBy(job => job.State)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);
        return new ProcessingSummary(
            counts.GetValueOrDefault(ProcessingJobState.Pending),
            counts.GetValueOrDefault(ProcessingJobState.Running),
            counts.GetValueOrDefault(ProcessingJobState.Failed));
    }

    public Task<List<ProcessingJobView>> ListRecentAsync(CancellationToken cancellationToken) =>
        db.ProcessingJobs.AsNoTracking()
            .Include(job => job.Document)
            .OrderByDescending(job => job.CreatedAt)
            .Take(100)
            .Select(job => new ProcessingJobView(
                job.Id,
                job.Document.Title,
                job.Document.OriginalFileName,
                job.Type,
                job.State,
                job.Attempts,
                job.Error,
                job.CreatedAt,
                job.FinishedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> RetryAsync(long id, CancellationToken cancellationToken)
    {
        var job = await db.ProcessingJobs
            .Include(item => item.Document)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (job is null || job.State != ProcessingJobState.Failed)
        {
            return false;
        }

        job.State = ProcessingJobState.Pending;
        job.Attempts = 0;
        job.StartedAt = null;
        job.FinishedAt = null;
        job.Error = null;
        job.Document.OcrStatus = OcrStatus.Pending;
        job.Document.OcrError = null;
        job.Document.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record ProcessingSummary(int Pending, int Running, int Failed)
{
    public int Active => Pending + Running;
}

public sealed record ProcessingJobView(
    long Id,
    string DocumentTitle,
    string OriginalFileName,
    ProcessingJobType Type,
    ProcessingJobState State,
    int Attempts,
    string? Error,
    DateTime CreatedAt,
    DateTime? FinishedAt);
