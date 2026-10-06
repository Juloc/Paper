using System.Globalization;
using System.Text.RegularExpressions;

namespace Paper.Web.Features.Processing;

public sealed class DocumentAnalyzer
{
    private static readonly Regex DatePattern = new(@"\b(?<day>\d{1,2})[./-](?<month>\d{1,2})[./-](?<year>20\d{2}|19\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex InvoiceNumberPattern = new(@"(?im)\b(?:rechnungsnummer|rechnung\s*(?:nr\.?|nummer))\s*[:#]?\s*(?<value>[A-Z0-9][A-Z0-9./_-]{2,80})", RegexOptions.Compiled);
    private static readonly Regex CustomerNumberPattern = new(@"(?im)\b(?:kundennummer|kunden\s*(?:nr\.?|nummer))\s*[:#]?\s*(?<value>[A-Z0-9][A-Z0-9./_-]{2,80})", RegexOptions.Compiled);
    private static readonly Regex ContractNumberPattern = new(@"(?im)\b(?:vertragsnummer|vertrags\s*(?:nr\.?|nummer))\s*[:#]?\s*(?<value>[A-Z0-9][A-Z0-9./_-]{2,80})", RegexOptions.Compiled);
    private static readonly Regex IbanPattern = new(@"\b(?<value>[A-Z]{2}[ \t]?\d{2}(?:[ \t]?[A-Z0-9]){10,30})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AmountPattern = new(@"(?im)\b(?:betrag|gesamt(?:betrag)?|summe)\s*[:#]?\s*(?<value>\d{1,3}(?:[.\s]\d{3})*(?:[,.]\d{2})?)\s*(?:EUR|€)?", RegexOptions.Compiled);

    public AnalysisResult Analyze(
        string fallbackTitle,
        string text,
        IReadOnlyCollection<string>? knownCorrespondents = null,
        IReadOnlyCollection<string>? knownDocumentTypes = null,
        IReadOnlyCollection<string>? knownTags = null)
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
        foreach (var knownTag in knownTags ?? [])
        {
            if (knownTag.Length >= 3 && text.Contains(knownTag, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add(knownTag);
            }
        }

        var correspondent = FindFirst(
            text,
            (knownCorrespondents ?? [])
                .Concat(["Stadtwerke Mannheim", "Allianz", "Sparkasse", "Amazon", "Finanzamt Mannheim"])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .ToArray());
        var documentType = new[]
        {
            ("Rechnung", new[] { "rechnung", "invoice", "betrag", "mwst" }),
            ("Vertrag", new[] { "vertrag", "agreement", "kündigung" }),
            ("Bescheid", new[] { "bescheid", "finanzamt" }),
            ("Kontoauszug", new[] { "kontoauszug", "kontostand" }),
            ("Versicherung", new[] { "versicherung", "police" })
        }
        .Concat((knownDocumentTypes ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => (value, new[] { value })))
        .OrderByDescending(item => item.Item1.Length)
        .FirstOrDefault(item => item.Item2.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))).Item1;
        var customFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddFieldIfFound(customFields, "Rechnungsnummer", InvoiceNumberPattern, text);
        AddFieldIfFound(customFields, "Kundennummer", CustomerNumberPattern, text);
        AddFieldIfFound(customFields, "Vertragsnummer", ContractNumberPattern, text);
        AddFieldIfFound(customFields, "IBAN", IbanPattern, text);
        AddFieldIfFound(customFields, "Betrag", AmountPattern, text);
        return new AnalysisResult(title, date, tags, correspondent, string.IsNullOrWhiteSpace(documentType) ? null : documentType, customFields);
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

    private static void AddFieldIfFound(Dictionary<string, string> fields, string name, Regex pattern, string text)
    {
        var match = pattern.Match(text);
        if (match.Success)
        {
            fields[name] = match.Groups["value"].Value.Trim();
        }
    }
}

public sealed record AnalysisResult(
    string Title,
    DateOnly? DocumentDate,
    IReadOnlyList<string> SuggestedTags,
    string? SuggestedCorrespondent,
    string? SuggestedDocumentType,
    IReadOnlyDictionary<string, string> SuggestedCustomFields);
