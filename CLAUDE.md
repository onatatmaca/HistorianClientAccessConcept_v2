# HistorianClientAccessConcept v2

## Project Overview
A Windows Forms demo application for GE Proficy Historian, written in C#. Connects to two
Historian servers (Primary + Secondary) and provides tag browsing, data reading, side-by-side
comparison, gap analysis, and selective backfill between servers.

This is **v2** — a clean rewrite of v1 (`../HistorianClientAccessConcept`), which served as the
proof-of-concept. The v2 goal is to extract testable service layers and deliver a production-grade
selective synchronization pipeline.

## Tech Stack

| Component        | Detail                                      |
|------------------|---------------------------------------------|
| Language         | C#                                          |
| UI Framework     | Windows Forms (WinForms)                    |
| Target Framework | .NET Framework 4.8                          |
| Platform Target  | x86                                         |
| External API     | Proficy.Historian.ClientAccess.API v1.0.0.0 |
| IDE              | Visual Studio (Windows only)                |

**API DLL hint path:**
`C:\Program Files\Proficy\Proficy Historian\Assemblies\Proficy.Historian.ClientAccess.API.dll`

## Key Commands

```
Build:  Open ClientAccessDemo.sln → Build → Build Solution (Ctrl+Shift+B)
Run:    F5 in Visual Studio (Debug, x86)
Target: x86 / .NET 4.8 — do not change platform target
```

## Folder Structure

```
HistorianClientAccessConcept_v2/
├── CLAUDE.md                        ← this file
├── .claude/rules/                   ← detailed rule files (see below)
├── ClientAccessDemo.sln
├── ClientAccessDemo.csproj
├── Program.cs                       ← WinForms entry point (Application.Run)
├── Main.cs                          ← primary form + all event handlers
├── Main.Designer.cs                 ← generated UI layout (do not hand-edit)
├── ORC_HistorianSync.cs             ← ORC_HistStats DTO
└── Properties/
```

## Rule Files

All detailed conventions, patterns, and domain knowledge live in `.claude/rules/`:

| File | Covers |
|------|--------|
| [`architecture.md`](.claude/rules/architecture.md) | Dual-server design, service-layer goals, data flow |
| [`csharp-conventions.md`](.claude/rules/csharp-conventions.md) | C# coding conventions (scoped to `**/*.cs`) |
| [`historian-api.md`](.claude/rules/historian-api.md) | Proficy API patterns, query types, write patterns |
| [`sync-workflow.md`](.claude/rules/sync-workflow.md) | Gap analysis, backfill, HistSync tag behavior |
| [`known-issues.md`](.claude/rules/known-issues.md) | Bugs, null risks, resource leaks, v1 pitfalls to avoid |
| [`roadmap.md`](.claude/rules/roadmap.md) | Phase completion status and next items |

## Documentation Protocol

After completing any feature or fixing a significant bug:
- Update the relevant `.claude/rules/` file with what changed
- Mark phases complete in `roadmap.md`
- Log non-obvious bugs/fixes in `known-issues.md` or the relevant rule file
- Keep all rule files under 200 lines
