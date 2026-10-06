using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Paper.Web.Data;
using Paper.Web.Features.Auth;
using Paper.Web.Features.Correspondents;
using Paper.Web.Features.CustomFields;
using Paper.Web.Features.Documents;
using Paper.Web.Features.DocumentTypes;
using Paper.Web.Features.Export;
using Paper.Web.Features.Import;
using Paper.Web.Features.Processing;
using Paper.Web.Features.Search;
using Paper.Web.Features.Shelf;
using Paper.Web.Features.Storage;
using Paper.Web.Features.Tags;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
var dataProtectionDirectory = new DirectoryInfo(builder.Configuration["DataProtection:KeysDirectory"] ?? "/data/keys");
Directory.CreateDirectory(dataProtectionDirectory.FullName);
builder.Services.AddDataProtection()
    .SetApplicationName("Paper")
    .PersistKeysToFileSystem(dataProtectionDirectory);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is required.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LocalDocumentStorage>();
builder.Services.AddScoped<DocumentImportService>();
builder.Services.AddScoped<DocumentStore>();
builder.Services.AddScoped<DocumentFilingService>();
builder.Services.AddScoped<DocumentFileService>();
builder.Services.AddScoped<DocumentSearchService>();
builder.Services.AddScoped<TagStore>();
builder.Services.AddScoped<CorrespondentStore>();
builder.Services.AddScoped<DocumentTypeStore>();
builder.Services.AddScoped<ShelfFolderStore>();
builder.Services.AddScoped<CustomFieldStore>();
builder.Services.AddScoped<DocumentExportService>();
builder.Services.AddScoped<DocumentBackupService>();
builder.Services.AddScoped<DocumentRestoreService>();
builder.Services.AddScoped<ProcessingJobStore>();
builder.Services.AddSingleton<DocumentAnalyzer>();
builder.Services.AddScoped<TesseractOcrService>();
builder.Services.AddScoped<OwnerAuthService>();
builder.Services.AddHostedService<DocumentProcessingWorker>();
builder.Services.AddHostedService<ConsumeDirectoryWorker>();
builder.Services.AddHostedService<ImapMailImportWorker>();
builder.Services.AddSingleton<EmailAttachmentExtractor>();
builder.Services.AddScoped<PaperlessImportService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Paper.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.Configure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();
app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/documents/{id:long}/file", async (long id, DocumentFileService files, CancellationToken cancellationToken) =>
{
    var file = await files.OpenAsync(id, cancellationToken);
    return file is null ? Results.NotFound() : Results.File(file.Stream, file.ContentType, file.DownloadName, enableRangeProcessing: true);
}).RequireAuthorization();
app.MapGet("/export/documents.json", async (HttpResponse response, DocumentExportService exporter, CancellationToken cancellationToken) =>
{
    response.ContentType = "application/json; charset=utf-8";
    response.Headers.ContentDisposition = "attachment; filename=\"paper-documents.json\"";
    await exporter.WriteJsonAsync(response.Body, cancellationToken);
}).RequireAuthorization();
app.MapGet("/export/documents.csv", async (HttpResponse response, DocumentExportService exporter, CancellationToken cancellationToken) =>
{
    response.ContentType = "text/csv; charset=utf-8";
    response.Headers.ContentDisposition = "attachment; filename=\"paper-documents.csv\"";
    await exporter.WriteCsvAsync(response.Body, cancellationToken);
}).RequireAuthorization();
app.MapGet("/export/backup.zip", async (HttpResponse response, DocumentBackupService backup, CancellationToken cancellationToken) =>
{
    response.ContentType = "application/zip";
    response.Headers.ContentDisposition = "attachment; filename=\"paper-backup.zip\"";
    await backup.WriteZipAsync(response.Body, cancellationToken);
}).RequireAuthorization();
app.MapRazorPages();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();
