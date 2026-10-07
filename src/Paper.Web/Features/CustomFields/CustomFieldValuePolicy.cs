using System.Globalization;

namespace Paper.Web.Features.CustomFields;

public static class CustomFieldValuePolicy
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    public static bool IsValid(CustomFieldType type, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return type switch
        {
            CustomFieldType.Text => value.Length <= 2000,
            CustomFieldType.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ||
                                       decimal.TryParse(value, NumberStyles.Number, GermanCulture, out _),
            CustomFieldType.Date => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _) ||
                                    DateOnly.TryParse(value, GermanCulture, DateTimeStyles.AllowWhiteSpaces, out _),
            CustomFieldType.Boolean => bool.TryParse(value, out _),
            _ => false
        };
    }
}
