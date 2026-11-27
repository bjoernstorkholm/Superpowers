using ApplicationLog;
using ApplicationLog.Components;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5176));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new ApplicationFiles(builder.Configuration["ApplicationsFolder"]!));

var app = builder.Build();
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && origin != "http://localhost:5176")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAntiforgery();
PhysicalFileProvider pdfAssets = new(Path.Combine(AppContext.BaseDirectory, "PdfViewer"));
app.Lifetime.ApplicationStopped.Register(pdfAssets.Dispose);
FileExtensionContentTypeProvider pdfContentTypes = new();
pdfContentTypes.Mappings[".mjs"] = "text/javascript";
pdfContentTypes.Mappings[".ftl"] = "text/plain; charset=utf-8";
pdfContentTypes.Mappings[".bcmap"] = "application/octet-stream";
pdfContentTypes.Mappings[".icc"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = pdfAssets,
    RequestPath = "/pdf-viewer",
    ContentTypeProvider = pdfContentTypes,
    OnPrepareResponse = response => response.Context.Response.Headers.XFrameOptions = "SAMEORIGIN"
});
app.MapStaticAssets();
app.MapGet("/preview", (string path, ApplicationFiles files) =>
{
    var contentType = ApplicationFiles.PreviewType(path);
    if (contentType is null)
        return Results.NotFound();

    try
    {
        return Results.File(files.ResolveFile(path), contentType, enableRangeProcessing: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        return Results.NotFound();
    }
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
