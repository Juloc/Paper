using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;
using Paper.Web.Features.CustomFields;
using NpgsqlTypes;

namespace Paper.Web.Data;

public sealed class Document
{
    public long Id { get; set; }

    public string Title { get; set; } = "";

    public DateOnly? DocumentDate { get; set; }

    public string OriginalFileName { get; set; } = "";

    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }

    public string Hash { get; set; } = "";

    public string? OcrText { get; set; }

    public OcrStatus OcrStatus { get; set; }

    public string? OcrError { get; set; }

    public DocumentStatus Status { get; set; }

    public long? CorrespondentId { get; set; }

    public Correspondent? Correspondent { get; set; }

    public long? DocumentTypeId { get; set; }

    public DocumentType? DocumentType { get; set; }

    public long? ShelfFolderId { get; set; }

    public ShelfFolder? ShelfFolder { get; set; }

    public long? SuggestedShelfFolderId { get; set; }

    public ShelfFolder? SuggestedShelfFolder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string SearchText { get; set; } = "";

    public NpgsqlTsVector SearchVector { get; private set; } = null!;

    public List<DocumentTag> Tags { get; set; } = [];

    public List<DocumentCustomFieldValue> CustomFields { get; set; } = [];
}

public sealed class Tag
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string NameKey { get; set; } = "";

    public List<DocumentTag> Documents { get; set; } = [];
}

public sealed class DocumentTag
{
    public long DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public long TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}

public sealed class Correspondent
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string NameKey { get; set; } = "";

    public List<Document> Documents { get; set; } = [];
}

public sealed class DocumentType
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string NameKey { get; set; } = "";

    public List<Document> Documents { get; set; } = [];
}

public sealed class ShelfFolder
{
    public long Id { get; set; }

    public long? ParentId { get; set; }

    public ShelfFolder? Parent { get; set; }

    public string Name { get; set; } = "";

    public string RelativePath { get; set; } = "";

    public string RelativePathKey { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<ShelfFolder> Children { get; set; } = [];

    public List<Document> Documents { get; set; } = [];
}

public sealed class ProcessingJob
{
    public long Id { get; set; }

    public long DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public ProcessingJobType Type { get; set; }

    public ProcessingJobState State { get; set; }

    public int Priority { get; set; }

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? Error { get; set; }
}

public sealed class MailImportState
{
    public long Id { get; set; }

    public string AccountName { get; set; } = "";

    public long? UidValidity { get; set; }

    public long LastUid { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public string? LastError { get; set; }
}

public sealed class MailImportFailure
{
    public long Id { get; set; }

    public string AccountName { get; set; } = "";

    public long Uid { get; set; }

    public string Error { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}

public sealed class ConsumeFailure
{
    public long Id { get; set; }

    public string OriginalFileName { get; set; } = "";

    public string Error { get; set; } = "";

    public DateTime CreatedAt { get; set; }
}

public sealed class AnalysisRule
{
    public long Id { get; set; }

    public string Term { get; set; } = "";

    public long? CorrespondentId { get; set; }

    public long? DocumentTypeId { get; set; }

    public long? ShelfFolderId { get; set; }

    public int UseCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
