using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Search;

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
        Assert.IsFalse(new SearchCriteria("", null, null, null, null, null, null, null, null).HasFilters);
    }
}
