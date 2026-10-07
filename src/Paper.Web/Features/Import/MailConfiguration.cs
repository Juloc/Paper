using System.Text.Json;

namespace Paper.Web.Features.Import;

public static class MailConfiguration
{
    public static IReadOnlyList<MailAccountOptions> Load(IConfiguration configuration)
    {
        var accounts = configuration.GetSection("Mail:Accounts").Get<List<MailAccountOptions>>();
        if (accounts is { Count: > 0 })
        {
            return accounts;
        }

        var accountsJson = configuration["Mail:AccountsJson"];
        if (!string.IsNullOrWhiteSpace(accountsJson))
        {
            try
            {
                return JsonSerializer.Deserialize<List<MailAccountOptions>>(
                           accountsJson,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? [];
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Mail:AccountsJson enthält kein gültiges Konten-JSON.", exception);
            }
        }

        var legacy = configuration.GetSection("Mail").Get<MailAccountOptions>();
        return legacy is null ? [] : [legacy];
    }
}
