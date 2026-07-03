# Scheduling & Revert

Moved out of `sync-workflow.md` (kept under 200 lines). Covers the Phase 7 unattended
scheduler and the Phase 8 revert/undo feature.

## Unattended Scheduled Backfill (Phase 7)
`ScheduleService` runs `RunScheduledBackfillAsync` on a fixed interval. Click the
status-bar `lblSchedule` ("Schedule: off" / "Next run: HH:mm") to open
`SchedulerSettingsDialog` and configure:

| Setting              | Default | Notes                                         |
|----------------------|---------|-----------------------------------------------|
| ScheduleEnabled      | false   | Master toggle                                 |
| IntervalMinutes      | 60      | Time between runs                             |
| EvalWindowHours      | 24      | Rolling window: `[now - N, now]`              |
| Direction            | Both    | `PrimaryToSecondary` / `SecondaryToPrimary` / `Both` |
| TagFilter            | `*`     | Mask applied to the shared-tag intersection   |
| RunOnStartup         | false   | Trigger one run shortly after app launch      |

Each scheduled run is gated by `IsPrimaryConnected && IsSecondaryConnected && !_isBusy`,
writes a one-line audit entry to `{exe}/logs/schedule-YYYY-MM.log` (no modal popup), and
auto-refreshes the UI via `AutoRefreshAfterBackfill` afterward.

Scheduled runs are fully headless (Phase 9): `_suppressOpDialog` is set around the whole
run, so neither the backfill nor its auto-refresh ever pops the modal progress dialog.
The rolling window's end is still clamped by the live-edge guard inside `ExecuteBackfill`.

`SchedulerSettingsDialog` also offers a **manual tag multiselect** (radio: mask vs.
explicit list). When `ScheduleUseTagList` is set, `RunScheduledBackfillAsync` browses
`*`, intersects both servers, then narrows to `ScheduleTagList`.

## Revert / Undo a Backfill (Phase 8)
Every backfill journals the exact `(tag, timestamp)` pairs it wrote+verified to
`logs/backfill-journal/{id}.json` (`BackfillJournalService`). The "Backfill
History…" button opens `BackfillHistoryDialog`; reverting deletes **only** those
timestamps via `HistorianDataService.DeleteSamples` → `IData.Delete(string[],
DateTime[], out ItemErrors)` (chunked 1000/call) — pre-existing samples are never
touched (worst case: incomplete revert, never a wrong deletion). Double-guarded
(enable-checkbox + confirm); target must be connected (matched by recorded hostname);
entry marked `Reverted` only on a clean pass, else kept Active for retry.

Journal timestamps are stored at whole-second resolution (what Historian actually
holds), and a journaled second is by construction a second the target did NOT have
before the run — so a revert can never delete pre-existing data, even across
overlapping backfill runs (a later run never re-journals seconds an earlier run wrote).

### Journal is also the timeline's memory (Phase 9)
Non-reverted journal entries drive the blue "backfilled by this tool" band on the
SYNC TIMELINE (matched by TargetHost + tag, clipped to the analysis window). Reverting
a run makes its band disappear on the next analysis.

### Cancel → keep or revert (Phase 9)
Cancelling a manual backfill mid-run stops at the current batch, journals what was
already written, then asks: **keep** the copied data (revertable later via history)
or **revert now** (deletes exactly the journaled samples). Default is keep.
