namespace Paper.Web.Features.Import;

internal sealed class ImportSizeBudget(long maximumBytes)
{
    private long consumedBytes;

    public bool TryConsume(long bytes)
    {
        if (bytes < 0 || bytes > maximumBytes - consumedBytes)
        {
            return false;
        }

        consumedBytes += bytes;
        return true;
    }
}
