using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Documents;

namespace Paper.Web.Features.Processing;

public sealed class DocumentLearningStore(AppDbContext db, TimeProvider timeProvider)
{
    private static readonly Regex WordPattern = new(@"[\p{L}\p{Nd}]{4,80}", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords =
    [
        "aber", "alle", "auch", "eine", "einer", "eines", "für", "gegen", "ihre", "ihren", "mit", "nach", "oder", "sich", "sind", "über", "und", "von", "wird", "zum", "zur", "dass", "dies", "hier", "bitte", "sehr"
    ];

    public async Task RecordCorrectionAsync(long documentId, DocumentEdit edit, CancellationToken cancellationToken)
    {
        if (edit.CorrespondentId is null && edit.DocumentTypeId is null && edit.ShelfFolderId is null)
        {
            return;
        }

        var document = await db.Documents.AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new { item.Title, item.OriginalFileName, item.OcrText })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var terms = ExtractTerms($"{document.Title} {document.OriginalFileName} {document.OcrText}").Take(12).ToArray();
        if (terms.Length == 0)
        {
            return;
        }

        var rules = await db.AnalysisRules
            .Where(rule => terms.Contains(rule.Term) &&
                          rule.CorrespondentId == edit.CorrespondentId &&
                          rule.DocumentTypeId == edit.DocumentTypeId &&
                          rule.ShelfFolderId == edit.ShelfFolderId)
            .ToListAsync(cancellationToken);
        foreach (var rule in rules)
        {
            rule.UseCount++;
            rule.UpdatedAt = now;
        }

        var existingTerms = rules.Select(rule => rule.Term).ToHashSet(StringComparer.Ordinal);
        foreach (var term in terms.Where(term => !existingTerms.Contains(term)))
        {
            db.AnalysisRules.Add(new AnalysisRule
            {
                Term = term,
                CorrespondentId = edit.CorrespondentId,
                DocumentTypeId = edit.DocumentTypeId,
                ShelfFolderId = edit.ShelfFolderId,
                UseCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<LearningSuggestion?> SuggestAsync(string text, CancellationToken cancellationToken)
    {
        var terms = ExtractTerms(text).Take(30).ToArray();
        if (terms.Length == 0)
        {
            return null;
        }

        var rules = await db.AnalysisRules.AsNoTracking()
            .Where(rule => terms.Contains(rule.Term) && rule.UseCount >= 2)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return null;
        }

        var correspondent = Best(rules.Where(rule => rule.CorrespondentId is not null).GroupBy(rule => rule.CorrespondentId!.Value));
        var documentType = Best(rules.Where(rule => rule.DocumentTypeId is not null).GroupBy(rule => rule.DocumentTypeId!.Value));
        var shelfFolder = Best(rules.Where(rule => rule.ShelfFolderId is not null).GroupBy(rule => rule.ShelfFolderId!.Value));
        var confidences = new[] { correspondent.Confidence, documentType.Confidence, shelfFolder.Confidence }
            .Where(confidence => confidence > 0)
            .ToArray();
        var confidence = confidences.Length == 0 ? 0 : confidences.Average();
        return confidence < 0.35 ? null : new LearningSuggestion(correspondent.Id, documentType.Id, shelfFolder.Id, confidence);
    }

    public static IReadOnlyList<string> ExtractTerms(string text) =>
        WordPattern.Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(term => !StopWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static (long? Id, double Confidence) Best(IEnumerable<IGrouping<long, AnalysisRule>> groups)
    {
        var scores = groups.Select(group => new { Id = group.Key, Score = group.Sum(rule => rule.UseCount) })
            .ToArray();
        var total = scores.Sum(item => item.Score);
        var best = scores
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        return best is null || total == 0 ? (null, 0) : (best.Id, (double)best.Score / total);
    }
}

public sealed record LearningSuggestion(long? CorrespondentId, long? DocumentTypeId, long? ShelfFolderId, double Confidence);
