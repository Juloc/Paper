using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Processing;

public sealed class ProcessingJobStore(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<int> RequeueInterruptedAsync(CancellationToken cancellationToken)
    {
        var requeued = await db.ProcessingJobs
            .Where(job => job.State == ProcessingJobState.Running && job.Attempts < 3)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, ProcessingJobState.Pending)
                .SetProperty(job => job.StartedAt, (DateTime?)null)
                .SetProperty(job => job.Error, "Der Worker wurde unterbrochen; der Job wird erneut versucht."), cancellationToken);
        await db.ProcessingJobs
            .Where(job => job.State == ProcessingJobState.Running && job.Attempts >= 3)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, ProcessingJobState.Failed)
                .SetProperty(job => job.FinishedAt, timeProvider.GetUtcNow().UtcDateTime)
                .SetProperty(job => job.Error, "Der Worker wurde nach dem letzten Versuch unterbrochen."), cancellationToken);
        return requeued;
    }

    public async Task<ProcessingJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await db.ProcessingJobs
            .FromSqlRaw("""
                SELECT *
                FROM "ProcessingJobs"
                WHERE "State" = 'Pending'
                ORDER BY "Priority" DESC, "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        job.State = ProcessingJobState.Running;
        job.Attempts++;
        job.StartedAt = timeProvider.GetUtcNow().UtcDateTime;
        job.Error = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task CompleteAsync(long jobId, CancellationToken cancellationToken)
    {
        var job = await db.ProcessingJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
        job.State = ProcessingJobState.Succeeded;
        job.FinishedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(long jobId, Exception exception, CancellationToken cancellationToken)
    {
        var job = await db.ProcessingJobs.SingleAsync(item => item.Id == jobId, cancellationToken);
        job.State = job.Attempts >= 3 ? ProcessingJobState.Failed : ProcessingJobState.Pending;
        job.Error = exception.Message[..Math.Min(2000, exception.Message.Length)];
        job.FinishedAt = job.State == ProcessingJobState.Failed ? timeProvider.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(cancellationToken);
    }
}
