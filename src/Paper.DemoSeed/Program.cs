using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Documents;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;

const string storageRoot = "/data/documents";
const string markerPath = $"{storageRoot}/.paper-demo-seeded";
var cancellationToken = CancellationToken.None;

if (File.Exists(markerPath))
{
    Console.WriteLine("Paper demo data already exists; seeding is skipped.");
    return;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? throw new InvalidOperationException("ConnectionStrings__Default is required.");
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Storage:Provider"] = "local",
        ["Storage:RootPath"] = storageRoot
    })
    .Build();
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var db = new AppDbContext(options);
await db.Database.MigrateAsync(cancellationToken);
var storage = new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);
var importer = new DocumentImportService(db, storage, TimeProvider.System, NullLogger<DocumentImportService>.Instance);
var filing = new DocumentFilingService(db, storage, TimeProvider.System, NullLogger<DocumentFilingService>.Instance);

var correspondents = CreateCorrespondents();
var documentTypes = CreateDocumentTypes();
var customFields = CreateCustomFields();
db.Correspondents.AddRange(correspondents);
db.DocumentTypes.AddRange(documentTypes);
db.CustomFields.AddRange(customFields);
await db.SaveChangesAsync(cancellationToken);

var folders = await CreateFoldersAsync(db, cancellationToken);
var allFields = customFields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
var allCorrespondents = correspondents.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
var allDocumentTypes = documentTypes.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
db.ChangeTracker.Clear();

foreach (var item in DemoDocuments())
{
    await SeedDocumentAsync(
        db,
        importer,
        filing,
        folders,
        allFields,
        allCorrespondents,
        allDocumentTypes,
        item,
        cancellationToken);
    db.ChangeTracker.Clear();
}

await MarkSpecialJobStatesAsync(db, cancellationToken);
Directory.CreateDirectory(storageRoot);
await File.WriteAllTextAsync(markerPath, $"seeded {DateTimeOffset.UtcNow:O}{Environment.NewLine}", cancellationToken);
Console.WriteLine("Paper demo data seeded successfully.");

static List<Correspondent> CreateCorrespondents() =>
[
    new() { Name = "Stadtwerke Mannheim" },
    new() { Name = "Deutsche Telekom" },
    new() { Name = "Allianz" },
    new() { Name = "Sparkasse" },
    new() { Name = "Finanzamt Mannheim" },
    new() { Name = "Hausverwaltung Mustermann" },
    new() { Name = "ADAC" },
    new() { Name = "Amazon" }
];

static List<DocumentType> CreateDocumentTypes() =>
[
    new() { Name = "Rechnung" },
    new() { Name = "Vertrag" },
    new() { Name = "Bescheid" },
    new() { Name = "Kontoauszug" },
    new() { Name = "Schreiben" },
    new() { Name = "Beleg" },
    new() { Name = "Abrechnung" }
];

static List<CustomField> CreateCustomFields() =>
[
    new() { Name = "Kundennummer", Type = CustomFieldType.Text },
    new() { Name = "Vertragsnummer", Type = CustomFieldType.Text },
    new() { Name = "Rechnungsnummer", Type = CustomFieldType.Text },
    new() { Name = "Betrag", Type = CustomFieldType.Number },
    new() { Name = "Zahlungsart", Type = CustomFieldType.Text },
    new() { Name = "IBAN", Type = CustomFieldType.Text }
];

static async Task<Dictionary<string, ShelfFolder>> CreateFoldersAsync(
    AppDbContext db,
    CancellationToken cancellationToken)
{
    var paths = new[]
    {
        "Wohnung", "Wohnung/Strom", "Wohnung/Internet", "Wohnung/Miete", "Wohnung/Versicherung",
        "Auto", "Auto/Versicherung", "Auto/Werkstatt", "Auto/Steuer",
        "Finanzen", "Finanzen/Bank", "Finanzen/Steuer"
    };
    var folders = new Dictionary<string, ShelfFolder>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in paths)
    {
        var parentPath = path.Contains('/') ? path[..path.LastIndexOf('/')] : null;
        var folder = new ShelfFolder
        {
            Parent = parentPath is null ? null : folders[parentPath],
            Name = path[(path.LastIndexOf('/') + 1)..],
            RelativePath = path,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.ShelfFolders.Add(folder);
        folders.Add(path, folder);
        await db.SaveChangesAsync(cancellationToken);
    }

    return folders;
}

static IReadOnlyList<DemoDocument> DemoDocuments() =>
[
    Filed("Wohnung/Strom", "2026-10-05 Stadtwerke Mannheim Rechnung.pdf", "Stadtwerke Mannheim", "Rechnung", ["Wohnung", "Strom", "Haushalt"], [("Kundennummer", "47110815"), ("Rechnungsnummer", "STW-48392"), ("Betrag", "209.95"), ("Zahlungsart", "SEPA")], "Stromrechnung fuer Oktober 2026", "209.95 EUR", "Kundennummer 47110815", "Rechnungsnummer STW-48392"),
    Filed("Wohnung/Strom", "2026-09-05 Stadtwerke Mannheim Rechnung.pdf", "Stadtwerke Mannheim", "Rechnung", ["Wohnung", "Strom"], [("Kundennummer", "47110815"), ("Rechnungsnummer", "STW-47201"), ("Betrag", "188.40")], "Stromrechnung fuer September 2026", "188.40 EUR", "Kundennummer 47110815", "Rechnungsnummer STW-47201"),
    Filed("Wohnung/Strom", "2026-08-05 Stadtwerke Mannheim Rechnung.pdf", "Stadtwerke Mannheim", "Rechnung", ["Wohnung", "Strom"], [("Kundennummer", "47110815"), ("Rechnungsnummer", "STW-46177"), ("Betrag", "196.10")], "Stromrechnung fuer August 2026", "196.10 EUR", "Kundennummer 47110815", "Rechnungsnummer STW-46177"),
    Filed("Wohnung/Strom", "2026-07-05 Stadtwerke Mannheim Rechnung.pdf", "Stadtwerke Mannheim", "Rechnung", ["Wohnung", "Strom"], [("Kundennummer", "47110815"), ("Rechnungsnummer", "STW-45012"), ("Betrag", "201.35")], "Stromrechnung fuer Juli 2026", "201.35 EUR", "Kundennummer 47110815", "Rechnungsnummer STW-45012"),
    Filed("Wohnung/Internet", "2026-10-01 Deutsche Telekom Rechnung.pdf", "Deutsche Telekom", "Rechnung", ["Wohnung", "Internet"], [("Kundennummer", "TK-88210"), ("Rechnungsnummer", "TEL-20261001"), ("Betrag", "49.95")], "Internetrechnung Oktober 2026", "49.95 EUR", "Kundennummer TK-88210", "Rechnungsnummer TEL-20261001"),
    Filed("Wohnung/Internet", "2026-01-15 Deutsche Telekom Vertrag.pdf", "Deutsche Telekom", "Vertrag", ["Wohnung", "Internet", "Wichtig"], [("Vertragsnummer", "TEL-VER-88210"), ("Zahlungsart", "SEPA")], "Internetvertrag Magenta Zuhause", "Vertragsnummer TEL-VER-88210", "Mannheim"),
    Filed("Wohnung/Miete", "2026-01-03 Hausverwaltung Nebenkostenabrechnung.pdf", "Hausverwaltung Mustermann", "Abrechnung", ["Wohnung", "Haushalt"], [("Betrag", "842.30"), ("Zahlungsart", "Ueberweisung")], "Nebenkostenabrechnung 2025", "842.30 EUR", "Wohnung Mannheim"),
    Filed("Wohnung/Miete", "2025-12-15 Hausverwaltung Mietanpassung.pdf", "Hausverwaltung Mustermann", "Schreiben", ["Wohnung", "Wichtig"], [], "Mietanpassung ab Januar 2026", "Neue monatliche Nettokaltmiete ab 01.01.2026", "Hausverwaltung Mustermann"),
    Filed("Wohnung/Versicherung", "2026-02-20 Amazon Garantiebeleg.pdf", "Amazon", "Beleg", ["Wohnung", "Garantie"], [("Betrag", "129.00"), ("Zahlungsart", "Karte")], "Garantiebeleg fuer Haushaltsgeraet", "Garantiebeleg und Kaufdatum 20.02.2026", "129.00 EUR"),
    Filed("Auto/Versicherung", "2026-04-10 Allianz Beitragsrechnung.pdf", "Allianz", "Rechnung", ["Auto", "Versicherung"], [("Vertragsnummer", "ALL-KFZ-7712"), ("Rechnungsnummer", "ALL-2026-0410"), ("Betrag", "612.80")], "Kfz Versicherungsbeitrag 2026", "612.80 EUR", "Vertragsnummer ALL-KFZ-7712", "Rechnungsnummer ALL-2026-0410"),
    Filed("Auto/Versicherung", "2026-03-20 Allianz Versicherungsschein.pdf", "Allianz", "Vertrag", ["Auto", "Versicherung", "Wichtig"], [("Vertragsnummer", "ALL-KFZ-7712")], "Versicherungsschein Kraftfahrzeug", "Vertragsnummer ALL-KFZ-7712", "Mannheim"),
    Filed("Auto/Werkstatt", "2026-08-17 Werkstatt Inspektion Rechnung.pdf", "ADAC", "Rechnung", ["Auto", "Wichtig"], [("Rechnungsnummer", "ADAC-W-8817"), ("Betrag", "389.50")], "Inspektion und Reparatur", "389.50 EUR", "Rechnungsnummer ADAC-W-8817", "Oelwechsel Bremsen Inspektion"),
    Filed("Auto/Werkstatt", "2026-02-09 ADAC Pannenhilfe Beleg.pdf", "ADAC", "Beleg", ["Auto"], [("Betrag", "145.00"), ("Zahlungsart", "Karte")], "Pannenhilfe Februar 2026", "145.00 EUR", "Mannheim"),
    Filed("Auto/Steuer", "2026-06-10 Finanzamt Kfz Steuer Bescheid.pdf", "Finanzamt Mannheim", "Bescheid", ["Auto", "Steuer", "Wichtig"], [("Betrag", "178.00"), ("Zahlungsart", "SEPA")], "Kraftfahrzeugsteuer Bescheid", "178.00 EUR", "Steuerjahr 2026", "Finanzamt Mannheim"),
    Filed("Finanzen/Bank", "2026-09-30 Sparkasse Kontoauszug.pdf", "Sparkasse", "Kontoauszug", ["Bank", "Wichtig"], [("IBAN", "DE02123456789012345678")], "Kontoauszug September 2026", "Kontostand 2840.72 EUR", "IBAN DE02123456789012345678"),
    Filed("Finanzen/Bank", "2026-08-31 Sparkasse Kontoauszug.pdf", "Sparkasse", "Kontoauszug", ["Bank"], [("IBAN", "DE02123456789012345678")], "Kontoauszug August 2026", "Kontostand 3012.18 EUR", "IBAN DE02123456789012345678"),
    Filed("Finanzen/Steuer", "2026-06-12 Finanzamt Einkommensteuerbescheid.pdf", "Finanzamt Mannheim", "Bescheid", ["Steuer", "Wichtig"], [("Betrag", "1240.00"), ("Zahlungsart", "Ueberweisung")], "Einkommensteuerbescheid 2025", "Erstattung 1240.00 EUR", "Steuerjahr 2025"),
    Filed("Finanzen/Steuer", "2025-07-01 Finanzamt Steuerbescheid.pdf", "Finanzamt Mannheim", "Bescheid", ["Steuer"], [("Betrag", "380.00")], "Steuerbescheid 2024", "Nachzahlung 380.00 EUR", "Steuerjahr 2024"),
    Filed("Wohnung/Miete", "2026-02-01 Hausverwaltung Mietquittung.pdf", "Hausverwaltung Mustermann", "Beleg", ["Wohnung", "Haushalt"], [("Betrag", "980.00"), ("Zahlungsart", "Ueberweisung")], "Mietquittung Februar 2026", "980.00 EUR", "Mietkonto Wohnung Mannheim"),
    Inbox("Neue Stromrechnung.pdf", "Stadtwerke Mannheim", "Rechnung", "Wohnung/Strom", ["Wohnung", "Strom"], [("Rechnungsnummer", "STW-NEW-100"), ("Betrag", "214.70")], "Neue Stromrechnung zur Pruefung", "214.70 EUR", "Rechnungsnummer STW-NEW-100"),
    InboxFailed("Neuer Kassenbon.pdf", "Amazon", "Beleg", "Wohnung/Versicherung", ["Haushalt", "Garantie"], [("Betrag", "59.90"), ("Zahlungsart", "Karte")], "Kassenbon fuer Ersatzteil", "59.90 EUR", "Kaufdatum 07.10.2026"),
    Inbox("Versicherungsschreiben.pdf", "Allianz", "Schreiben", "Auto/Versicherung", ["Auto", "Versicherung"], [("Vertragsnummer", "ALL-KFZ-7712")], "Versicherungsschreiben zur Pruefung", "Vertragsnummer ALL-KFZ-7712", "Bitte Unterlagen pruefen"),
    Inbox("Telekom-Rechnung.pdf", "Deutsche Telekom", "Rechnung", "Wohnung/Internet", ["Wohnung", "Internet"], [("Betrag", "49.95"), ("Rechnungsnummer", "TEL-INBOX-01")], "Telekom Rechnung neue Leitung", "49.95 EUR", "Rechnungsnummer TEL-INBOX-01"),
    InboxProcessing("Werkstattrechnung.pdf", "ADAC", "Rechnung", "Auto/Werkstatt", ["Auto", "Wichtig"], [("Betrag", "275.00"), ("Rechnungsnummer", "ADAC-INBOX-27")], "Werkstattrechnung zur Pruefung", "275.00 EUR", "Rechnungsnummer ADAC-INBOX-27"),
];

static DemoDocument Filed(
    string folder,
    string fileName,
    string correspondent,
    string documentType,
    IReadOnlyList<string> tags,
    IReadOnlyList<(string Name, string Value)> fields,
    params string[] lines) =>
    new(folder, fileName, correspondent, documentType, tags, fields, lines, true, false, false);

static DemoDocument Inbox(
    string fileName,
    string correspondent,
    string documentType,
    string suggestedFolder,
    IReadOnlyList<string> tags,
    IReadOnlyList<(string Name, string Value)> fields,
    params string[] lines) =>
    new(suggestedFolder, fileName, correspondent, documentType, tags, fields, lines, false, false, false);

static DemoDocument InboxProcessing(
    string fileName,
    string correspondent,
    string documentType,
    string suggestedFolder,
    IReadOnlyList<string> tags,
    IReadOnlyList<(string Name, string Value)> fields,
    params string[] lines) =>
    new(suggestedFolder, fileName, correspondent, documentType, tags, fields, lines, false, true, false);

static DemoDocument InboxFailed(
    string fileName,
    string correspondent,
    string documentType,
    string suggestedFolder,
    IReadOnlyList<string> tags,
    IReadOnlyList<(string Name, string Value)> fields,
    params string[] lines) =>
    new(suggestedFolder, fileName, correspondent, documentType, tags, fields, lines, false, false, true);

static async Task SeedDocumentAsync(
    AppDbContext db,
    DocumentImportService importer,
    DocumentFilingService filing,
    IReadOnlyDictionary<string, ShelfFolder> folders,
    IReadOnlyDictionary<string, CustomField> fields,
    IReadOnlyDictionary<string, Correspondent> correspondents,
    IReadOnlyDictionary<string, DocumentType> documentTypes,
    DemoDocument item,
    CancellationToken cancellationToken)
{
    var pdf = DemoPdf.Create(item.FileName, item.Lines);
    await using var content = new MemoryStream(pdf, writable: false);
    var imported = await importer.ImportAsync(content, item.FileName, "application/pdf", pdf.Length, cancellationToken);
    if (!imported.Success || imported.DocumentId is null)
    {
        throw new InvalidOperationException($"Could not seed {item.FileName}: {imported.Error}");
    }

    var fieldValues = item.Fields
        .Where(value => fields.ContainsKey(value.Name))
        .ToDictionary(value => fields[value.Name].Id, value => value.Value);
    var title = item.Lines.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(item.FileName);
    var edit = new DocumentEdit(
        title,
        DateOnly.TryParse(item.FileName[..10], out var date) ? date : null,
        correspondents[item.Correspondent].Id,
        documentTypes[item.DocumentType].Id,
        folders[item.Folder].Id,
        string.Join(", ", item.Tags),
        fieldValues);
    var saved = await filing.SaveAsync(imported.DocumentId.Value, edit, item.Filed, cancellationToken);
    if (!saved.Succeeded)
    {
        throw new InvalidOperationException($"Could not file {item.FileName}: {saved.Error}");
    }

    if (item.Failed)
    {
        var document = await db.Documents.SingleAsync(document => document.Id == imported.DocumentId, cancellationToken);
        var job = await db.ProcessingJobs.SingleAsync(job => job.DocumentId == document.Id, cancellationToken);
        document.OcrStatus = OcrStatus.Failed;
        document.OcrError = "Demo-Fehler: OCR wurde für dieses Dokument absichtlich als fehlgeschlagen markiert.";
        job.State = ProcessingJobState.Failed;
        job.Attempts = 3;
        job.Error = document.OcrError;
        job.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

static async Task MarkSpecialJobStatesAsync(AppDbContext db, CancellationToken cancellationToken)
{
    var processingDocument = await db.Documents.SingleAsync(
        document => document.OriginalFileName == "Werkstattrechnung.pdf",
        cancellationToken);
    var processingJob = await db.ProcessingJobs.SingleAsync(
        job => job.DocumentId == processingDocument.Id,
        cancellationToken);
    processingDocument.OcrStatus = OcrStatus.Processing;
    processingJob.State = ProcessingJobState.Running;
    processingJob.Attempts = Math.Max(processingJob.Attempts, 1);
    processingJob.StartedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
}

public sealed record DemoDocument(
    string Folder,
    string FileName,
    string Correspondent,
    string DocumentType,
    IReadOnlyList<string> Tags,
    IReadOnlyList<(string Name, string Value)> Fields,
    IReadOnlyList<string> Lines,
    bool Filed,
    bool Processing,
    bool Failed);

public static class DemoPdf
{
    public static byte[] Create(string title, IReadOnlyList<string> lines)
    {
        var content = new StringBuilder("BT /F1 12 Tf 50 740 Td ");
        foreach (var line in new[] { title }.Concat(lines).Take(28))
        {
            content.Append('(').Append(Escape(line)).Append(") Tj 0 -18 Td ");
        }

        content.Append("ET");
        var stream = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}\nendstream"
        };
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(output, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal)
        .Replace("€", "EUR", StringComparison.Ordinal);

    private static void WriteAscii(Stream output, string value) =>
        output.Write(Encoding.ASCII.GetBytes(value));
}
