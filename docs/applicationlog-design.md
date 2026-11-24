# ApplicationLog

**Design and implementation**

November 24, 2026  •  applications-browser  •  f63e7fc

## Purpose

ApplicationLog is a local Windows app for browsing job application folders and reading documents in a browser. It uses the existing folder structure as its source of truth and reads files in place.

## Interface

The left pane shows an expandable folder tree. Folders appear before files at each level, with names sorted alphabetically within each group. Each name is prefixed with its last-modified timestamp in the computer’s local time, using the format yyyy-MM-dd HH:mm.

The folder count includes nested subfolders and excludes the configured root and linked folders. Refresh folders rescans the tree and recalculates the count. The root opens again after a refresh.

The right pane displays the selected document. A workbook has a button for each worksheet. If a file type is unsupported or a preview cannot be read, the pane displays a status message.

## Run locally

Install the .NET 10 SDK. Open Applications.sln in Visual Studio, or run the following from the repository root in PowerShell.

```powershell
dotnet run --project .\ApplicationLog.csproj
```

Open http://localhost:5080. The default folder is C:\Users\bjoer\Documents\Applications. Set ApplicationsRoot in appsettings.json or as an environment variable to browse a different folder.

## Implementation

ASP.NET Core hosts the app in Kestrel on loopback port 5080. Blazor interactive server components handle folder expansion, selection, and refresh. The browser renders the UI while the server reads the Windows filesystem.

| Source file | Responsibility |
| --- | --- |
| `Program.cs` | Configures the web host, registers the filesystem service, and serves PDFs with byte-range support. |
| `Components/Home.razor` | Tracks the selected file, active worksheet, folder count, and preview status. |
| `Components/TreeEntry.razor` | Recursively renders tree rows and loads folder contents when expanded. |
| `Library.cs` | Resolves paths under the configured root, enumerates entries, counts folders, and opens files for reading. |
| `Preview.cs` | Reads text, extracts Word content, and builds Excel worksheet previews. |
| `wwwroot/app.css` | Styles the folder tree, preview panes, and responsive layout. |

### File access

The filesystem service takes paths relative to the configured root. It rejects paths that resolve outside that root and skips filesystem links and junctions. Files open with read-only access. PDF requests use the same filesystem service as the other previews.

### Preview behavior

| Format | Rendering and limits |
| --- | --- |
| PDF | A local endpoint streams the document to the browser’s PDF viewer. |
| TXT | Plain text with UTF-8 or Unicode byte-order mark detection. The preview indicates truncation after 200,000 characters. |
| DOCX | Reads the main document XML in the Word archive. Displays paragraphs, inline bold and italic, and basic tables. |
| XLSX | ClosedXML 0.105.0 reads workbooks. The preview displays up to 200 rows and 40 columns per worksheet and uses cached formula values when available. |

Word and Excel previews work without Office installed. Complex Word layouts and Excel charts can be omitted. Legacy .xls files display an unsupported-preview message.

[Source repository](https://github.com/bjoernstorkholm/Superpowers/tree/applications-browser)
