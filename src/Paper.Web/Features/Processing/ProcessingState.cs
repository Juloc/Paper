namespace Paper.Web.Features.Processing;

public enum OcrStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public enum ProcessingJobType
{
    OcrAndAnalyze
}

public enum ProcessingJobState
{
    Pending,
    Running,
    Succeeded,
    Failed
}
