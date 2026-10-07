using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Import;
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
            var deferred = await db.Documents.SingleAsync();
            deferred.Status = DocumentStatus.Deferred;
            await db.SaveChangesAsync();
            var suggestion = await filing.SaveAsync(
                imported.DocumentId!.Value,
                new DocumentEdit(
                    "Stadtwerke Rechnung",
                    new DateOnly(2026, 10, 7),
                    null,
                    null,
                    folder.Id,
                    "energie",
                    new Dictionary<long, string>()),
                fileFromInbox: false,
                CancellationToken.None);

            Assert.IsTrue(suggestion.Succeeded);
            var suggestedDocument = await db.Documents.SingleAsync();
            Assert.AreEqual(DocumentStatus.Deferred, suggestedDocument.Status);
            Assert.IsNull(suggestedDocument.ShelfFolderId);
            Assert.AreEqual(folder.Id, suggestedDocument.SuggestedShelfFolderId);
            Assert.IsTrue(storage.FileExists(suggestedDocument.FilePath));

            suggestedDocument.Status = DocumentStatus.Inbox;
            await db.SaveChangesAsync();
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

    [TestMethod]
    public async Task ProcessingRecoveryRequeuesInterruptedJobsAndManualRetryResetsFailures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var retryDocument = NewDocument("retry.pdf", now);
        var failedDocument = NewDocument("failed.pdf", now);
        db.Documents.AddRange(retryDocument, failedDocument);
        db.ProcessingJobs.AddRange(
            new ProcessingJob
            {
                Document = retryDocument,
                Type = ProcessingJobType.OcrAndAnalyze,
                State = ProcessingJobState.Running,
                Attempts = 1,
                Priority = 10,
                CreatedAt = now,
                StartedAt = now
            },
            new ProcessingJob
            {
                Document = failedDocument,
                Type = ProcessingJobType.OcrAndAnalyze,
                State = ProcessingJobState.Running,
                Attempts = 3,
                Priority = 10,
                CreatedAt = now,
                StartedAt = now
            });
        await db.SaveChangesAsync();

        var jobs = new ProcessingJobStore(db, TimeProvider.System);
        Assert.AreEqual(1, await jobs.RequeueInterruptedAsync(CancellationToken.None));

        db.ChangeTracker.Clear();
        var recovered = await db.ProcessingJobs.OrderBy(job => job.Id).ToListAsync();
        Assert.AreEqual(ProcessingJobState.Pending, recovered[0].State);
        Assert.AreEqual(ProcessingJobState.Failed, recovered[1].State);
        Assert.AreEqual(OcrStatus.Pending, (await db.Documents.SingleAsync(document => document.Id == retryDocument.Id)).OcrStatus);
        Assert.AreEqual(OcrStatus.Failed, (await db.Documents.SingleAsync(document => document.Id == failedDocument.Id)).OcrStatus);

        var status = new ProcessingStatusStore(db, TimeProvider.System);
        Assert.IsTrue(await status.RetryAsync(recovered[1].Id, CancellationToken.None));
        var reset = await db.ProcessingJobs.SingleAsync(job => job.Id == recovered[1].Id);
        Assert.AreEqual(ProcessingJobState.Pending, reset.State);
        Assert.AreEqual(0, reset.Attempts);
        Assert.AreEqual(OcrStatus.Pending, (await db.Documents.SingleAsync(document => document.Id == failedDocument.Id)).OcrStatus);
    }

    [TestMethod]
    public async Task ConsumeImportsStableFilesAndQuarantinesDuplicatesWithoutRetryLoop()
    {
        var storageRoot = CreateStorageRoot();
        var consumeRoot = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(consumeRoot);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();
        var storage = CreateStorage(storageRoot);
        var importer = new DocumentImportService(
            db,
            storage,
            TimeProvider.System,
            NullLogger<DocumentImportService>.Instance);
        using var services = new ServiceCollection()
            .AddSingleton(importer)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Consume:RootPath"] = consumeRoot,
            ["Consume:EmailAttachments"] = "false"
        }).Build();
        var worker = new ConsumeDirectoryWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            new EmailAttachmentExtractor(),
            TimeProvider.System,
            NullLogger<ConsumeDirectoryWorker>.Instance);
        var sourcePath = Path.Combine(consumeRoot, "rechnung.pdf");

        try
        {
            await File.WriteAllBytesAsync(sourcePath, "%PDF-consume"u8.ToArray());
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(-10));
            await worker.RunOnceAsync(CancellationToken.None);

            Assert.AreEqual(1, await db.Documents.CountAsync());
            Assert.IsFalse(File.Exists(sourcePath));
            Assert.IsEmpty(Directory.EnumerateFiles(Path.Combine(consumeRoot, ".processing")));

            var duplicatePath = Path.Combine(consumeRoot, "duplicate.pdf");
            await File.WriteAllBytesAsync(duplicatePath, "%PDF-consume"u8.ToArray());
            File.SetLastWriteTimeUtc(duplicatePath, DateTime.UtcNow.AddSeconds(-10));
            await worker.RunOnceAsync(CancellationToken.None);
            await worker.RunOnceAsync(CancellationToken.None);

            Assert.AreEqual(1, await db.Documents.CountAsync());
            var failedFiles = Directory.EnumerateFiles(Path.Combine(consumeRoot, "failed")).ToArray();
            Assert.AreEqual(2, failedFiles.Length);
            Assert.IsTrue(failedFiles.Any(path => path.EndsWith(".error.txt", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(failedFiles.Any(path => path.EndsWith("_duplicate.pdf", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
            DeleteStorageRoot(consumeRoot);
        }
    }

    private static DbContextOptions<AppDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

    private static AppDbContext CreateDatabase(SqliteConnection connection) => new(CreateOptions(connection));

    private static Document NewDocument(string fileName, DateTime now) => new()
    {
        Title = Path.GetFileNameWithoutExtension(fileName),
        OriginalFileName = fileName,
        FilePath = $"inbox/{fileName}",
        FileSize = 9,
        Hash = Guid.NewGuid().ToString("N").PadRight(64, 'a'),
        OcrStatus = OcrStatus.Processing,
        Status = DocumentStatus.Inbox,
        CreatedAt = now,
        UpdatedAt = now,
        SearchText = fileName
    };

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
