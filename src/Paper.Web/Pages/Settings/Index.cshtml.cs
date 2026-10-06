using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Paper.Web.Pages.Settings;

public sealed class IndexModel(IConfiguration configuration) : PageModel
{
    public string StoragePath => configuration["Storage:RootPath"] ?? "/data/documents";

    public string OcrLanguage => configuration["Ocr:Language"] ?? "eng";
}
