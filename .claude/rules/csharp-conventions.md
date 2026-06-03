---
description: C# coding conventions for this project, scoped to **/*.cs
globs: ["**/*.cs"]
---

# C# Conventions

## Project Settings
- **Namespace:** `HistorianCSharp` (root); `ORC_HistSync` (DTO file)
- **Target framework:** .NET Framework 4.8 — do not upgrade without explicit decision
- **Platform target:** x86 — required by the Proficy API DLL (MSIL, loaded in 32-bit host)
- **Output type:** WinExe
- No NuGet packages — all dependencies are GAC or local DLL references

## File Responsibilities

| File | Role |
|---|---|
| `Program.cs` | Entry point only — `Application.Run(new Main())` |
| `Main.cs` | Form class, event handlers, inner domain classes |
| `Main.Designer.cs` | Auto-generated layout — never hand-edit |
| `ORC_HistorianSync.cs` | DTOs only (`ORC_HistStats`) |

## Naming
- Event handlers: `On_cmd<Action>_Click` (e.g., `On_cmdConnect_Click`)
- Private fields: camelCase (e.g., `lastPrimaryHistSyncGap`)
- Properties: PascalCase
- Inner/helper classes inside `Main.cs`: PascalCase, private nested classes

## Inner Classes (v1 pattern — refactor in v2)
These were private nested classes inside `Main` in v1. In v2 extract them to their own files:
- `GapAnalysisResult` → `Models/GapAnalysisResult.cs`
- `GapWindow` → `Models/GapWindow.cs`
- `GapBatch` → `Models/GapBatch.cs`
- `CompareRowData` → `Models/CompareRowData.cs`

## Error Handling
- Wrap all Historian API calls in `try/catch (Exception ex)` — the API throws on network errors
- Log errors via `Log(ex.Message)` and set `tsStatus` to red
- Do not swallow exceptions silently
- Do not use bare `catch {}` without logging

## Logging
- `Log(string message)` appends to `txt_Log` with a timestamp prefix
- Status strip (`tsStatus`) shows the last single-line state; color = Blue (success) / Red (error)
- No logging framework in v1; v2 should introduce at minimum `System.Diagnostics.Trace`

## Async
- v1 was entirely synchronous (all handlers on UI thread — cursor set to WaitCursor during ops)
- v2 should use `async/await` with `Task.Run` for all Historian API calls to keep UI responsive
- Never call `Thread.Sleep` on the UI thread

## Data Types
- All tag data in this demo is `float` (filtering enforced at browse time)
- Timestamps are `DateTime` (local time — Historian returns local by default)
- `DataSamples<float>` for write payloads; `DataSamples<MultiFieldValue>` for multi-field tags

## DO NOT
- Reference `System.Windows.Forms` from service classes
- Hand-edit `Main.Designer.cs` or `*.Designer.cs` files
- Change platform target to AnyCPU — the Proficy DLL requires x86
- Add WPF references or XAML files
