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

        var legacy = configuration.GetSection("Mail").Get<MailAccountOptions>();
        return legacy is null ? [] : [legacy];
    }
}
