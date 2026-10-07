using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using EntityTagHeaderValue = Microsoft.Net.Http.Headers.EntityTagHeaderValue;
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
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = DocumentRestoreService.MaximumBackupSize + 1024 * 1024);
var dataProtectionDirectory = new DirectoryInfo(builder.Configuration["DataProtection:KeysDirectory"] ?? "/data/keys");
Directory.CreateDirectory(dataProtectionDirectory.FullName);
builder.Services.AddDataProtection()
    .SetApplicationName("Paper")
    .PersistKeysToFileSystem(dataProtectionDirectory);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is required.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StorageProviderFactory>();
builder.Services.AddScoped<StorageConfigurationStore>();
builder.Services.AddScoped<StorageConnectionTestService>();
builder.Services.AddScoped<IStorageProvider>(services =>
{
    var configurations = services.GetRequiredService<StorageConfigurationStore>();
    var providers = services.GetRequiredService<StorageProviderFactory>();
    return providers.Create(configurations.LoadProviderOptions());
});
builder.Services.AddScoped<DocumentImportService>();
builder.Services.AddScoped<DocumentStore>();
builder.Services.AddScoped<DocumentFilingService>();
builder.Services.AddScoped<DocumentFileService>();
builder.Services.AddScoped<ThumbnailService>();
builder.Services.AddScoped<DocumentSearchService>();
builder.Services.AddScoped<TagStore>();
builder.Services.AddScoped<CorrespondentStore>();
builder.Services.AddScoped<DocumentTypeStore>();
builder.Services.AddScoped<ShelfFolderStore>();
builder.Services.AddScoped<CustomFieldStore>();
builder.Services.AddScoped<StorageIntegrityService>();
builder.Services.AddScoped<DocumentExportService>();
builder.Services.AddScoped<DocumentBackupService>();
builder.Services.AddScoped<DocumentRestoreService>();
builder.Services.AddScoped<ProcessingJobStore>();
builder.Services.AddScoped<ProcessingStatusStore>();
builder.Services.AddScoped<AnalysisRuleStore>();
builder.Services.AddSingleton<DocumentAnalyzer>();
builder.Services.AddScoped<DocumentLearningStore>();
builder.Services.AddScoped<TesseractOcrService>();
builder.Services.AddScoped<OwnerAuthService>();
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddHostedService<DocumentProcessingWorker>();
builder.Services.AddHostedService<ConsumeDirectoryWorker>();
builder.Services.AddSingleton<ImapMailImportWorker>();
builder.Services.AddSingleton<IHostedService>(services => services.GetRequiredService<ImapMailImportWorker>());
builder.Services.AddSingleton<EmailAttachmentExtractor>();
builder.Services.AddScoped<PaperlessImportService>();
builder.Services.AddScoped<ConsumeFailureStore>();
builder.Services.AddScoped<MailImportStatusStore>();

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
if (builder.Environment.IsProduction() &&
    (string.IsNullOrWhiteSpace(builder.Configuration["Auth:Password"]) || builder.Configuration["Auth:Password"] == "change-me"))
{
    throw new InvalidOperationException("Auth:Password muss in Produktionsumgebungen gesetzt werden.");
}

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'; frame-src 'self'; img-src 'self' data:; object-src 'self'; style-src 'self' 'unsafe-inline'";
    await next();
});
app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async Task<IResult> (AppDbContext db, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    try
    {
        return await db.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ok" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        logger.LogWarning(exception, "Health check could not connect to the database.");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
app.MapGet("/documents/{id:long}/file", async (HttpResponse response, long id, bool? download, DocumentFileService files, CancellationToken cancellationToken) =>
{
    var file = await files.OpenAsync(id, cancellationToken);
    return file is null
        ? Results.NotFound()
        : CreatePrivateFileResponse(response, file.Stream, file.ContentType, download == true ? file.DownloadName : null, file.LastModified, file.EntityTag);
}).RequireAuthorization();
app.MapGet("/documents/{id:long}/thumbnail", async (HttpResponse response, long id, ThumbnailService thumbnails, CancellationToken cancellationToken) =>
{
    var file = await thumbnails.OpenAsync(id, cancellationToken);
    return file is null
        ? Results.NotFound()
        : CreatePrivateFileResponse(response, file.Stream, file.ContentType, null, null, file.EntityTag, cacheForBrowser: true);
}).RequireAuthorization();
app.MapGet("/export/documents.json", async (HttpResponse response, DocumentExportService exporter, CancellationToken cancellationToken) =>
{
    SetNoStore(response);
    response.ContentType = "application/json; charset=utf-8";
    response.Headers.ContentDisposition = "attachment; filename=\"paper-documents.json\"";
    await exporter.WriteJsonAsync(response.Body, cancellationToken);
}).RequireAuthorization();
app.MapGet("/export/documents.csv", async (HttpResponse response, DocumentExportService exporter, CancellationToken cancellationToken) =>
{
    SetNoStore(response);
    response.ContentType = "text/csv; charset=utf-8";
    response.Headers.ContentDisposition = "attachment; filename=\"paper-documents.csv\"";
    await exporter.WriteCsvAsync(response.Body, cancellationToken);
}).RequireAuthorization();
app.MapGet("/export/backup.zip", async (HttpResponse response, DocumentBackupService backup, CancellationToken cancellationToken) =>
{
    SetNoStore(response);
    response.ContentType = "application/zip";
    response.Headers.ContentDisposition = "attachment; filename=\"paper-backup.zip\"";
    await backup.WriteZipAsync(response.Body, cancellationToken);
}).RequireAuthorization();
app.MapRazorPages();

static IResult CreatePrivateFileResponse(
    HttpResponse response,
    Stream stream,
    string contentType,
    string? downloadName,
    DateTimeOffset? lastModified,
    string entityTag,
    bool cacheForBrowser = false)
{
    if (cacheForBrowser)
    {
        response.Headers.CacheControl = "private, max-age=3600, must-revalidate";
    }
    else
    {
        SetNoStore(response);
    }
    return Results.File(
        stream,
        contentType,
        downloadName,
        lastModified,
        new EntityTagHeaderValue($"\"{entityTag}\""),
        enableRangeProcessing: true);
}

static void SetNoStore(HttpResponse response)
{
    response.Headers.CacheControl = "private, no-store";
    response.Headers.Pragma = "no-cache";
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<StorageConfigurationStore>().EnsureInitializedAsync(CancellationToken.None);
    await scope.ServiceProvider.GetRequiredService<ThumbnailService>().QueueMissingAsync(CancellationToken.None);
}

await app.RunAsync();
