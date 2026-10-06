using System.Globalization;
using System.Text.RegularExpressions;

namespace Paper.Web.Features.Processing;

public sealed class DocumentAnalyzer
{
    private static readonly Regex DatePattern = new(@"\b(?<day>\d{1,2})[./-](?<month>\d{1,2})[./-](?<year>20\d{2}|19\d{2})\b", RegexOptions.Compiled);

    public AnalysisResult Analyze(string fallbackTitle, string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var title = lines.FirstOrDefault(line => line.Length is > 2 and <= 120) ?? fallbackTitle;
        DateOnly? date = null;
        var match = DatePattern.Match(text);
        if (match.Success && DateOnly.TryParseExact($"{match.Groups["day"].Value}.{match.Groups["month"].Value}.{match.Groups["year"].Value}", "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }

        var tags = new List<string>();
        AddTagIfFound(tags, text, "rechnung", "rechnung", "invoice", "betrag", "mwst");
        AddTagIfFound(tags, text, "vertrag", "vertrag", "agreement", "kündigung");
        AddTagIfFound(tags, text, "versicherung", "versicherung", "insurance", "police");
        AddTagIfFound(tags, text, "steuer", "steuer", "finanzamt", "tax");
        var correspondent = FindFirst(text, "Stadtwerke Mannheim", "Allianz", "Sparkasse", "Amazon", "Finanzamt Mannheim");
        var documentType = new[]
        {
            ("Rechnung", new[] { "rechnung", "invoice", "betrag", "mwst" }),
            ("Vertrag", new[] { "vertrag", "agreement", "kündigung" }),
            ("Bescheid", new[] { "bescheid", "finanzamt" }),
            ("Kontoauszug", new[] { "kontoauszug", "kontostand" }),
            ("Versicherung", new[] { "versicherung", "police" })
        }.FirstOrDefault(item => item.Item2.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))).Item1;
        return new AnalysisResult(title, date, tags, correspondent, string.IsNullOrWhiteSpace(documentType) ? null : documentType);
    }

    private static void AddTagIfFound(List<string> tags, string text, string tag, params string[] terms)
    {
        if (terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(tag);
        }
    }

    private static string? FindFirst(string text, params string[] values) =>
        values.FirstOrDefault(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
}

public sealed record AnalysisResult(
    string Title,
    DateOnly? DocumentDate,
    IReadOnlyList<string> SuggestedTags,
    string? SuggestedCorrespondent,
    string? SuggestedDocumentType);
