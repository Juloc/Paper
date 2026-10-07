using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Export;
using Paper.Web.Features.Import;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Search;
using Paper.Web.Features.Shelf;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;

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

            var shelfFolders = new ShelfFolderStore(db, TimeProvider.System, storage);
            var renamed = await shelfFolders.UpdateLocationAsync(folder.Id, null, "Energie", CancellationToken.None);

            Assert.IsTrue(renamed.Succeeded);
            filedDocument = await db.Documents.SingleAsync();
            Assert.AreEqual("Energie/2026-10-07 Stadtwerke Rechnung.pdf", filedDocument.FilePath);
            Assert.IsFalse(storage.FileExists("Wohnung/Strom/2026-10-07 Stadtwerke Rechnung.pdf"));
            Assert.IsTrue(storage.FileExists(filedDocument.FilePath));
            StringAssert.Contains(filedDocument.SearchText, "Energie");
            Assert.IsFalse(filedDocument.SearchText.Contains("Wohnung/Strom", StringComparison.Ordinal));

            var secondFolder = new ShelfFolder
            {
                Name = "Archiv",
                RelativePath = "Archiv",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ShelfFolders.Add(secondFolder);
            await db.SaveChangesAsync();
            storage.EnsureDirectory(secondFolder.RelativePath);
            var moved = await filing.MoveToShelfAsync(filedDocument.Id, secondFolder.Id, CancellationToken.None);

            Assert.IsTrue(moved.Succeeded);
            var movedDocument = await db.Documents.SingleAsync();
            Assert.AreEqual(DocumentStatus.Filed, movedDocument.Status);
            Assert.AreEqual(secondFolder.Id, movedDocument.ShelfFolderId);
            Assert.AreEqual("Archiv/2026-10-07 Stadtwerke Rechnung.pdf", movedDocument.FilePath);
            Assert.IsFalse(storage.FileExists("Wohnung/Strom/2026-10-07 Stadtwerke Rechnung.pdf"));
            Assert.IsTrue(storage.FileExists(movedDocument.FilePath));
            StringAssert.Contains(movedDocument.SearchText, "Archiv");

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
            .AddSingleton(new ConsumeFailureStore(db, TimeProvider.System))
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
            Assert.AreEqual(1, await db.ConsumeFailures.CountAsync());
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

    [TestMethod]
    public async Task PaperlessImportPersistsCatalogsTagsAndCustomFieldsInTheInbox()
    {
        var root = CreateStorageRoot();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();
        var storage = CreateStorage(root);
        var importer = new PaperlessImportService(
            db,
            storage,
            TimeProvider.System,
            NullLogger<PaperlessImportService>.Instance);

        try
        {
            await using var archive = CreatePaperlessArchive();
            var result = await importer.ImportAsync(archive, archive.Length, CancellationToken.None);

            Assert.AreEqual(1, result.Imported);
            Assert.AreEqual(0, result.Skipped);
            Assert.IsEmpty(result.Errors);
            var document = await db.Documents
                .Include(item => item.Correspondent)
                .Include(item => item.DocumentType)
                .Include(item => item.Tags).ThenInclude(item => item.Tag)
                .Include(item => item.CustomFields).ThenInclude(item => item.CustomField)
                .SingleAsync();
            Assert.AreEqual("Stadtwerke Rechnung", document.Title);
            Assert.AreEqual(new DateOnly(2026, 10, 5), document.DocumentDate);
            Assert.AreEqual("Stadtwerke Mannheim", document.Correspondent!.Name);
            Assert.AreEqual("Rechnung", document.DocumentType!.Name);
            Assert.AreEqual("energie", document.Tags.Single().Tag.Name);
            Assert.AreEqual("RE-42", document.CustomFields.Single().Value);
            Assert.AreEqual(DocumentStatus.Inbox, document.Status);
            Assert.IsTrue(storage.FileExists(document.FilePath));
            Assert.AreEqual(0, await db.ProcessingJobs.CountAsync());

            await using var duplicateArchive = CreatePaperlessArchive();
            var duplicate = await importer.ImportAsync(duplicateArchive, duplicateArchive.Length, CancellationToken.None);
            Assert.AreEqual(0, duplicate.Imported);
            Assert.AreEqual(1, duplicate.Skipped);
            Assert.AreEqual(1, await db.Documents.CountAsync());
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [TestMethod]
    public async Task BackupRestoreRoundTripKeepsFiledDocumentAndHumanReadablePath()
    {
        var sourceRoot = CreateStorageRoot();
        var restoreRoot = CreateStorageRoot();
        await using var sourceConnection = new SqliteConnection("Data Source=:memory:");
        await sourceConnection.OpenAsync();
        await using var sourceDb = CreateDatabase(sourceConnection);
        await sourceDb.Database.EnsureCreatedAsync();
        var sourceStorage = CreateStorage(sourceRoot);
        var importer = new DocumentImportService(
            sourceDb,
            sourceStorage,
            TimeProvider.System,
            NullLogger<DocumentImportService>.Instance);
        try
        {
            var imported = await importer.ImportAsync(
                new MemoryStream("%PDF-backup"u8.ToArray()),
                "strom.pdf",
                "application/pdf",
                11,
                CancellationToken.None);
            Assert.IsTrue(imported.Success);
            var folder = new ShelfFolder
            {
                Name = "Strom",
                RelativePath = "Wohnung/Strom",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            sourceDb.ShelfFolders.Add(folder);
            await sourceDb.SaveChangesAsync();
            sourceStorage.EnsureDirectory(folder.RelativePath);
            var filing = new DocumentFilingService(
                sourceDb,
                sourceStorage,
                TimeProvider.System,
                NullLogger<DocumentFilingService>.Instance);
            var filed = await filing.SaveAsync(
                imported.DocumentId!.Value,
                new DocumentEdit(
                    "Stromrechnung",
                    new DateOnly(2026, 10, 5),
                    null,
                    null,
                    folder.Id,
                    "energie",
                    new Dictionary<long, string>()),
                fileFromInbox: true,
                CancellationToken.None);
            Assert.IsTrue(filed.Succeeded);

            await using var backup = new MemoryStream();
            await new DocumentBackupService(sourceDb, sourceStorage).WriteZipAsync(backup, CancellationToken.None);
            backup.Position = 0;

            await using var restoreConnection = new SqliteConnection("Data Source=:memory:");
            await restoreConnection.OpenAsync();
            await using var restoreDb = CreateDatabase(restoreConnection);
            await restoreDb.Database.EnsureCreatedAsync();
            var restoreStorage = CreateStorage(restoreRoot);
            var restoreFiling = new DocumentFilingService(
                restoreDb,
                restoreStorage,
                TimeProvider.System,
                NullLogger<DocumentFilingService>.Instance);
            var restore = await new DocumentRestoreService(
                restoreDb,
                restoreStorage,
                restoreFiling,
                TimeProvider.System,
                NullLogger<DocumentRestoreService>.Instance)
                .RestoreAsync(backup, backup.Length, CancellationToken.None);

            Assert.AreEqual(1, restore.Imported);
            Assert.IsEmpty(restore.Errors);
            var restored = await restoreDb.Documents.SingleAsync();
            Assert.AreEqual(DocumentStatus.Filed, restored.Status);
            Assert.AreEqual("Wohnung/Strom/2026-10-05 Stromrechnung.pdf", restored.FilePath);
            Assert.IsTrue(restoreStorage.FileExists(restored.FilePath));
            Assert.AreEqual(0, await restoreDb.ShelfFolders.CountAsync(folder => folder.RelativePath == "inbox"));
        }
        finally
        {
            DeleteStorageRoot(sourceRoot);
            DeleteStorageRoot(restoreRoot);
        }
    }

    [TestMethod]
    public async Task ShelfFolderPathsRejectCaseOnlyPhysicalCollisions()
    {
        var root = CreateStorageRoot();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();
        var storage = CreateStorage(root);
        var folders = new ShelfFolderStore(db, TimeProvider.System, storage);

        try
        {
            var first = await folders.CreateAsync(null, "Strom", CancellationToken.None);
            var duplicate = await folders.CreateAsync(null, "strom", CancellationToken.None);

            Assert.IsNotNull(first);
            Assert.IsNull(duplicate);
            Assert.AreEqual("STROM", first.RelativePathKey);
            Assert.AreEqual(1, await db.ShelfFolders.CountAsync());

            var caseOnlyRename = await folders.UpdateLocationAsync(first.Id, null, "strom", CancellationToken.None);

            Assert.IsFalse(caseOnlyRename.Succeeded);
            Assert.IsTrue(caseOnlyRename.Error?.Contains("Groß-/Kleinschreibung", StringComparison.Ordinal) == true);
            Assert.AreEqual("Strom", (await db.ShelfFolders.SingleAsync()).Name);
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [TestMethod]
    public async Task SearchShelfFilterIncludesDocumentsInDescendantFolders()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();

        var parent = new ShelfFolder { Name = "Wohnung", RelativePath = "Wohnung", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var child = new ShelfFolder { Name = "Strom", RelativePath = "Wohnung/Strom", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var other = new ShelfFolder { Name = "Auto", RelativePath = "Auto", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.ShelfFolders.AddRange(parent, child, other);
        db.Documents.AddRange(
            NewFiledDocument("direct.pdf", parent, "direct", new DateOnly(2026, 10, 1)),
            NewFiledDocument("nested.pdf", child, "nested", new DateOnly(2026, 10, 5)),
            NewFiledDocument("other.pdf", other, "other", null));
        await db.SaveChangesAsync();

        var search = new DocumentSearchService(db);
        var page = await search.SearchAsync(new SearchCriteria("", null, null, parent.Id, null, null, null, null, null), 1, CancellationToken.None);

        Assert.AreEqual(2, page.TotalCount);
        CollectionAssert.AreEqual(new[] { "nested", "direct" }, page.Results.Select(result => result.Title).ToArray());
    }

    [TestMethod]
    public async Task TagStoreManagesTagsAndCascadesDocumentLinks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDatabase(connection);
        await db.Database.EnsureCreatedAsync();

        var tags = new TagStore(db, TimeProvider.System);
        var created = await tags.CreateAsync(" Energie ", CancellationToken.None);
        var duplicate = await tags.CreateAsync("ENERGIE", CancellationToken.None);

        Assert.IsNotNull(created);
        Assert.AreEqual("energie", created.Name);
        Assert.AreEqual(created.Id, duplicate!.Id);

        var document = NewDocument("energie.pdf", DateTime.UtcNow);
        document.Tags.Add(new DocumentTag { Document = document, Tag = created });
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var listed = (await tags.ListAsync(CancellationToken.None)).Single();
        Assert.AreEqual("energie", listed.Name);
        Assert.AreEqual(1, listed.DocumentCount);

        Assert.IsTrue(await tags.DeleteAsync(created.Id, CancellationToken.None));
        Assert.AreEqual(0, await db.Tags.CountAsync());
        Assert.AreEqual(0, await db.DocumentTags.CountAsync());
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

    private static Document NewFiledDocument(string fileName, ShelfFolder folder, string title, DateOnly? documentDate) => new()
    {
        Title = title,
        DocumentDate = documentDate,
        OriginalFileName = fileName,
        FilePath = $"{folder.RelativePath}/{fileName}",
        FileSize = 9,
        Hash = Guid.NewGuid().ToString("N").PadRight(64, 'b'),
        OcrStatus = OcrStatus.Completed,
        Status = DocumentStatus.Filed,
        ShelfFolder = folder,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SearchText = $"{title} {fileName} {folder.RelativePath}"
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

    private static MemoryStream CreatePaperlessArchive()
    {
        var manifest = JsonSerializer.Serialize(new object[]
        {
            new { model = "documents.tag", pk = 1, fields = new { name = "Energie" } },
            new { model = "documents.correspondent", pk = 2, fields = new { name = "Stadtwerke Mannheim" } },
            new { model = "documents.doctype", pk = 3, fields = new { name = "Rechnung" } },
            new { model = "documents.customfield", pk = 4, fields = new { name = "Rechnungsnummer", data_type = "string" } },
            new
            {
                model = "documents.document",
                pk = 10,
                fields = new
                {
                    title = "Stadtwerke Rechnung",
                    document_date = "2026-10-05",
                    original_filename = "rechnung.pdf",
                    filename = "originals/rechnung.pdf",
                    correspondent = 2,
                    document_type = 3,
                    tags = new[] { 1 },
                    content = "Stadtwerke Mannheim Rechnung",
                    custom_fields = new[] { new { field = 4, value = "RE-42" } }
                }
            }
        });
        var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifestEntry = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                manifestEntry.Write(manifest);
            }

            using var documentEntry = archive.CreateEntry("originals/rechnung.pdf").Open();
            documentEntry.Write("%PDF-paperless"u8);
        }

        archiveStream.Position = 0;
        return archiveStream;
    }

    private static void DeleteStorageRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
