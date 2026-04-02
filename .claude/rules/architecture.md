---
description: Overall application architecture, dual-server design, and v2 service-layer goals
---

# Architecture

## Dual-Server Model
The application always operates against two Historian instances:
- **Primary** (`sc`) — the authoritative data source
- **Secondary** (`scs`) — the redundant/mirror server

Both are `ServerConnection` objects. Most operations require both to be connected. The secondary
hostname is derived from the primary at startup: if the primary ends with `PC2`, the secondary
strips that suffix; otherwise `PC2` is appended.

## UI Layer (WinForms)
- Single form: `Main` (inherits `Form`)
- Entry point: `Program.cs` → `Application.Run(new Main())`
- All button click handlers are named `On_cmd<Action>_Click`
- Status feedback goes to `tsStatus` (status strip label) and `txt_Log` (textbox log)
- Grids: `dataGridDataPrimary`, `dataGridDataSecondary`, `dataGridCompare`
- Tag selectors: `cboTagsPrimary`, `cboTagsSecondary`
- **Do not use WPF.** v1 had dead WPF files (`MainWindow.xaml`, `App.xaml`) — v2 must not include them.

## v1 Problem: Monolithic Main.cs
In v1, `Main.cs` (1402 lines) mixed UI events, Historian API calls, and domain logic in one class.
This made the synchronization algorithm untestable without the UI.

## v2 Target: Service Layer
Extract these three services from the form class:

| Service | Responsibility |
|---|---|
| `HistorianConnectionService` | Open/close `ServerConnection`, expose connection state |
| `HistorianDataService` | All `IData` and `ITags` queries and writes |
| `GapAnalysisService` | Gap detection, batch planning, backfill orchestration |

The form should only wire UI events and delegate to services. Services must have no `Form`/`Control`
dependencies so they can be unit tested independently.

## Data Flow per Use Case

### Connect
1. User enters primary hostname → secondary derived automatically
2. `HistorianConnectionService` creates two `ServerConnection` instances
3. Status reflected in status strip + log

### Browse Tags
1. `ITags.Query` with `TagnameMask` filter and `DataType = Float`
2. Paginated with `PageSize = 100` using `while` loop
3. Results bound to comboboxes

### Read Samples
- Interpolated query: `now-10min` to `now`, 10 samples, selected tag
- Raw query: from `dtStartdate` to now, all samples

### Compare
1. Raw query both tags from same start date
2. Align by timestamp into `SortedDictionary<DateTime, CompareRowData>`
3. Missing side shown as `"missing"` in grid

### Copy / Backfill
1. Gap analysis must run first (populates `lastPrimaryHistSyncGap` / `lastSecondaryHistSyncGap`)
2. `CopySamplesForHistSyncGaps` iterates gap windows → batches → reads source → writes target
3. Only backfillable batches (`CanBackfill = true`) are written

### HistSync Gap Analysis
1. Check tag `HistSync` exists on both servers
2. `AnalyzeGaps` per server: compute median interval, detect gaps, split into 10-min batches
3. Mark `CanBackfill` per batch by checking source server has data in that range
4. Display coverage ratio and visual coverage bar
