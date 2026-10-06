using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Processing;

public sealed class AnalysisRuleStore(AppDbContext db)
{
    public Task<List<AnalysisRuleView>> ListAsync(CancellationToken cancellationToken) =>
        db.AnalysisRules.AsNoTracking()
            .OrderByDescending(rule => rule.UseCount)
            .ThenBy(rule => rule.Term)
            .Take(100)
            .Select(rule => new AnalysisRuleView(
                rule.Id,
                rule.Term,
                rule.UseCount,
                db.Correspondents.Where(item => item.Id == rule.CorrespondentId).Select(item => item.Name).FirstOrDefault(),
                db.DocumentTypes.Where(item => item.Id == rule.DocumentTypeId).Select(item => item.Name).FirstOrDefault(),
                db.ShelfFolders.Where(item => item.Id == rule.ShelfFolderId).Select(item => item.RelativePath).FirstOrDefault(),
                rule.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var rule = await db.AnalysisRules.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        db.AnalysisRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record AnalysisRuleView(
    long Id,
    string Term,
    int UseCount,
    string? Correspondent,
    string? DocumentType,
    string? ShelfPath,
    DateTime UpdatedAt);
