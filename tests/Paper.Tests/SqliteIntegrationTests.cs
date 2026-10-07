using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;

namespace Paper.Tests;

[TestClass]
public sealed class SqliteIntegrationTests
{
    [TestMethod]
    public async Task AppModelCanCreateAndPersistARealDocumentDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);

        await db.Database.EnsureCreatedAsync();
        db.Documents.Add(new Document
        {
            Title = "SQLite-Test",
            OriginalFileName = "test.pdf",
            FilePath = "inbox/test.pdf",
            FileSize = 9,
            Hash = "a".PadLeft(64, 'a'),
            OcrStatus = OcrStatus.Pending,
            Status = DocumentStatus.Inbox,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SearchText = "SQLite-Test test.pdf"
        });

        await db.SaveChangesAsync();

        Assert.AreEqual(1, await db.Documents.CountAsync());
    }

    [TestMethod]
    public async Task ImportPersistsDurableJobAndCleansUpDuplicateFiles()
    {
        var root = CreateStorageRoot();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();
        var storage = CreateStorage(root);
        var importer = new DocumentImportService(
            db,
            storage,
            TimeProvider.System,
            NullLogger<DocumentImportService>.Instance);

        try
        {
            var first = await importer.ImportAsync(
                new MemoryStream("%PDF-test"u8.ToArray()),
                "invoice.pdf",
                "application/pdf",
                9,
                CancellationToken.None);

            Assert.IsTrue(first.Success);
            Assert.AreEqual(1, await db.Documents.CountAsync());
            Assert.AreEqual(1, await db.ProcessingJobs.CountAsync());
            var storedPath = await db.Documents.Select(document => document.FilePath).SingleAsync();
            Assert.IsTrue(storage.FileExists(storedPath));

            var duplicate = await importer.ImportAsync(
                new MemoryStream("%PDF-test"u8.ToArray()),
                "renamed-invoice.pdf",
                "application/pdf",
                9,
                CancellationToken.None);

            Assert.IsFalse(duplicate.Success);
            Assert.AreEqual(1, await db.Documents.CountAsync());
            var duplicatePath = StoragePathPolicy.CreateInboxPath(
                await db.Documents.Select(document => document.Hash).SingleAsync(),
                "renamed-invoice.pdf");
            Assert.IsFalse(storage.FileExists(duplicatePath));
            Assert.IsTrue(storage.FileExists(storedPath));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [TestMethod]
    public async Task FilingAndDeletionKeepDatabaseAndPhysicalStorageInSync()
    {
        var root = CreateStorageRoot();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();
        var storage = CreateStorage(root);

        try
        {
            var importer = new DocumentImportService(
                db,
                storage,
                TimeProvider.System,
                NullLogger<DocumentImportService>.Instance);
            var imported = await importer.ImportAsync(
                new MemoryStream("%PDF-test"u8.ToArray()),
                "invoice.pdf",
                "application/pdf",
                9,
                CancellationToken.None);
            Assert.IsTrue(imported.Success);

            var folder = new ShelfFolder
            {
                Name = "Strom",
                RelativePath = "Wohnung/Strom",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ShelfFolders.Add(folder);
            await db.SaveChangesAsync();
            storage.EnsureDirectory(folder.RelativePath);

            var filing = new DocumentFilingService(
                db,
                storage,
                TimeProvider.System,
                NullLogger<DocumentFilingService>.Instance);
            var saved = await filing.SaveAsync(
                imported.DocumentId!.Value,
                new DocumentEdit(
                    "Stadtwerke Rechnung",
                    new DateOnly(2026, 10, 7),
                    null,
                    null,
                    folder.Id,
                    "energie",
                    new Dictionary<long, string>()),
                fileFromInbox: true,
                CancellationToken.None);

            Assert.IsTrue(saved.Succeeded);
            var filedDocument = await db.Documents.SingleAsync();
            Assert.AreEqual(DocumentStatus.Filed, filedDocument.Status);
            Assert.AreEqual("Wohnung/Strom/2026-10-07 Stadtwerke Rechnung.pdf", filedDocument.FilePath);
            Assert.IsTrue(storage.FileExists(filedDocument.FilePath));
            Assert.IsFalse(storage.FileExists("inbox/" + filedDocument.Hash[..12] + " invoice.pdf"));

            var documents = new DocumentStore(
                db,
                TimeProvider.System,
                storage,
                NullLogger<DocumentStore>.Instance);
            var deleted = await documents.DeleteAsync(filedDocument.Id, CancellationToken.None);

            Assert.IsTrue(deleted.Succeeded);
            Assert.AreEqual(0, await db.Documents.CountAsync());
            Assert.IsFalse(storage.FileExists(filedDocument.FilePath));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    private static DbContextOptions<AppDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static AppDbContext CreateDatabase(SqliteConnection connection) => new(CreateOptions(connection));

    private static LocalDocumentStorage CreateStorage(string root)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:RootPath"] = root
        }).Build();
        return new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);
    }

    private static string CreateStorageRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteStorageRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
