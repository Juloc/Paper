using Paper.Web.Data;

namespace Paper.Web.Features.CustomFields;

public enum CustomFieldType
{
    Text,
    Number,
    Date,
    Boolean
}

public sealed record CustomFieldDefinition(string Name, CustomFieldType Type);

public sealed class CustomField
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public CustomFieldType Type { get; set; }

    public List<DocumentCustomFieldValue> Values { get; set; } = [];
}

public sealed class DocumentCustomFieldValue
{
    public long DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public long CustomFieldId { get; set; }

    public CustomField CustomField { get; set; } = null!;

    public string Value { get; set; } = "";
}
