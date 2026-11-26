# Instructions for coding agents

## Scope and workflow
- These instructions apply to the entire repository. Also read the applicable nested `AGENTS.md` files before changing an area.
- Inspect the current branch, working tree and relevant code before making changes.
- Preserve unrelated user changes.
- Follow the user's choice of branch. Otherwise, create a new feature branch from the intended base branch.
- Explain meaningful changes and how they will be verified.
- Commit, push or merge only when the user has requested it.

## Parallel agents and worktrees
- Before starting parallel code changes, remind the user that separate Git worktrees are recommended.
- When parallel agent work is authorized, use one worktree and one branch per agent by default.
- Agree on task boundaries, file ownership and shared interfaces before agents start editing.
- Keep each agent's changes focused and reviewable. Each agent should run relevant checks in its own worktree.
- Integrate the agents' changes in a dedicated integration branch/worktree and resolve conflicts there.
- Run the combined build and relevant tests after integration; passing checks in individual worktrees are not sufficient.
- Use a shared workspace only for small, clearly separated changes or when the user explicitly prefers it.
- Do not delete worktrees or branches containing uncommitted or unmerged work.

## Shared conventions and security
- Use C# and .NET 10 (`net10.0`), as specified by the project files.
- Use explicit types for all C# variable declarations; never use `var`.
- Prefer classes over records. Avoid nested classes and records; declare types at namespace scope.
- Avoid any blank lines immediately after class declarations or definitions.
- Put early-return statements on a separate line beneath their condition, without a blank line between the condition and the return. Add one blank line after each guard clause to separate it from the following code. Add one blank line before the method's final return, unless that return is the method's only statement. Apply the same rules to property getters and all existing C# files.
- No file should end with a newline. Do not append LF or CRLF after the final content; apply this rule to all existing text files.
- Follow surrounding naming and formatting conventions. Nullable reference types and implicit usings are enabled.
- Add focused regression tests for bug fixes and relevant tests for behavior changes.
- Preserve request verification, transport security and existing data.
- Never commit secrets or log CV text, credentials, tokens or sensitive search parameters.
- Do not add public registration or external email delivery without explicit user instructions.
- Use isolated test data; never run automated tests against real candidate data.
- Do not weaken production HTTPS or key-protection requirements to make tests or startup pass.

## Navigation
- `Applications.sln`: Visual Studio solution for ApplicationLog and its tests.
- `ApplicationLog.csproj` and `Program.cs`: Blazor project and local web host.
- `Components/`: Razor markup and C# code-behind for the folder tree, editor and previews.
- `Library.cs` and `TextDocument.cs`: local file access, folder ordering, text encoding and autosave.
- `Preview.cs`: document preview readers.
- `ApplicationLog.Tests/`: xUnit tests and bUnit component tests.
- `docs/applicationlog-design.md`: design and implementation overview.

## Verification and reporting
Run from the repository root with the .NET 10 SDK:

```sh
dotnet restore Applications.sln
dotnet build Applications.sln --no-restore
dotnet test Applications.sln --no-build
```

- Run area-specific checks described in any applicable nested instructions.
- Follow `README.md` for local startup and test instructions.
- Report test counts and coverage percentages separately when coverage is collected.
- Explain changed behavior, completed checks and material limitations.
- Distinguish local verification from confirmed GitHub Actions results. Do not claim tests passed unless they were run.
- Keep current test counts, coverage percentages and temporary branch names out of instruction files.