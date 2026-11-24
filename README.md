# Applications

A local Blazor app for browsing job-application folders and previewing documents.

## Run on Windows

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then open PowerShell in the folder containing this README and run:

```powershell
dotnet run --project .\Applications
```

Open [http://localhost:5080](http://localhost:5080). Press Ctrl+C in PowerShell to stop the app.

The default folder is `C:\Users\bjoer\Documents\Applications`. Set `ApplicationsRoot` in `Applications/appsettings.json` or set the environment variable before starting:

```powershell
$env:ApplicationsRoot = 'D:\Job applications'
dotnet run --project .\Applications
```

The tree prefixes folder and file names with their last-modified time in local Windows time. **Refresh folders** reloads the tree and opens the root again. The app reads files locally and leaves their names and contents intact.

PDFs use the browser viewer. Text previews support UTF-8 and Unicode files with a byte-order mark. Word previews show main document paragraphs, inline bold and italic, and basic tables. Excel previews show up to 200 rows and 40 columns per worksheet, with a button to switch sheets. Excel formulas show cached results when available. Older `.xls` files appear in the tree with an unsupported-preview message.
