using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Paper.Web.Data;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;

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

            Assert.AreEqual("3c87d37f1dbea6909f917ce437c390fb8e655a774387d9e69301c0b2283d5b63", result.Stored.Hash);
            Assert.ThrowsExactly<InvalidOperationException>(() => storage.GetSafePath("../secrets.pdf"));
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
    public void AnalyzerFindsDateTitleAndOneCanonicalTag()
    {
        var result = new DocumentAnalyzer().Analyze("scan", "Rechnung März\nRechnungsnummer 4\n12.03.2026\nBetrag 42 EUR");

        Assert.AreEqual("Rechnung März", result.Title);
        Assert.AreEqual(new DateOnly(2026, 3, 12), result.DocumentDate);
        CollectionAssert.Contains(result.SuggestedTags.ToList(), "rechnung");
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
}
