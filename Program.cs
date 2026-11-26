using Applications;
using Applications.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5080));
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 2 * 1024 * 1024);
builder.Services.AddSingleton(new Library(builder.Configuration["ApplicationsRoot"]!));

WebApplication app = builder.Build();
app.MapStaticAssets();
app.UseAntiforgery();

app.MapGet("/preview/pdf", (string path, Library library) =>
{
    if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();

    try
    {
        FileStream stream = library.Open(path);

        return Results.File(stream, "application/pdf", enableRangeProcessing: true);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
    {
        return Results.NotFound();
    }
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();