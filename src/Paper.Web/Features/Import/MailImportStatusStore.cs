using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Import;

public sealed class MailImportStatusStore(AppDbContext db)
{
    public Task<List<MailImportStateView>> ListStatesAsync(CancellationToken cancellationToken) =>
        db.MailImportStates.AsNoTracking()
            .OrderBy(state => state.AccountName)
            .Select(state => new MailImportStateView(state.AccountName, state.LastUid, state.LastSyncAt, state.LastError))
            .ToListAsync(cancellationToken);

    public Task<List<MailImportFailureView>> ListFailuresAsync(CancellationToken cancellationToken) =>
        db.MailImportFailures.AsNoTracking()
            .OrderByDescending(failure => failure.CreatedAt)
            .Take(10)
            .Select(failure => new MailImportFailureView(failure.AccountName, failure.Uid, failure.Error, failure.CreatedAt))
            .ToListAsync(cancellationToken);
}

public sealed record MailImportStateView(string AccountName, long LastUid, DateTime? LastSyncAt, string? LastError);

public sealed record MailImportFailureView(string AccountName, long Uid, string Error, DateTime CreatedAt);
