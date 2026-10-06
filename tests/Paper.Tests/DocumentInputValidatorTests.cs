using Paper.Web.Features.Storage;

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
}
