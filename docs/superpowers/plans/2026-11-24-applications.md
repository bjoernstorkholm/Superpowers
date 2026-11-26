# Applications browser implementation plan

Build a local .NET 10 Blazor Server app. Read the configured folder on demand, display local modification timestamps, and keep selection and expansion in the browser session.

- List directories and files without following links. Verify nested entries, timestamps, refresh, and missing folders.
- Render text, PDF, Word paragraphs and tables, and Excel worksheets through read-only handlers. Verify representative files and unsupported or unreadable files.
- Document Windows startup and check the app against a synthetic folder, since the Windows Documents directory is outside the build environment.