using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Paper.Web.Pages.Settings.Processing;

public sealed class IndexModel(IConfiguration configuration) : PageModel
{
    public string OcrLanguage => configuration["Ocr:Language"] ?? "deu+eng";
}
