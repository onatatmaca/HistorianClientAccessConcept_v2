# UTC frame vs `DateTime.Now` — the 2026-07-16 incident and audit

Split out of [`known-issues.md`](known-issues.md) (200-line budget). Referenced from
[`architecture.md`](architecture.md) and [`historian-api.md`](historian-api.md); the rules
that follow from it live there.

## INCIDENT + OPEN DEFECTS: UTC frame vs `DateTime.Now` (2026-07-16, audited)

**Proven** (live probes, see [`historian-api.md`](historian-api.md)): the API frame is **UTC**
(returned `Kind=Utc`), and a `Local`/`Unspecified` **query start** is converted local→UTC, so the
query **starts early by the UTC offset** (1 h winter / 2 h summer) and returns samples before `from`.

### Data loss (external tooling, NOT the app) — fixed + data restored
A throwaway helper read `[from,to]` with only `if (ts > to) break;` (**no `ts < from` guard**) and
deleted every timestamp it read ⇒ **~1 h of Secondary data BEFORE the window was destroyed**;
restored from Primary (2,343 samples). **The app cannot do this**: its only `IData.Delete`
consumes **journal ticks**, never a read, and `SampleFilter` has the `if (s.Time < start)
continue;` guard. `IData.Delete` has no range overload (verified by reflection).

### FIXED — the app now converts at the API boundary (single point)
`HistorianDataService` gained `ToApi()` / `FromApi()`; **the API frame stops at that service and the
rest of the app works in LOCAL time**. Applied to every crossing: query bounds, returned times,
writes, deletes, `ReadRaw`, `ReadInterpolated`. `ReadRawInRange` runs its chunk loop in UTC
(`cursor = lastTs.AddTicks(1)` **preserves Kind** — never `new DateTime(ticks+1)`), clips in UTC,
then hands back local. **Verified live** against Historian Trend on a picker-style window.

This one change fixed **by construction**: the dead live-edge guard, the scheduler's future-window
no-op, the empty "Last 1h" preset, the ~1–2 h window shift (first hour silently dropped: 8,383 read
→ 8,167 kept), and `GapAnalysisService`'s phantom trailing gray band — all were `DateTime.Now`
compared against UTC sample times; both sides are now local.

Also fixed: reads now `ThrowOnItemErrors` (an errored read must never look like "no data" — that
would force exact-diff on an empty target, mass-copy, journal it, and a later revert would delete
pre-existing data). Verified live that an empty window **and** a nonexistent tag both return
`ItemErrors=0`, so it cannot fire on the normal empty case. `LiveEdgeGraceSeconds` now requires
`> 0` (a 0 grace disabled the write/collector race guard).

**Journal frame — deliberately unchanged (this was the landmine):** journals on disk hold **UTC**
ticks. Journaling now converts `bs.Time.ToUniversalTime()` and revert re-tags
`new DateTime(tk, DateTimeKind.Utc)`, so **legacy and new entries revert identically with no
migration**. Do NOT "simplify" this to local ticks — old journals would then delete at an instant
1–2 h off, i.e. real plant data.

**Never affected:** copied values always landed at the correct absolute instant (same frame both
sides), and revert always deleted the correct instant (journal stores `long[] Ticks`, never
`DateTime`, so the serializer never converted them). No data was ever corrupted by this class.

**Still open:** revert reports *requested* not *confirmed* deletions (`MainForm.cs:1693`, no
read-back) — fails safe (deletes nothing, but the message would be optimistic).

---
