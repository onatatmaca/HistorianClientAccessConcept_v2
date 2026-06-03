---
description: Historical bugs and design defects found in v1 — kept as a reference so v2 does not regress into them
---

# Known Issues — v1 (Historical Reference)

All items below are bugs or design defects found in v1 (`../HistorianClientAccessConcept/Main.cs`).
They are all fixed in v2; this file exists as a reference so similar regressions can be caught.

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
