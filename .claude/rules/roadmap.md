---
description: Phase completion status and next items for v2 development
---

# Roadmap

## Status Legend
- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete

---

## Phase 1 — Service Layer Extraction
Extract domain logic out of `Main.cs` into independently testable classes.

- [ ] Create `HistorianConnectionService`
  - Owns both `ServerConnection` instances
  - Exposes `ConnectPrimary(host)`, `ConnectSecondary(host)`, `IsConnected(side)`
  - Implements `IDisposable`, calls `Disconnect()` on dispose
- [ ] Create `HistorianDataService`
  - All `IData` and `ITags` calls move here
  - Returns plain C# types (not Historian API types) where possible
  - No `Form` or `Control` dependencies
- [ ] Create `GapAnalysisService`
  - `AnalyzeGaps(samples) → GapAnalysisResult`
  - `PlanBatches(gapWindows, batchSize) → List<GapBatch>`
  - `CheckBackfillFeasibility(batch, otherServerSamples) → bool`
- [ ] Move model classes to `Models/` folder
  - `GapAnalysisResult.cs`, `GapWindow.cs`, `GapBatch.cs`, `CompareRowData.cs`
- [ ] Slim `Main.cs` to UI wiring only (event handlers delegate to services)

---

## Phase 2 — Selective Synchronization
Replace full-range copy with gap-targeted, backfill-only writes.

- [ ] Only write batches where `CanBackfill = true`
- [ ] Preserve original sample quality (do not force `DataQuality.Good`)
- [ ] Make `HistSync` tag name configurable (app.config or settings)
- [ ] Make batch size configurable (default: 10 minutes)
- [ ] Add per-batch retry with exponential backoff (max 3 attempts)
- [ ] Surface run-report: gaps found, batches attempted, batches succeeded, samples written

---

## Phase 3 — Read-After-Write Verification
Confirm data was actually written before marking a batch as done.

- [ ] After each batch write, re-query the target for that time range
- [ ] Compare returned sample count to written sample count
- [ ] Log verification result per batch (pass / mismatch / no data)
- [ ] Mark batch `Verified` or `VerificationFailed` in run-report

---

## Phase 4 — Automated Tests
Make core logic testable without the UI or a live Historian instance.

- [ ] Add a test project (`ClientAccessDemo.Tests`)
- [ ] Unit test `GapAnalysisService` with synthetic sample lists
  - Median interval calculation
  - Gap detection at 1.5× threshold
  - Batch boundary correctness (half-open interval)
  - Coverage ratio calculation
- [ ] Unit test `HistorianDataService` with a mock `ServerConnection` interface
- [ ] Integration test harness (optional): requires live Historian, tagged as `[Integration]`

---

## Bugs Fixed vs v1 (track here as each is resolved)

| Bug | Phase Fixed | Notes |
|-----|-------------|-------|
| ServerConnection never disposed | 1 | Fixed in `HistorianConnectionService.Dispose()` |
| Log uses dtTimestamp instead of DateTime.Now | 1 | Fixed in new `Log()` helper |
| HasSampleInRange strict boundary exclusion | 2 | Use `>=` start, `<` end |
| TagQueryParams not reset between servers in stats | 1 | New params instance per server |
| All ops on UI thread | 1 | async/await + Task.Run in service calls |
| No retry on API calls | 2 | Retry helper in HistorianDataService |
| HistSync tag name hardcoded | 2 | Moved to config |
| Batch size hardcoded (10 min) | 2 | Moved to config |
| Quality forced to Good on backfill writes | 2 | Pass through source quality |
