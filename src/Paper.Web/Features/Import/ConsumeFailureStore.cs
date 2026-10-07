using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Import;

public sealed class ConsumeFailureStore(AppDbContext db, TimeProvider timeProvider)
{
    public Task<List<ConsumeFailureView>> ListAsync(CancellationToken cancellationToken) =>
        db.ConsumeFailures.AsNoTracking()
            .OrderByDescending(failure => failure.CreatedAt)
            .Take(20)
            .Select(failure => new ConsumeFailureView(failure.OriginalFileName, failure.Error, failure.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task RecordAsync(string originalFileName, string error, CancellationToken cancellationToken)
    {
        var normalizedFileName = originalFileName.Trim();
        var normalizedError = error.Trim();
        db.ConsumeFailures.Add(new ConsumeFailure
        {
            OriginalFileName = normalizedFileName[..Math.Min(255, normalizedFileName.Length)],
            Error = normalizedError[..Math.Min(2000, normalizedError.Length)],
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ConsumeFailureView(string OriginalFileName, string Error, DateTime CreatedAt);
