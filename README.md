# ApplicationLog

ApplicationLog is a local Blazor app for browsing job application folders. The left pane lists folders and files. Select a file to see its preview on the right.

## Run on Windows

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Extract the project files into `C:\projects\ApplicationLog`.
3. Open PowerShell and run:

   ```powershell
   Set-Location C:\projects\ApplicationLog
   dotnet run
   ```

4. Open [http://localhost:5176](http://localhost:5176).

The app reads `C:\Users\bjoer\Documents\Applications` by default. To change the location, edit `ApplicationsFolder` in `appsettings.json`. The web server listens on loopback. The app browses source files directly without copying or editing them.

## Previews

- PDF, PNG, JPEG, GIF, WebP, and BMP preview in the browser.
- TXT, Markdown, CSV, and log files preview as plain text.
- **Open in desktop app** uses your Windows file association for these formats and common Word, Excel, PowerPoint, OpenDocument, email, and image formats.
- Other file types appear in the tree. Use Windows File Explorer to open them.
- **Refresh folders** rescans the root and resets the current selection.

## WPF desktop app

On Windows with the .NET 10 SDK, run this command from the repository root:

```powershell
dotnet run --project .\ApplicationLog.Wpf\ApplicationLog.Wpf.csproj
```

The desktop app supports TXT editing and autosave, PDF, Word and Excel previews, and separate zoom controls for the folder tree and preview. Install the Microsoft Edge WebView2 Runtime for PDF previews. Set `ApplicationsRoot` in `ApplicationLog.Wpf/appsettings.json` or as an environment variable to change the desktop app's folder. Blazor continues to use `ApplicationsFolder` in the root settings file.

Build both apps with `dotnet build Applications.slnx`.