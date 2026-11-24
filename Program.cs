using Applications;
using Applications.Components;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(5080));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(new Library(builder.Configuration["ApplicationsRoot"]!));

var app = builder.Build();
app.MapStaticAssets();
app.UseAntiforgery();

app.MapGet("/preview/pdf", (string path, Library library) =>
{
    if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();

    try
    {
        var stream = library.Open(path);
        return Results.File(stream, "application/pdf", enableRangeProcessing: true);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
    {
        return Results.NotFound();
    }
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
