using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Search;
using Paper.Web.Features.Import;

namespace Paper.Tests;

[TestClass]
public sealed class StorageAndAnalysisTests
{
    [TestMethod]
    public async Task StorageComputesStableHashAndRejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = root
            }).Build();
            var storage = new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);
            await using var content = new MemoryStream("%PDF-test"u8.ToArray());
            var result = await storage.SaveAsync(content, "invoice.pdf", CancellationToken.None);

            Assert.AreEqual("3c87d37f1dbea6909f917ce437c390fb8e655a774387d9e69301c0b2283d5b63", result.Hash);
            Assert.IsTrue(result.RelativePath.StartsWith("inbox/", StringComparison.Ordinal));
            Assert.ThrowsExactly<ArgumentException>(() => storage.GetSafePath("../secrets.pdf"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task StorageMovesDocumentsToHumanReadableShelfAndHandlesCollisions()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = root
            }).Build();
            var storage = new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);
            await using var first = new MemoryStream("%PDF-one"u8.ToArray());
            await using var second = new MemoryStream("%PDF-two"u8.ToArray());
            var firstStored = await storage.SaveAsync(first, "rechnung.pdf", CancellationToken.None);
            var secondStored = await storage.SaveAsync(second, "rechnung.pdf", CancellationToken.None);

            var firstPath = await storage.MoveToShelfAsync(firstStored.RelativePath, "Wohnung/Strom", new DateOnly(2026, 10, 5), "Stadtwerke Mannheim Rechnung", "rechnung.pdf", CancellationToken.None);
            var secondPath = await storage.MoveToShelfAsync(secondStored.RelativePath, "Wohnung/Strom", new DateOnly(2026, 10, 5), "Stadtwerke Mannheim Rechnung", "rechnung.pdf", CancellationToken.None);

            Assert.AreEqual("Wohnung/Strom/2026-10-05 Stadtwerke Mannheim Rechnung.pdf", firstPath);
            Assert.AreEqual("Wohnung/Strom/2026-10-05 Stadtwerke Mannheim Rechnung (2).pdf", secondPath);
            Assert.IsTrue(File.Exists(storage.GetSafePath(firstPath)));
            Assert.IsTrue(File.Exists(storage.GetSafePath(secondPath)));

            var renamedPath = await storage.MoveToShelfAsync(firstPath, "Wohnung/Strom", new DateOnly(2026, 10, 5), "Stadtwerke Mannheim Abschlag", "rechnung.pdf", CancellationToken.None);
            Assert.AreEqual("Wohnung/Strom/2026-10-05 Stadtwerke Mannheim Abschlag.pdf", renamedPath);
            Assert.IsFalse(File.Exists(storage.GetSafePath(firstPath)));
            Assert.IsTrue(File.Exists(storage.GetSafePath(renamedPath)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task StorageDoesNotDeleteExistingFileWhenSameContentIsSavedAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = root
            }).Build();
            var storage = new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);
            await using var first = new MemoryStream("%PDF-same"u8.ToArray());
            await using var second = new MemoryStream("%PDF-same"u8.ToArray());
            var original = await storage.SaveAsync(first, "same.pdf", CancellationToken.None);
            var duplicate = await storage.SaveAsync(second, "same.pdf", CancellationToken.None);

            Assert.IsTrue(duplicate.AlreadyExisted);
            Assert.IsTrue(File.Exists(storage.GetSafePath(original.RelativePath)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task StorageDoesNotSilentlyIgnoreMissingShelfDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = root
            }).Build();
            var storage = new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);

            storage.EnsureDirectory("Wohnung/Leer");
            Assert.IsTrue(storage.DirectoryExists("Wohnung/Leer"));
            await storage.MoveDirectoryAsync("Wohnung/Leer", "Wohnung/Archiv", CancellationToken.None);

            Assert.IsFalse(storage.DirectoryExists("Wohnung/Leer"));
            Assert.IsTrue(storage.DirectoryExists("Wohnung/Archiv"));
            await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(() =>
                storage.MoveDirectoryAsync("Wohnung/Fehlt", "Wohnung/Archiv2", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void StoragePolicyRejectsInboxAsShelfAndTraversal()
    {
        Assert.ThrowsExactly<ArgumentException>(() => StoragePathPolicy.NormalizeFolderPath("inbox"));
        Assert.ThrowsExactly<ArgumentException>(() => StoragePathPolicy.NormalizeFolderPath("Wohnung/../Secrets"));
        Assert.ThrowsExactly<ArgumentException>(() => StoragePathPolicy.NormalizeFolderPath("C:/Secrets"));
        Assert.AreEqual("undated Vertrag.pdf", StoragePathPolicy.CreateShelfFileName(null, "Vertrag", "scan.pdf"));
    }

    [TestMethod]
    public void AnalyzerFindsDateTitleAndOneCanonicalTag()
    {
        var result = new DocumentAnalyzer().Analyze("scan", "Rechnung März\nRechnungsnummer 4\n12.03.2026\nBetrag 42 EUR");

        Assert.AreEqual("Rechnung März", result.Title);
        Assert.AreEqual(new DateOnly(2026, 3, 12), result.DocumentDate);
        CollectionAssert.Contains(result.SuggestedTags.ToList(), "rechnung");
        Assert.AreEqual("Rechnung", result.SuggestedDocumentType);
    }

    [TestMethod]
    public void AnalyzerExtractsConservativeCustomFieldValues()
    {
        var result = new DocumentAnalyzer().Analyze("scan", "Rechnungsnummer: RE-2026/42\nKundennummer: K-1234\nIBAN: DE89 3704 0044 0532 0130 00\nBetrag: 1.234,50 EUR");

        Assert.AreEqual("RE-2026/42", result.SuggestedCustomFields["Rechnungsnummer"]);
        Assert.AreEqual("K-1234", result.SuggestedCustomFields["Kundennummer"]);
        Assert.AreEqual("DE89 3704 0044 0532 0130 00", result.SuggestedCustomFields["IBAN"]);
        Assert.AreEqual("1.234,50", result.SuggestedCustomFields["Betrag"]);
    }

    [TestMethod]
    public void SearchTextCombinesTitleOcrAndTags()
    {
        var document = new Document { Title = "Strom", OcrText = "Januar", Tags = [new DocumentTag { Tag = new Tag { Name = "vertrag" } }] };

        Assert.AreEqual("Strom Januar vertrag", TagStore.BuildSearchText(document));
    }

    [TestMethod]
    public void ProcessingStatesExposeDurableLifecycle()
    {
        Assert.AreEqual("Pending", ProcessingJobState.Pending.ToString());
        Assert.AreEqual("Failed", OcrStatus.Failed.ToString());
        Assert.AreEqual("OcrAndAnalyze", ProcessingJobType.OcrAndAnalyze.ToString());
    }

    [TestMethod]
    public void ProcessingSummarySeparatesActiveAndFailedJobs()
    {
        var summary = new ProcessingSummary(2, 1, 3);

        Assert.AreEqual(3, summary.Active);
        Assert.AreEqual(3, summary.Failed);
    }

    [TestMethod]
    public void ModelDefinesShelfMetadataAndCustomFieldConstraints()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=paper")
            .Options;
        using var db = new AppDbContext(options);

        var shelf = db.Model.FindEntityType(typeof(ShelfFolder))!;
        Assert.IsTrue(shelf.GetIndexes().Any(index => index.IsUnique && index.Properties.Single().Name == nameof(ShelfFolder.RelativePath)));
        Assert.AreEqual(DeleteBehavior.Restrict, shelf.FindNavigation(nameof(ShelfFolder.Parent))!.ForeignKey.DeleteBehavior);

        var customValue = db.Model.FindEntityType(typeof(DocumentCustomFieldValue))!;
        CollectionAssert.AreEquivalent(
            new[] { nameof(DocumentCustomFieldValue.DocumentId), nameof(DocumentCustomFieldValue.CustomFieldId) },
            customValue.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.AreEqual(DeleteBehavior.Cascade, customValue.FindNavigation(nameof(DocumentCustomFieldValue.Document))!.ForeignKey.DeleteBehavior);
    }

    [TestMethod]
    public void SearchCriteriaSupportsFilterOnlySearches()
    {
        var criteria = new SearchCriteria("", null, null, 4, null, null, null, null, null);
        Assert.IsTrue(criteria.HasFilters);
        Assert.IsTrue(new SearchCriteria("", null, null, null, null, null, null, 3, "").HasFilters);
        Assert.IsFalse(new SearchCriteria("", null, null, null, null, null, null, null, null).HasFilters);
    }

    [TestMethod]
    public void StorageOptionsSelectsSmbRootWithoutChangingLocalDefault()
    {
        var options = new StorageOptions { Provider = "smb", SmbRootPath = "\\\\nas\\paper" };
        Assert.AreEqual("\\\\nas\\paper", options.EffectiveRootPath());
        Assert.AreEqual("data/documents", new StorageOptions { RootPath = "data/documents" }.EffectiveRootPath());
        Assert.ThrowsExactly<InvalidOperationException>(() => new StorageOptions { Provider = "smb" }.EffectiveRootPath());
        Assert.ThrowsExactly<InvalidOperationException>(() => new StorageOptions { Provider = "ftp" }.EffectiveRootPath());
    }

    [TestMethod]
    public async Task PaperlessImporterAcceptsEmptySingleManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            await using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                await writer.WriteAsync("[]");
            }

            zip.Position = 0;
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=paper")
                .Options;
            using var db = new AppDbContext(options);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:RootPath"] = root
            }).Build();
            var storage = new LocalDocumentStorage(configuration, NullLogger<LocalDocumentStorage>.Instance);
            var importer = new PaperlessImportService(db, storage, TimeProvider.System, NullLogger<PaperlessImportService>.Instance);

            var result = await importer.ImportAsync(zip, zip.Length, CancellationToken.None);

            Assert.AreEqual(0, result.Imported);
            Assert.AreEqual(0, result.Skipped);
            Assert.AreEqual(0, result.Errors.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void EmailExtractorFindsAndDecodesPdfAttachment()
    {
        var message = """
            From: sender@example.test
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="paper-boundary"

            --paper-boundary
            Content-Type: text/plain; charset=utf-8

            Siehe Anhang.
            --paper-boundary
            Content-Type: application/pdf; name="rechnung.pdf"
            Content-Disposition: attachment; filename="rechnung.pdf"
            Content-Transfer-Encoding: base64

            JVBERi0=
            --paper-boundary--
            """;

        var attachments = new EmailAttachmentExtractor().Extract(Encoding.ASCII.GetBytes(message.Replace("\r\n", "\n")));

        Assert.AreEqual(1, attachments.Count);
        Assert.AreEqual("rechnung.pdf", attachments[0].FileName);
        CollectionAssert.AreEqual("%PDF-"u8.ToArray(), attachments[0].Content);
    }

    [TestMethod]
    public void MailAccountOptionsRejectIncompleteConfiguration()
    {
        var options = new MailAccountOptions();

        Assert.IsFalse(options.IsConfigured(out var error));
        StringAssert.Contains(error, "host");

        options.Host = "imap.example.test";
        options.Username = "user";
        options.Password = "secret";
        Assert.IsTrue(options.IsConfigured(out error), error);
    }

    [TestMethod]
    public void LearningTermsAreStableAndIgnoreCommonWords()
    {
        var terms = DocumentLearningStore.ExtractTerms("Stadtwerke Mannheim Rechnung und eine Rechnung");

        CollectionAssert.AreEqual(new[] { "stadtwerke", "mannheim", "rechnung" }, terms.ToArray());
    }

    [TestMethod]
    public void MailAccountOptionsNormalizeAttachmentFilters()
    {
        var options = new MailAccountOptions { AttachmentExtensions = "pdf, .JPG, invalid extension" };

        var extensions = options.AllowedAttachmentExtensions();

        CollectionAssert.AreEquivalent(new[] { ".pdf", ".jpg" }, extensions.ToArray());
    }
}
