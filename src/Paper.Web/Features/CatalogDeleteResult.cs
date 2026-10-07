namespace Paper.Web.Features;

public sealed record CatalogDeleteResult(bool Deleted, bool NotFound, bool InUse)
{
    public static CatalogDeleteResult Success { get; } = new(true, false, false);
    public static CatalogDeleteResult Missing { get; } = new(false, true, false);
    public static CatalogDeleteResult Used { get; } = new(false, false, true);
}
