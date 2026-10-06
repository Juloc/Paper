using Paper.Web.Features.Documents;
using Paper.Web.Features.Processing;
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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string SearchText { get; set; } = "";

    public NpgsqlTsVector SearchVector { get; private set; } = null!;

    public List<DocumentTag> Tags { get; set; } = [];
}

public sealed class Tag
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public List<DocumentTag> Documents { get; set; } = [];
}

public sealed class DocumentTag
{
    public long DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public long TagId { get; set; }

    public Tag Tag { get; set; } = null!;
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
