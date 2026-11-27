# Applications

A local Blazor app for browsing job-application folders and previewing documents.

## Run on Windows

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then open PowerShell in the folder containing this README and run:

```powershell
dotnet run --project .\ApplicationLog.csproj
```

Open [http://localhost:5080](http://localhost:5080). Press Ctrl+C in PowerShell to stop the app.

The default folder is `C:\Users\bjoer\Documents\Applications`. Set `ApplicationsRoot` in `appsettings.json` or set the environment variable before starting:

```powershell
$env:ApplicationsRoot = 'D:\Job applications'
dotnet run --project .\ApplicationLog.csproj
```

The tree prefixes folder and file names with their last-modified time in local Windows time. Use **Sort by** to switch between name and newest modification time. `log.txt` appears first in each folder. The remaining folders appear before other files. **Refresh folders** reloads the tree and opens the root again. The app reads files locally. TXT edits are saved back to the selected file.

PDFs use the browser viewer. TXT files up to 200,000 characters can be edited in the preview pane. The editor supports UTF-8 and Unicode files with a byte-order mark, and saves after about one second of inactivity or when you switch files. If the file changes on disk, copy your draft before reloading. Word previews show main document paragraphs, inline bold and italic, and basic tables. Excel previews show up to 200 rows and 40 columns per worksheet, with a button to switch sheets. Excel formulas show cached results when available. Older `.xls` files appear in the tree with an unsupported-preview message.

## Run the WPF desktop app on Windows

Install the .NET 10 SDK and the Microsoft Edge WebView2 Runtime (for the embedded PDF viewer). From this folder, run:

```powershell
dotnet run --project .\ApplicationLog.Wpf\ApplicationLog.Wpf.csproj
```

The desktop app uses the same `ApplicationsRoot` setting and environment override as the Blazor app. Both apps read and edit the same files when pointed at the same folder. The text editor checks for changes on disk before saving and keeps your draft available if it detects a conflict.

The folder tree and document preview have separate zoom controls. Use the minus and plus buttons to zoom between 50% and 200%, or select the percentage to reset to 100%. In the folder tree and native previews, focus the pane and use Ctrl+Plus, Ctrl+Minus, or Ctrl+0. You can also hold Ctrl while scrolling over these panes.

## Run tests

Run `dotnet test Applications.sln` with the .NET 10 SDK. The xUnit project lives in `ApplicationLog.Tests` and uses temporary files. It can run without access to your Applications folder or a local Office installation.