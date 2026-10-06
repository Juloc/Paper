namespace Paper.Web.Features.Import;

public sealed class MailAccountOptions
{
    public bool Enabled { get; set; }
    public string AccountName { get; set; } = "default";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Folder { get; set; } = "INBOX";
    public int PollSeconds { get; set; } = 300;
    public int MaxMessagesPerRun { get; set; } = 10;
    public bool OnlyUnread { get; set; } = true;
    public bool MarkSeen { get; set; }

    public bool IsConfigured(out string error)
    {
        if (string.IsNullOrWhiteSpace(AccountName) || AccountName.Length > 120)
        {
            error = "Mail account name is missing or too long.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Host) || Host.Length > 255)
        {
            error = "Mail host is missing or too long.";
            return false;
        }

        if (Port is < 1 or > 65535)
        {
            error = "Mail port is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            error = "Mail username or password is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Folder) || Folder.Length > 255)
        {
            error = "Mail folder is missing or too long.";
            return false;
        }

        if (PollSeconds is < 30 or > 86400 || MaxMessagesPerRun is < 1 or > 1000)
        {
            error = "Mail polling limits are invalid.";
            return false;
        }

        error = "";
        return true;
    }
}
