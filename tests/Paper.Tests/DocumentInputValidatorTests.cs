using Paper.Web.Features.Storage;
using Paper.Web.Features.Documents;

namespace Paper.Tests;

[TestClass]
public sealed class DocumentInputValidatorTests
{
    [TestMethod]
    public void AcceptsPdfWhenExtensionMimeAndSignatureAgree()
    {
        var valid = DocumentInputValidator.TryValidate(
            "invoice.pdf",
            "application/pdf",
            5,
            "%PDF-"u8,
            out var extension,
            out _);

        Assert.IsTrue(valid);
        Assert.AreEqual(".pdf", extension);
    }

    [TestMethod]
    public void RejectsContentWithAFalseExtension()
    {
        var valid = DocumentInputValidator.TryValidate(
            "invoice.pdf",
            "application/pdf",
            4,
            new byte[] { 0xff, 0xd8, 0xff, 0x00 },
            out _,
            out var error);

        Assert.IsFalse(valid);
        StringAssert.Contains(error, "Dateiinhalt");
    }

    [TestMethod]
    public void RejectsUnsupportedTypeAndOversizedFiles()
    {
        Assert.IsFalse(DocumentInputValidator.TryValidate("notes.txt", "text/plain", 1, new byte[] { 1 }, out _, out _));
        Assert.IsFalse(DocumentInputValidator.TryValidate("notes.pdf", "application/pdf", DocumentInputValidator.MaximumFileSize + 1, "%PDF-"u8, out _, out _));
    }

    [TestMethod]
    public void BatchUploadPolicyBoundsCountAndTotalSize()
    {
        Assert.IsNull(BatchUploadPolicy.Validate(1, 1));
        StringAssert.Contains(BatchUploadPolicy.Validate(BatchUploadPolicy.MaximumFileCount + 1, 1)!, "höchstens");
        StringAssert.Contains(BatchUploadPolicy.Validate(1, BatchUploadPolicy.MaximumTotalSize + 1)!, "500 MB");
    }

    [TestMethod]
    public void BatchUploadPolicyKeepsTempDataErrorSummaryShort()
    {
        var errors = Enumerable.Range(1, 10).Select(index => $"file-{index}.pdf: Fehler").ToArray();

        var summary = BatchUploadPolicy.SummarizeErrors(errors);

        StringAssert.Contains(summary, "file-1.pdf");
        StringAssert.Contains(summary, "file-3.pdf");
        StringAssert.Contains(summary, "7 weitere");
        Assert.IsFalse(summary.Contains("file-10.pdf", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadPrefixHandlesStreamsThatReturnShortReads()
    {
        await using var source = new ShortReadStream("%PDF-123"u8.ToArray());
        var buffer = new byte[8];

        var length = await DocumentInputValidator.ReadPrefixAsync(source, buffer, CancellationToken.None);

        Assert.AreEqual(8, length);
        CollectionAssert.AreEqual("%PDF-123"u8.ToArray(), buffer);
    }

    private sealed class ShortReadStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
