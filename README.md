# Historian Sync Tool

A Windows Forms utility for keeping a pair of GE Proficy Historian servers
(primary + secondary) in sync. Connect to both servers, run a per-tag gap analysis,
preview the cross-server diff, and selectively backfill missing samples in either
direction. Supports unattended scheduled syncs and full revert of any backfill.

## Features

- **Dual-server connection.** Connect to a primary and secondary historian; the
  secondary hostname is derived from the primary at startup.
- **Tag browsing and inspection.** Paginated tag query, interpolated and raw sample
  reads, side-by-side comparison of two tags from the same date range.
- **Per-tag gap analysis.** Coverage bars and a cross-server diff table that reads
  *"Primary has N samples Secondary lacks"* for the selected tag(s).
- **Bidirectional backfill preview.** One dialog showing both copy directions
  (Primary&nbsp;→&nbsp;Secondary and Secondary&nbsp;→&nbsp;Primary), with independent
  per-side tag selection. Diff is computed at whole-second resolution to match how
  Historian actually stores timestamps.
- **Read-after-write verification.** Every written batch is re-read and each second
  is confirmed present, so a write that doesn't land (e.g. archive compression) is
  reported as a failure rather than silently looping forever.
- **Scheduler.** Unattended rolling-window backfills on a fixed interval, with
  configurable direction (P→S, S→P, both), evaluation window, and either a tag-name
  mask or an explicit multiselect tag list. Status visible in the status bar.
- **Backfill journal and revert.** Every successful (tag, timestamp) pair is logged
  to disk. A guarded *Backfill History* dialog lists past runs (manual and scheduled)
  and lets you revert one — only the samples that run wrote are removed, never
  pre-existing data.
- **Sync reports.** Per-tag breakdown after every run, exportable as CSV or TXT.
  Past runs can be re-opened from the *Backfill History* dialog and exported the
  same way.

## Requirements

- Windows with Proficy Historian Client Access API installed:
  `C:\Program Files\Proficy\Proficy Historian\Assemblies\Proficy.Historian.ClientAccess.API.dll`
- Visual Studio 2022 with the **.NET Framework 4.8** targeting pack
- Two reachable historian servers (read-only access is enough for analysis;
  write access on the target is required for backfill)

## Build and run

1. Open `HistorianSyncTool.sln` in Visual Studio.
2. Build (Ctrl+Shift+B). Configuration **Debug**, platform **x86** — the Proficy
   API DLL is 32-bit only, do not change the platform target to AnyCPU.
3. Press F5 to run.

## Project layout

```
Forms/             WinForms UI (MainForm + dialogs)
Models/            Plain data types (gap windows, run reports, journal entries)
Services/          Historian I/O, gap analysis, scheduler, journaling
UI/                Custom controls and theme
HistorianSyncTool.Tests/   MSTest unit tests for pure-logic services
docs/              Technical documentation (English + German, print-friendly HTML)
_backup/           Reference-only archived code (NOT compiled into the build)
```

## Configuration

A handful of knobs live in `app.config`:

| Key                 | Default     | Purpose                                       |
|---------------------|-------------|-----------------------------------------------|
| `SyncTagName`       | `HistSync`  | Heartbeat tag used when no tag is selected    |
| `BatchSizeMinutes`  | `10`        | Bucket size for grouping samples into writes  |
| `MinimumGapSeconds` | `120`       | Floor for the gap-detection threshold         |
| `RetryAttempts`     | `3`         | Per-call retry attempts on transient errors   |

Scheduler settings live in `Properties.Settings` and are edited via the
*Scheduler Settings* dialog (status-bar entry point).

## Documentation

- [Technical documentation — English](docs/index.html)
- [Technical documentation — German](docs/index.de.html)
