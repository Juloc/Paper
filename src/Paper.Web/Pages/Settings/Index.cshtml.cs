using Microsoft.AspNetCore.Mvc.RazorPages;
using Paper.Web.Features.Storage;

namespace Paper.Web.Pages.Settings;

public sealed class IndexModel(StorageConfigurationStore storage, IConfiguration configuration) : PageModel
{
    public StorageConfigurationView Storage { get; private set; } = null!;

    public string OcrLanguage => configuration["Ocr:Language"] ?? "deu+eng";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Storage = await storage.GetAsync(cancellationToken);
    }
}
