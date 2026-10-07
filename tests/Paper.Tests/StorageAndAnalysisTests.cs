using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Documents;
using Paper.Web.Features.Export;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Search;
using Paper.Web.Features.Import;
using Paper.Web.Features.Auth;

namespace Paper.Tests;

[TestClass]
public sealed class StorageAndAnalysisTests
{
    [TestMethod]
    public void ImapClientParsesUidValidityFromSelectResponse()
    {
        Assert.AreEqual(385752904L, ImapClient.ParseUidValidity(
            ["* OK [UIDVALIDITY 385752904]", "A0001 OK SELECT completed"]));
        Assert.IsNull(ImapClient.ParseUidValidity(["* FLAGS (\\Seen)"]));
    }

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
            Assert.IsTrue(storage.FileExists(result.RelativePath));
            Assert.AreEqual(9, storage.GetFileMetadata(result.RelativePath)!.Length);
            Assert.IsFalse(storage.FileExists("inbox/missing.pdf"));
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
    public async Task StorageUsesUniqueShelfNamesForConcurrentMoves()
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
            await using var first = new MemoryStream("%PDF-race-one"u8.ToArray());
            await using var second = new MemoryStream("%PDF-race-two"u8.ToArray());
            var firstStored = await storage.SaveAsync(first, "rechnung.pdf", CancellationToken.None);
            var secondStored = await storage.SaveAsync(second, "rechnung.pdf", CancellationToken.None);

            var results = await Task.WhenAll(
                storage.MoveToShelfAsync(firstStored.RelativePath, "Wohnung/Strom", new DateOnly(2026, 10, 5), "Stadtwerke Rechnung", "rechnung.pdf", CancellationToken.None),
                storage.MoveToShelfAsync(secondStored.RelativePath, "Wohnung/Strom", new DateOnly(2026, 10, 5), "Stadtwerke Rechnung", "rechnung.pdf", CancellationToken.None));

            Assert.AreNotEqual(results[0], results[1]);
            Assert.IsTrue(results.All(storage.FileExists));
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
    public async Task StorageTreatsConcurrentSameHashUploadsAsDuplicates()
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
            var uploads = Enumerable.Range(0, 8).Select(async _ =>
            {
                await using var content = new MemoryStream("%PDF-concurrent"u8.ToArray());
                return await storage.SaveAsync(content, "parallel.pdf", CancellationToken.None);
            });

            var results = await Task.WhenAll(uploads);

            Assert.AreEqual(1, results.Count(result => !result.AlreadyExisted));
            Assert.IsTrue(results.All(result => result.AlreadyExisted || storage.FileExists(result.RelativePath)));
            Assert.IsTrue(storage.FileExists(results[0].RelativePath));
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
    public async Task StorageMovesInboxFileWhenTheShortHashPathIsOccupiedByOtherContent()
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
            var content = "%PDF-collision"u8.ToArray();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
            var occupiedPath = StoragePathPolicy.CreateInboxPath(hash, "collision.pdf");
            var occupiedFullPath = storage.GetSafePath(occupiedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(occupiedFullPath)!);
            await File.WriteAllBytesAsync(occupiedFullPath, "%PDF-other"u8.ToArray());

            await using var source = new MemoryStream(content, writable: false);
            var stored = await storage.SaveAsync(source, "collision.pdf", CancellationToken.None);

            Assert.AreNotEqual(occupiedPath, stored.RelativePath);
            Assert.IsTrue(stored.RelativePath.Contains("(2)", StringComparison.Ordinal));
            Assert.IsTrue(storage.FileExists(occupiedPath));
            Assert.IsTrue(storage.FileExists(stored.RelativePath));
            CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(storage.GetSafePath(stored.RelativePath)));
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
    public async Task StorageCanMoveAndRestoreAFileThroughTheTrashPath()
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
            var stored = await storage.SaveAsync(content, "trash-test.pdf", CancellationToken.None);

            await storage.MoveAsync(stored.RelativePath, ".trash/test_trash-test.pdf", CancellationToken.None);
            Assert.IsFalse(File.Exists(storage.GetSafePath(stored.RelativePath)));

            await storage.MoveBackAsync(".trash/test_trash-test.pdf", stored.RelativePath, CancellationToken.None);
            Assert.IsTrue(File.Exists(storage.GetSafePath(stored.RelativePath)));
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
        Assert.AreEqual("rechnung_2026_.pdf", StoragePathPolicy.SanitizeFileName("rechnung:2026?.pdf"));
    }

    [TestMethod]
    public void StoragePolicyBoundsPathsWithoutDroppingTheExtension()
    {
        var longName = StoragePathPolicy.SanitizeFileName(new string('a', 260) + ".pdf");
        var longPath = StoragePathPolicy.Combine(new string('b', 480), longName);

        Assert.IsTrue(longPath.Length <= StoragePathPolicy.MaximumRelativePathLength);
        Assert.IsTrue(longPath.EndsWith(".pdf", StringComparison.Ordinal));
        Assert.ThrowsExactly<ArgumentException>(() => StoragePathPolicy.NormalizeRelativePath(new string('x', 501)));
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
    public void AnalyzerUsesExistingCatalogEntriesForSuggestions()
    {
        var result = new DocumentAnalyzer().Analyze(
            "scan",
            "Hausverwaltung Nord GmbH\nMietvertrag für Wohnung",
            ["Hausverwaltung Nord GmbH"],
            ["Mietvertrag"],
            ["wohnung"]);

        Assert.AreEqual("Hausverwaltung Nord GmbH", result.SuggestedCorrespondent);
        Assert.AreEqual("Mietvertrag", result.SuggestedDocumentType);
        CollectionAssert.Contains(result.SuggestedTags.ToList(), "wohnung");
    }

    [TestMethod]
    public void AnalyzerSuggestsAnExistingShelfFromDocumentText()
    {
        var result = new DocumentAnalyzer().Analyze(
            "scan",
            "Stadtwerke Mannheim Stromrechnung Wohnung",
            knownShelfFolders:
            [
                new ShelfFolderDefinition("Finanzen", "Finanzen/Bank"),
                new ShelfFolderDefinition("Strom", "Wohnung/Strom")
            ]);

        Assert.AreEqual("Wohnung/Strom", result.SuggestedShelfPath);
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
    public void CustomFieldValuePolicyAcceptsGermanNumbersAndRejectsWrongTypes()
    {
        Assert.IsTrue(CustomFieldValuePolicy.IsValid(CustomFieldType.Number, "1.234,50"));
        Assert.IsTrue(CustomFieldValuePolicy.IsValid(CustomFieldType.Date, "31.12.2026"));
        Assert.IsTrue(CustomFieldValuePolicy.IsValid(CustomFieldType.Boolean, "true"));
        Assert.IsFalse(CustomFieldValuePolicy.IsValid(CustomFieldType.Date, "not-a-date"));
        Assert.IsFalse(CustomFieldValuePolicy.IsValid((CustomFieldType)99, "value"));
        Assert.IsFalse(CustomFieldValuePolicy.IsValid(CustomFieldType.Text, new string('x', 2001)));
    }

    [TestMethod]
    public void AnalyzerExtractsUserDefinedLabeledCustomFields()
    {
        var result = new DocumentAnalyzer().Analyze(
            "scan",
            "Kostenstelle: 4711\nZahlungsart: Überweisung\nInterne Notiz: nicht als Datum verwenden",
            knownCustomFields:
            [
                new CustomFieldDefinition("Kostenstelle", CustomFieldType.Number),
                new CustomFieldDefinition("Zahlungsart", CustomFieldType.Text),
                new CustomFieldDefinition("Interne Notiz", CustomFieldType.Date)
            ]);

        Assert.AreEqual("4711", result.SuggestedCustomFields["Kostenstelle"]);
        Assert.AreEqual("Überweisung", result.SuggestedCustomFields["Zahlungsart"]);
        Assert.IsFalse(result.SuggestedCustomFields.ContainsKey("Interne Notiz"));
    }

    [TestMethod]
    public void SearchTextCombinesTitleOcrAndTags()
    {
        var document = new Document
        {
            Title = "Strom",
            OriginalFileName = "stromrechnung.pdf",
            FilePath = "Wohnung/Strom/2026-10-05 Strom.pdf",
            OcrText = "Januar",
            Tags = [new DocumentTag { Tag = new Tag { Name = "vertrag" } }],
            CustomFields =
            [
                new DocumentCustomFieldValue
                {
                    CustomField = new CustomField { Name = "Rechnungsnummer" },
                    Value = "RE-42"
                }
            ]
        };

        Assert.AreEqual("Strom stromrechnung.pdf Januar Wohnung/Strom/2026-10-05 Strom.pdf vertrag Rechnungsnummer RE-42", TagStore.BuildSearchText(document));
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
        Assert.IsTrue(shelf.GetIndexes().Any(index => index.IsUnique && index.Properties.Count == 1 && index.Properties[0].Name == nameof(ShelfFolder.RelativePathKey)));
        Assert.AreEqual(DeleteBehavior.Restrict, shelf.FindNavigation(nameof(ShelfFolder.Parent))!.ForeignKey.DeleteBehavior);

        var customValue = db.Model.FindEntityType(typeof(DocumentCustomFieldValue))!;
        CollectionAssert.AreEquivalent(
            new[] { nameof(DocumentCustomFieldValue.DocumentId), nameof(DocumentCustomFieldValue.CustomFieldId) },
            customValue.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.AreEqual(DeleteBehavior.Cascade, customValue.FindNavigation(nameof(DocumentCustomFieldValue.Document))!.ForeignKey.DeleteBehavior);

        var designModel = db.GetService<IDesignTimeModel>().Model;
        var documentConstraints = designModel.FindEntityType(typeof(Document))!.GetCheckConstraints().Select(constraint => constraint.Name).ToArray();
        CollectionAssert.Contains(documentConstraints, "CK_Documents_Status");
        CollectionAssert.Contains(documentConstraints, "CK_Documents_OcrStatus");
        var documentTagIndexes = designModel.FindEntityType(typeof(DocumentTag))!.GetIndexes().Select(index => index.Properties.Select(property => property.Name).ToArray()).ToArray();
        Assert.IsTrue(documentTagIndexes.Any(index => index.SequenceEqual(new[] { nameof(DocumentTag.TagId), nameof(DocumentTag.DocumentId) })));
        var customFieldValueIndexes = designModel.FindEntityType(typeof(DocumentCustomFieldValue))!.GetIndexes().Select(index => index.Properties.Select(property => property.Name).ToArray()).ToArray();
        Assert.IsTrue(customFieldValueIndexes.Any(index => index.SequenceEqual(new[] { nameof(DocumentCustomFieldValue.CustomFieldId), nameof(DocumentCustomFieldValue.DocumentId) })));
        var jobConstraints = designModel.FindEntityType(typeof(ProcessingJob))!.GetCheckConstraints().Select(constraint => constraint.Name).ToArray();
        CollectionAssert.Contains(jobConstraints, "CK_ProcessingJobs_State");
        CollectionAssert.Contains(jobConstraints, "CK_ProcessingJobs_Type");
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
    public void SearchQueryPolicyKeepsIdentifiersSearchableAsLiterals()
    {
        Assert.IsFalse(SearchQueryPolicy.NeedsLiteralFallback("Stadtwerke Mannheim"));
        Assert.IsTrue(SearchQueryPolicy.NeedsLiteralFallback("RE-2026/42"));
        Assert.AreEqual("%RE-2026/42%", SearchQueryPolicy.ToLikePattern("RE-2026/42"));
        Assert.AreEqual("%100\\%\\_fertig%", SearchQueryPolicy.ToLikePattern("100%_fertig"));
        Assert.AreEqual("%C:\\\\Archiv\\%2026%", SearchQueryPolicy.ToLikePattern("C:\\Archiv%2026"));
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
    public void SmbEndpointParsesAndRejectsTraversal()
    {
        var endpoint = SmbEndpoint.Parse("smb://nas/documents/Paper/2026");

        Assert.AreEqual("nas", endpoint.Server);
        Assert.AreEqual("documents", endpoint.Share);
        Assert.AreEqual("Paper/2026", endpoint.BasePath);
        Assert.AreEqual("smb://nas/documents/Paper/2026", endpoint.ToRootPath());
        Assert.ThrowsExactly<InvalidOperationException>(() => SmbEndpoint.Parse("smb://nas/documents/../private"));
        Assert.ThrowsExactly<InvalidOperationException>(() => SmbEndpoint.Create("nas", "documents", "Paper/../../private"));
    }

    [TestMethod]
    public void StorageConfigurationValidatorChecksLocalAndSmbInputs()
    {
        var local = new StorageConfigurationEdit(
            StorageProviderType.Local,
            " /data/documents ",
            null,
            null,
            null,
            null,
            null,
            null,
            WakePolicy.Never,
            null,
            "255.255.255.255");
        Assert.IsNull(StorageConfigurationValidator.Validate(local));

        var smb = local with { ProviderType = StorageProviderType.Smb, SmbServer = "nas", SmbShare = "documents" };
        Assert.AreEqual("Der SMB-Benutzer ist erforderlich.", StorageConfigurationValidator.Validate(smb));
        Assert.IsNull(StorageConfigurationValidator.Validate(smb with { SmbUsername = "paper" }));
    }

    [TestMethod]
    public void StoragePasswordUsesDataProtectionAndNeverAppearsInConfigurationView()
    {
        const string password = "secret-not-for-logs";
        var protector = new EphemeralDataProtectionProvider().CreateProtector("Paper.Storage.SmbPassword.v1");
        var encrypted = protector.Protect(password);
        var view = new StorageConfigurationView(
            StorageProviderType.Smb,
            "/data/documents",
            "nas",
            "documents",
            "Paper",
            "paper",
            "ACME",
            true,
            WakePolicy.Never,
            null,
            "255.255.255.255",
            DateTime.UtcNow);

        Assert.AreNotEqual(password, encrypted);
        Assert.AreEqual(password, protector.Unprotect(encrypted));
        Assert.IsFalse(typeof(StorageConfigurationView).GetProperties().Any(property => property.Name == "SmbPassword"));
        Assert.IsFalse(JsonSerializer.Serialize(view).Contains(password, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LocalConnectionTestReturnsSafeResultAndProviderFactorySelectsLocalProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = new StorageOptions { RootPath = root };
            var provider = new StorageProviderFactory(LoggerFactory.Create(_ => { })).Create(options);
            var result = await provider.TestConnectionAsync(CancellationToken.None);

            Assert.IsTrue(result.Succeeded);
            Assert.IsInstanceOfType<LocalDocumentStorage>(provider);
            Assert.IsFalse(result.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
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
    public void SettingsPageModelsDoNotOwnDirectDatabaseAccess()
    {
        var repoRoot = FindRepositoryRoot();
        var settingsPath = Path.Combine(repoRoot, "src", "Paper.Web", "Pages", "Settings");
        var source = Directory.EnumerateFiles(settingsPath, "*.cs*", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.IsNotEmpty(source);
        Assert.IsFalse(source.Any(content => content.Contains("AppDbContext", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StorageIntegrityCheckReportsMissingFilesWithoutChangingDocuments()
    {
        var documents = new[]
        {
            new StorageIntegrityDocument(1, "Vorhanden", "inbox/one.pdf", 10),
            new StorageIntegrityDocument(2, "Fehlt", "inbox/two.pdf", 20),
            new StorageIntegrityDocument(3, "Auch vorhanden", "shelf/three.pdf", 30)
        };

        var report = StorageIntegrityService.CheckDocuments(
            documents,
            path => path switch
            {
                "inbox/two.pdf" => null,
                "inbox/one.pdf" => new StorageFileMetadata(10),
                _ => new StorageFileMetadata(30)
            },
            new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(3, report.DocumentsChecked);
        Assert.AreEqual(1, report.MissingFiles);
        Assert.AreEqual("inbox/two.pdf", report.Issues.Single().FilePath);
        Assert.IsFalse(report.IsHealthy);
        Assert.IsNull(report.Error);
    }

    [TestMethod]
    public void StorageIntegrityCheckReportsSizeMismatches()
    {
        var documents = new[]
        {
            new StorageIntegrityDocument(1, "Verändert", "shelf/one.pdf", 12)
        };

        var report = StorageIntegrityService.CheckDocuments(
            documents,
            _ => new StorageFileMetadata(9),
            DateTimeOffset.UtcNow);

        Assert.AreEqual(0, report.MissingFiles);
        Assert.AreEqual(1, report.SizeMismatches);
        Assert.AreEqual(StorageIntegrityIssueKind.SizeMismatch, report.Issues.Single().Kind);
        Assert.AreEqual(9, report.Issues.Single().ActualSize);
    }

    [TestMethod]
    public async Task StorageIntegrityCheckReportsHashMismatches()
    {
        var documents = new[]
        {
            new StorageIntegrityDocument(1, "Verändert", "shelf/one.pdf", 12, "expected")
        };

        var report = await StorageIntegrityService.CheckDocumentsAsync(
            documents,
            _ => Task.FromResult<StorageFileVerification?>(new StorageFileVerification(12, "actual")),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.AreEqual(0, report.MissingFiles);
        Assert.AreEqual(0, report.SizeMismatches);
        Assert.AreEqual(1, report.HashMismatches);
        Assert.AreEqual(StorageIntegrityIssueKind.HashMismatch, report.Issues.Single().Kind);
        Assert.IsFalse(report.IsHealthy);
    }

    [TestMethod]
    public void StorageIntegrityCheckStopsSafelyWhenProviderCannotBeQueried()
    {
        var documents = new[]
        {
            new StorageIntegrityDocument(1, "Dokument", "inbox/one.pdf", 1)
        };

        var report = StorageIntegrityService.CheckDocuments(
            documents,
            _ => throw new IOException("NAS nicht erreichbar"),
            DateTimeOffset.UtcNow);

        Assert.AreEqual(1, report.DocumentsChecked);
        Assert.IsNotNull(report.Error);
        StringAssert.Contains(report.Error, "NAS nicht erreichbar");
        Assert.IsFalse(report.IsHealthy);
    }

    [TestMethod]
    public void StorageIntegrityCheckReportsInvalidRegisteredPaths()
    {
        var documents = new[]
        {
            new StorageIntegrityDocument(1, "Ungültig", "../outside.pdf", 1)
        };

        var report = StorageIntegrityService.CheckDocuments(
            documents,
            path =>
            {
                StoragePathPolicy.NormalizeRelativePath(path);
                return new StorageFileMetadata(1);
            },
            DateTimeOffset.UtcNow);

        Assert.IsNotNull(report.Error);
        StringAssert.Contains(report.Error!, "ungültig");
        Assert.AreEqual(1, report.DocumentsChecked);
    }

    [TestMethod]
    public void LoginAttemptLimiterLocksOutAfterRepeatedFailuresAndResetsAfterWindow()
    {
        var limiter = new LoginAttemptLimiter();
        var start = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        for (var attempt = 0; attempt < LoginAttemptLimiter.MaximumFailures; attempt++)
        {
            var failureAt = attempt == LoginAttemptLimiter.MaximumFailures - 1 ? start.AddMinutes(14) : start;
            Assert.IsTrue(limiter.TryBegin("192.0.2.10", failureAt, out _));
            limiter.RecordFailure("192.0.2.10", failureAt);
        }

        Assert.IsFalse(limiter.TryBegin("192.0.2.10", start.AddMinutes(15), out var retryAfter));
        Assert.IsTrue(retryAfter > TimeSpan.Zero);
        Assert.IsTrue(limiter.TryBegin("192.0.2.11", start.AddMinutes(15), out _));
        Assert.IsFalse(limiter.TryBegin("192.0.2.10", start.AddMinutes(16), out _));
        Assert.IsTrue(limiter.TryBegin("192.0.2.10", start.AddMinutes(31), out _));
    }

    [TestMethod]
    public void DirectSmbProviderValidatesItsEndpointBeforeConnecting()
    {
        var validConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "smb",
            ["Storage:SmbRootPath"] = "smb://nas/paper/archive",
            ["Storage:SmbUsername"] = "paper",
            ["Storage:SmbPassword"] = "secret"
        }).Build();

        _ = new SmbStorageProvider(validConfiguration, NullLogger<SmbStorageProvider>.Instance);

        var invalidConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "smb",
            ["Storage:SmbRootPath"] = "not-a-share"
        }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new SmbStorageProvider(invalidConfiguration, NullLogger<SmbStorageProvider>.Instance));
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
    public async Task BackupRestoreStreamsEmptyManifest()
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
            var filing = new DocumentFilingService(db, storage, TimeProvider.System, NullLogger<DocumentFilingService>.Instance);
            var restore = new DocumentRestoreService(db, storage, filing, TimeProvider.System, NullLogger<DocumentRestoreService>.Instance);

            var result = await restore.RestoreAsync(zip, zip.Length, CancellationToken.None);

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
    public async Task BackupRestoreReportsMalformedManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            await using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
            {
                await writer.WriteAsync("{");
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
            var filing = new DocumentFilingService(db, storage, TimeProvider.System, NullLogger<DocumentFilingService>.Instance);
            var restore = new DocumentRestoreService(db, storage, filing, TimeProvider.System, NullLogger<DocumentRestoreService>.Instance);

            var result = await restore.RestoreAsync(zip, zip.Length, CancellationToken.None);

            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains(result.Errors[0], "ungültig");
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
    public async Task PaperlessImporterReportsMalformedZipInsteadOfThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), "paper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
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
            await using var malformed = new MemoryStream("not a zip"u8.ToArray());

            var result = await importer.ImportAsync(malformed, malformed.Length, CancellationToken.None);

            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains(result.Errors[0], "gültiges ZIP");
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

    [TestMethod]
    public void MailConfigurationSupportsMultipleAccountsAndLegacySettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Accounts:0:AccountName"] = "private",
            ["Mail:Accounts:0:Enabled"] = "true",
            ["Mail:Accounts:0:Host"] = "imap.private.test",
            ["Mail:Accounts:0:Username"] = "private-user",
            ["Mail:Accounts:0:Password"] = "private-secret",
            ["Mail:Accounts:1:AccountName"] = "business",
            ["Mail:Accounts:1:Enabled"] = "true",
            ["Mail:Accounts:1:Host"] = "imap.business.test",
            ["Mail:Accounts:1:Username"] = "business-user",
            ["Mail:Accounts:1:Password"] = "business-secret"
        }).Build();

        var accounts = MailConfiguration.Load(configuration);

        Assert.AreEqual(2, accounts.Count);
        CollectionAssert.AreEquivalent(new[] { "private", "business" }, accounts.Select(account => account.AccountName).ToArray());

        var jsonConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:AccountsJson"] = "[{\"Enabled\":true,\"AccountName\":\"json\",\"Host\":\"imap.json.test\",\"Username\":\"json-user\",\"Password\":\"json-secret\"}]"
        }).Build();
        var jsonAccounts = MailConfiguration.Load(jsonConfiguration);
        Assert.AreEqual(1, jsonAccounts.Count);
        Assert.AreEqual("json", jsonAccounts[0].AccountName);

        var legacyConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:Enabled"] = "true",
            ["Mail:AccountName"] = "legacy",
            ["Mail:Host"] = "imap.legacy.test",
            ["Mail:Username"] = "legacy-user",
            ["Mail:Password"] = "legacy-secret"
        }).Build();
        var legacy = MailConfiguration.Load(legacyConfiguration);
        Assert.AreEqual("legacy", legacy.Single().AccountName);
    }

    [TestMethod]
    public void MailConfigurationRejectsMalformedAccountsJsonWithAnActionableError()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mail:AccountsJson"] = "{malformed"
        }).Build();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => MailConfiguration.Load(configuration));

        StringAssert.Contains(exception.Message, "gültiges Konten-JSON");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Paper.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository-Root wurde nicht gefunden.");
    }
}
