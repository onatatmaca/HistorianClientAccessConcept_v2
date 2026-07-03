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
├── HistorianSyncTool.sln
├── HistorianSyncTool.csproj
├── Program.cs                       ← WinForms entry point
├── Forms/                           ← MainForm, TagSelectionDialog, BidirectionalBackfillDialog,
│                                      SyncReportDialog, SchedulerSettingsDialog,
│                                      BackfillHistoryDialog, ProgressDialog
├── Services/                        ← HistorianConnectionService, HistorianDataService,
│                                      GapAnalysisService, SyncPlanner (backfill planning:
│                                      aligned vs independent streams), RetryHelper,
│                                      SampleFilter, SampleBucketer, IntervalBuilder,
│                                      HostInputParser (pure-logic seams with unit tests),
│                                      ProficyEndpoint (IP/port connect), ScheduleService,
│                                      ScheduleLogger, BackfillJournalService
├── Models/                          ← GapAnalysisResult, GapWindow, GapBatch,
│                                      SyncRunReport, TagBackfillResult, ServerStats,
│                                      BackfillJournalEntry / BackfillJournalTag,
│                                      TimelineData (TimeRange, TimelineTrackData, CopyableSegment)
├── UI/                              ← AppTheme + Controls (FlatButton, CoverageBar,
│                                      GapTimeline, SectionHeader, ConnectionDot,
│                                      CollapsiblePanel)
├── lib/                             ← Local Proficy DLL copy for building WITHOUT the
│                                      Historian client installed (gitignored; build with
│                                      dotnet msbuild /p:ReferencePath=<repo>\lib)
├── _backup/                         ← Reference-only archived code (NOT compiled).
│                                      Currently holds the pre-direct-comparison
│                                      batch-based backfill implementation with a
│                                      README explaining revert paths.
├── HistorianSyncTool.Tests/         ← MSTest project (GapAnalysisService + RetryHelper
│                                      + SampleFilter + SampleBucketer + IntervalBuilder)
├── logs/                            ← Created at runtime — rolling monthly schedule
│                                      audit (`schedule-YYYY-MM.log`) + per-run
│                                      revert journal (`backfill-journal/{id}.json`)
└── Properties/
```

## Rule Files

All detailed conventions, patterns, and domain knowledge live in `.claude/rules/`:

| File | Covers |
|------|--------|
| [`architecture.md`](.claude/rules/architecture.md) | Dual-server design, service-layer goals, data flow |
| [`csharp-conventions.md`](.claude/rules/csharp-conventions.md) | C# coding conventions (scoped to `**/*.cs`) |
| [`historian-api.md`](.claude/rules/historian-api.md) | Proficy API patterns, query types, write patterns |
| [`sync-workflow.md`](.claude/rules/sync-workflow.md) | Gap analysis, sync timeline, backfill, preview dialogs |
| [`scheduling-and-revert.md`](.claude/rules/scheduling-and-revert.md) | Unattended scheduler (Phase 7) + revert/undo journal (Phase 8) |
| [`known-issues.md`](.claude/rules/known-issues.md) | v2 bugs fixed (phase 8+) + open deferred items |
| [`known-issues-archive.md`](.claude/rules/known-issues-archive.md) | Resolved v2 issues (phases 5–6) + v1-bug tracking table |
| [`known-issues-v1.md`](.claude/rules/known-issues-v1.md) | v1 historical pitfalls — reference so v2 doesn't regress |
| [`roadmap.md`](.claude/rules/roadmap.md) | Phase completion status and next items |
| [`test-environment.md`](.claude/rules/test-environment.md) | Test Historian servers, Genthin data ranges, MigrateIHA |

## Documentation Protocol

After completing any feature or fixing a significant bug:
- Update the relevant `.claude/rules/` file with what changed
- Mark phases complete in `roadmap.md`
- Log non-obvious bugs/fixes in `known-issues.md` or the relevant rule file
- Keep all rule files under 200 lines
