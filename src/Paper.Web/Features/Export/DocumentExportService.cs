using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;

namespace Paper.Web.Features.Export;

public sealed class DocumentExportService(AppDbContext db)
{
    public async Task WriteJsonAsync(Stream destination, CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        await foreach (var document in Query().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", document.Id);
            writer.WriteString("title", document.Title);
            if (document.DocumentDate is not null)
            {
                writer.WriteString("documentDate", document.DocumentDate.Value.ToString("yyyy-MM-dd"));
            }
            writer.WriteString("originalFileName", document.OriginalFileName);
            writer.WriteString("filePath", document.FilePath);
            writer.WriteNumber("fileSize", document.FileSize);
            writer.WriteString("hash", document.Hash);
            writer.WriteString("status", document.Status.ToString());
            writer.WriteString("ocrStatus", document.OcrStatus.ToString());
            writer.WriteString("ocrText", document.OcrText);
            writer.WriteString("ocrError", document.OcrError);
            writer.WriteString("createdAt", document.CreatedAt);
            writer.WriteString("updatedAt", document.UpdatedAt);
            writer.WriteString("correspondent", document.Correspondent?.Name);
            writer.WriteString("documentType", document.DocumentType?.Name);
            writer.WriteString("shelfPath", document.ShelfFolder?.RelativePath);
            writer.WriteString("suggestedShelfPath", document.SuggestedShelfFolder?.RelativePath);
            writer.WriteStartArray("tags");
            foreach (var tag in document.Tags.OrderBy(item => item.Tag.Name))
            {
                writer.WriteStringValue(tag.Tag.Name);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("customFields");
            foreach (var field in document.CustomFields.OrderBy(item => item.CustomField.Name))
            {
                writer.WriteStartObject();
                writer.WriteString("name", field.CustomField.Name);
                writer.WriteString("type", field.CustomField.Type.ToString());
                writer.WriteString("value", field.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
    }

    public async Task WriteCsvAsync(Stream destination, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 16 * 1024, leaveOpen: true);
        await writer.WriteLineAsync("Id;Titel;Dokumentdatum;Originaldatei;Dateipfad;Größe;Hash;Status;OCR;OCR-Text;OCR-Fehler;Erstellt;Geändert;Korrespondent;Dokumenttyp;Regal;Regalvorschlag;Tags;Zusatzfelder");
        await writer.FlushAsync(cancellationToken);
        await foreach (var document in Query().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var customFields = string.Join(" | ", document.CustomFields
                .OrderBy(item => item.CustomField.Name)
                .Select(item => $"{item.CustomField.Name}={item.Value}"));
            var values = new[]
            {
                document.Id.ToString(),
                document.Title,
                document.DocumentDate?.ToString("yyyy-MM-dd"),
                document.OriginalFileName,
                document.FilePath,
                document.FileSize.ToString(),
                document.Hash,
                document.Status.ToString(),
                document.OcrStatus.ToString(),
                document.OcrText,
                document.OcrError,
                document.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                document.UpdatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                document.Correspondent?.Name,
                document.DocumentType?.Name,
                document.ShelfFolder?.RelativePath,
                document.SuggestedShelfFolder?.RelativePath,
                string.Join(", ", document.Tags.OrderBy(item => item.Tag.Name).Select(item => item.Tag.Name)),
                customFields
            };
            await writer.WriteLineAsync(string.Join(';', values.Select(Escape)));
        }

        await writer.FlushAsync(cancellationToken);
    }

    private IQueryable<Document> Query() =>
        db.Documents.AsNoTracking().AsSplitQuery()
            .Include(document => document.Correspondent)
            .Include(document => document.DocumentType)
            .Include(document => document.ShelfFolder)
            .Include(document => document.SuggestedShelfFolder)
            .Include(document => document.Tags).ThenInclude(link => link.Tag)
            .Include(document => document.CustomFields).ThenInclude(field => field.CustomField)
            .OrderBy(document => document.Id);

    private static string Escape(string? value)
    {
        var normalized = value ?? "";
        return normalized.Contains(';') || normalized.Contains('"') || normalized.Contains('\r') || normalized.Contains('\n')
            ? $"\"{normalized.Replace("\"", "\"\"")}\""
            : normalized;
    }
}
