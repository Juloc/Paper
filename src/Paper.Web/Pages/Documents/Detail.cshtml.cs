using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Documents;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Shelf;

namespace Paper.Web.Pages.Documents;

public sealed class DetailModel(
    DocumentStore documents,
    DocumentFilingService filing,
    CorrespondentStore correspondents,
    DocumentTypeStore documentTypes,
    ShelfFolderStore shelfFolders,
    CustomFieldStore customFields,
    DocumentLearningStore learning,
    ILogger<DetailModel> logger) : PageModel
{
    [BindProperty]
    public EditDocumentInput Input { get; set; } = new();

    public DocumentDetails? Document { get; private set; }
    public IReadOnlyList<CorrespondentOption> Correspondents { get; private set; } = [];
    public IReadOnlyList<DocumentTypeOption> DocumentTypes { get; private set; } = [];
    public IReadOnlyList<ShelfFolderOption> ShelfFolders { get; private set; } = [];
    public IReadOnlyList<CustomFieldOption> CustomFields { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        Document = await documents.GetAsync(id, cancellationToken);
        if (Document is null)
        {
            return NotFound();
        }

        Input.Load(Document);
        await LoadOptionsAsync(cancellationToken);
        return Page();
    }

    public Task<IActionResult> OnPostFileAsync(long id, CancellationToken cancellationToken) =>
        SaveAsync(id, fileFromInbox: true, "Dokument abgelegt.", cancellationToken);

    public Task<IActionResult> OnPostSaveAsync(long id, CancellationToken cancellationToken) =>
        SaveAsync(id, fileFromInbox: false, "Änderungen gespeichert.", cancellationToken);

    public async Task<IActionResult> OnPostReanalyzeAsync(long id, CancellationToken cancellationToken)
    {
        if (!await documents.QueueReanalysisAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = "Dokument wird erneut analysiert.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRestoreToInboxAsync(long id, CancellationToken cancellationToken)
    {
        if (!await documents.SetInboxStatusAsync(id, DocumentStatus.Inbox, cancellationToken))
        {
            return NotFound();
        }

        TempData["Status"] = "Dokument wieder in die Inbox gelegt.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(long id, CancellationToken cancellationToken)
    {
        var result = await documents.DeleteAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Das Dokument konnte nicht gelöscht werden.");
            await ReloadAsync(id, cancellationToken);
            return Document is null ? NotFound() : Page();
        }

        TempData["Status"] = "Dokument gelöscht.";
        return RedirectToPage("/Inbox/Index");
    }

    private async Task<IActionResult> SaveAsync(long id, bool fileFromInbox, string successMessage, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await ReloadAsync(id, cancellationToken);
            return Document is null ? NotFound() : Page();
        }

        var result = await filing.SaveAsync(id, Input.ToEdit(), fileFromInbox, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Die Änderungen konnten nicht gespeichert werden.");
            await ReloadAsync(id, cancellationToken);
            return Document is null ? NotFound() : Page();
        }

        try
        {
            await learning.RecordCorrectionAsync(id, Input.ToEdit(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not record the learning correction for document {DocumentId}.", id);
        }

        TempData["Status"] = successMessage;
        return RedirectToPage(new { id });
    }

    private async Task ReloadAsync(long id, CancellationToken cancellationToken)
    {
        Document = await documents.GetAsync(id, cancellationToken);
        await LoadOptionsAsync(cancellationToken);
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        Correspondents = await correspondents.ListAsync(cancellationToken);
        DocumentTypes = await documentTypes.ListAsync(cancellationToken);
        ShelfFolders = await shelfFolders.ListOptionsAsync(cancellationToken);
        CustomFields = await customFields.ListAsync(cancellationToken);
    }
}

public sealed class EditDocumentInput
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(300)]
    public string Title { get; set; } = "";

    public DateOnly? DocumentDate { get; set; }
    public long? CorrespondentId { get; set; }
    public long? DocumentTypeId { get; set; }
    public long? ShelfFolderId { get; set; }
    public string Tags { get; set; } = "";
    public Dictionary<long, string> CustomFields { get; set; } = [];

    public void Load(DocumentDetails document)
    {
        Title = document.Title;
        DocumentDate = document.DocumentDate;
        CorrespondentId = document.CorrespondentId;
        DocumentTypeId = document.DocumentTypeId;
        ShelfFolderId = document.ShelfFolderId;
        Tags = string.Join(", ", document.Tags);
        CustomFields = document.CustomFields.ToDictionary(field => field.CustomFieldId, field => field.Value);
    }

    public DocumentEdit ToEdit() => new(Title, DocumentDate, CorrespondentId, DocumentTypeId, ShelfFolderId, Tags, CustomFields);
}
