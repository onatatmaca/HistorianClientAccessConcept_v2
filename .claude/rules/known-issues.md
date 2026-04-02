---
description: Known bugs and v1 pitfalls to avoid when building v2
---

# Known Issues

All items below are bugs or design defects found in v1 (`../HistorianClientAccessConcept/Main.cs`).
Do NOT carry them into v2.

---

## BUG: Double AddRange on Last Pagination Page
**Location:** `On_cmdBrowseTags_Click` and `On_cmdGetServerStats_Click`

```csharp
// v1 code — correct pattern but produces duplicates if not understood
while (sc.ITags.Query(ref query, out tmp))
    allTags.AddRange(tmp);
allTags.AddRange(tmp); // adds last page — this is intentional and correct
```

The double-add is required because `Query` returns `false` on the final page but still
populates `tmp`. However, in the stats handler the `TagQueryParams` object was reused
without reset between primary and secondary calls, which could return stale paged state.
**Fix in v2:** Reset or create a new `TagQueryParams` before each server's query.

---

## BUG: Null Reference on lastPrimaryHistSyncGap / lastSecondaryHistSyncGap
**Location:** `On_cmdMoveToPrimary_Click`, `On_cmdMoveToSecondary_Click`

These fields are checked with `== null` before use in the copy handlers, which is correct.
However, if the gap analysis ran but returned no gaps (e.g., server has complete data),
the field is set to a result object where `Gaps` is an empty list — not null. This path
is handled. The risk was in earlier versions where the null check was missing entirely.

**Fix in v2:** Use a proper result type with a clear `HasGap` flag; never rely on
reference nullability to represent "analysis not run."

---

## BUG: Boundary Exclusion in HasSampleInRange
**Location:** v1 inline logic in gap batch backfill feasibility check

Used strict `sampleTime > batch.Start && sampleTime < batch.End`, which excludes samples
exactly on the boundary timestamps.

**Fix in v2:** Use half-open interval: `sampleTime >= batch.Start && sampleTime < batch.End`

---

## BUG: ServerConnection Never Disposed
**Location:** `Main.cs` form fields `sc`, `scs`

Both `ServerConnection` objects are created on connect and abandoned on form close.
`Disconnect()` is never called explicitly.

**Fix in v2:** Call `sc.Disconnect()` and `scs.Disconnect()` in `Form.OnFormClosing` or
in the `Dispose` method of the owning service.

---

## BUG: Log Timestamp Uses dtTimestamp (User-Selected), Not DateTime.Now
**Location:** `Log(string message)` in v1

```csharp
private void Log(string message)
{
    txt_Log.AppendText(dtTimestamp.Value + " - " + message + Environment.NewLine);
}
```

The log prefix uses the DateTimePicker control value (intended for write operations),
not the current wall-clock time. Log entries show the wrong timestamp if the user has
changed the date picker.

**Fix in v2:** Use `DateTime.Now` in the log method, not a UI control value.

---

## DESIGN: Tag Filter Hardcoded to UI TextBox
Tags are filtered only by whatever is typed in `txt_TagnameFilter`. There is no persistent
configuration. If the textbox is blank, all tags are returned (no mask = wildcard).

**Fix in v2:** Add an `appsettings`/config-file default for the tag name mask. Make UI
pre-populate from config.

---

## DESIGN: All Operations on UI Thread (No Async)
All Historian API calls block the UI thread. Long-running queries (e.g., full-range raw
query across all tags for stats) can freeze the form for seconds.

**Fix in v2:** Wrap all service calls in `Task.Run(...)` and use `async/await`. Disable
action buttons during execution to prevent re-entrancy.

---

## DEAD CODE: Unused WPF Files in v1
`MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml`, `App.xaml.cs` exist in v1 but are
not the active UI. The entry point is WinForms (`Program.cs`). These files caused confusion.

**Fix in v2:** Do not include any WPF files. WinForms only.

---

## DESIGN: No Retry on API Calls
Any transient network error causes immediate failure with a log message. No retry or
exponential backoff.

**Fix in v2:** Wrap `HistorianDataService` methods in a retry helper (max 3 attempts,
500ms / 1s / 2s backoff) for all `IData` and `ITags` calls.

---

## BUG: Per-Tag Gap Analysis Produces False Gaps on Bimodal Data (v2, fixed Phase 5)
**Location:** Gap analysis when applied to non-HistSync tags

Tags with bimodal sampling (e.g., pairs of samples 1s apart, then 15s between pairs) caused
the median-based gap detection to pick up 1s as the interval. Every normal 15s pause was flagged
as a gap (805+ false gaps), producing wildly wrong coverage (6%/1% instead of near-100%).

**Fix:** Gap analysis now **always** uses HistSync (steady heartbeat). No per-tag gap analysis.
Radio buttons removed. Coverage bars always show HistSync coverage.

---

## BUG: DateTimePicker Shows Arrows Instead of Calendar Dropdown (v2, fixed Phase 5)
**Location:** `MakeDtp()` in `MainForm.Designer.cs`

`ShowUpDown = true` caused DateTimePicker to show up/down arrows instead of a calendar popup.

**Fix:** Changed to `ShowUpDown = false`.

---

## BUG: Gap Grid Duration Column Cut Off (v2, fixed Phase 5)
**Location:** `SetupGapGrid()` in `MainForm.Designer.cs`

The gap grid used `FillWeight` on columns but didn't have `AutoSizeColumnsMode = Fill`, so
the Duration column was truncated on the right edge.

**Fix:** Added `gridGaps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill`.

---

## DESIGN: No UI Refresh After Backfill (v2, fixed Phase 5)
After `ExecuteBackfill` completed, the gap analysis results, coverage bars, and data tables
were not refreshed. The user had to manually re-run gap analysis to see updated coverage.

**Fix:** Added `AutoRefreshAfterBackfill()` which calls `RunGapAnalysis` after every backfill.
