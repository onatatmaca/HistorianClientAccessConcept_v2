# Test Environment & Loading Real Plant Data

How the test Historian servers are set up and how to load **real plant archive data** into them
(so the tool can be exercised against real values instead of simulated ones).

## Test Historian servers (Primary/Secondary pair)
- **TESTSV1** = Primary (mirrors plant server *GENTHIN*); **TESTSV1PC2** = Secondary (*GENTHINPC2*).
- Windows Server 2019, **Proficy Historian 9.1** (9.1.5173.0). Archive dir
  `C:\Proficy Historian Data\Archives`. ClientAccess TCP port **14000**.
- In the tool, set Primary host = `TESTSV1`; it auto-derives `TESTSV1PC2`.
- Normally hold only simulated data (**baseline 7 tags** each). The box is a multi-plant rig — it also
  contains **orphaned** archive files from other plants (COGSSV1, MUSTERDEU) that are present as files
  but NOT loaded (the config only knows the 7 sim tags).
- Connection specifics (IPs, credentials, SSH/plink method, RDP status) live in the agent's local
  project memory, not committed here.

## Loading a real plant's data (MigrateIHA)
Objective: import a plant's Historian archive (`.iha`) + config (`.ihc`) so the tool reads real data.

- **Copying `.iha` into the Archives folder does nothing.** Historian only serves archives that are
  registered in its config with their tags created. (Proof: the COGSSV1/MUSTERDEU `.iha` files sit in
  the Archives folder unused — tag count stays at the 7 sim tags.)
- The supported importer is **`MigrateIHA_x64.exe`** (`…\Proficy Historian\x64\`). It re-inserts the
  archive's samples into the running Data Archiver and creates the tags from the `.ihc`.
- **MigrateIHA is GUI-only** — it cannot be driven over SSH (it launches invisibly in the service
  session and exits). Run it in an interactive desktop (RDP or the VM console). No headless importer is
  installed on this box (`ihArchiveInfo.exe` absent; the Archive Pre-Ingestion service exists but is
  complex/unverified).

### MigrateIHA gotchas (learned 2026-07)
1. **"Migration log could not be created" popup (one per log line):** MigrateIHA hardcodes its log to
   `C:\IHAMigration.Log` (C:\ root), which non-elevated processes can't create. Fix: pre-create that file
   writable (`Everyone:Modify`), **or** run MigrateIHA **as Administrator**.
2. **0 tags / 0 samples migrated if the `.ihc` is omitted:** you MUST point MigrateIHA at the matching
   `.ihc`. Without it, it reads the archive's tags but migrates 0 ("Total Tags to be migrated: 0"). With
   the `.ihc` + **"Migrate All Tags"** + full time range, it creates the tags and migrates the data.
3. **Verify with the ClientAccess API, not ping:** connect with a real account (an empty username is
   rejected); `IServer.GetConfiguration().ActualTags` is the quickest success metric (it rises above the
   7 baseline). Data is historical — query the archive's real date range, not `now-10min`.

## Genthin data — LOADED ✅ (migration completed 2026-07-02, 0 errors)
- **TESTSV1**: 51 465 860 samples, 72 Genthin tags (79 total incl. sim). Range
  **2024-05-02 → 2026-05-28**.
- **TESTSV1PC2**: 14 350 524 samples, 25 tags (32 total). Range **2023-12-28 → 2026-05-29**.
- Overlap ≈ **2024-05 → 2026-05** (~2 years); ~23 tags exist on BOTH servers.
- Tag naming: prefix `STAT6.`, suffix `.F_CV` (iFIX float values), e.g.
  `STAT6.TEMP_05_GAA_SCALE.F_CV`. Data is historical — query 2024–2026 ranges, not `now-…`.

## PC2 license: WAS 32-tag capped, UPGRADED to full on 2026-07-03
`IServer.GetConfiguration()` on each server:
- **TESTSV1**: `MaxTags = 100000`, full `PlantHistorian` feature set → holds all 73 plant
  tags (79 total). Migration log: 0 errors, 0 skipped.
- **TESTSV1PC2**: originally `MaxTags = 32` (GE Historian free edition) and FULL at 32/32 —
  which is why its first migration logged **114 `WARNING: Unable to add Tag`** lines (no
  free slots; a license cap, NOT a data error — MigrateIHA just says "Unable to add" while
  the archiver rejects the add). **Upgraded 2026-07-03**: now `MaxTags = 100000`,
  `MaxUsers = 10`, full feature set (matches SV1). At upgrade time `ActualTags` was still
  32 — lifting the cap does NOT backfill tags; a **re-run of MigrateIHA** (with the `.ihc`
  + Migrate All Tags + full range) is required to actually create the ~114 previously-failed
  tags. **Re-check `ActualTags` and the shared-tag intersection after that migration.**

**Re-migration DONE (observed 2026-07-03):** the sync tool's Preview dialog now reports
**78 shared tag(s)** between the servers — PC2 holds the full Genthin set. The earlier
20-shared-tag limitation is history.
- "Fewer tags than expected" in the UI had TWO causes: (1) the persisted browse filter is
  `STAT6.T*` (only T-tags) — set it to `*`/`STAT6.*` to see all; (2) PC2 genuinely had 25
  before the license upgrade + re-migration.

## Connecting the tool to the test servers
- The tool accepts hostname OR IP, with optional port: `TESTSV1`, `192.168.50.186`,
  `TESTSV1:13000`… (IPs go through the lenient-identity path — see
  [`historian-api.md`](historian-api.md)). ClientAccess WCF port here is **13000**
  (the DLL default; 14000 is a different service).
- Remote connections need a real login (empty username is rejected off-box): set
  app.config `HistorianUsername`/`HistorianPassword` (in `bin\...\HistorianSyncTool.exe.config`
  at runtime — don't commit credentials to the repo default).

## Notes
- The VMs don't answer ICMP ping (Windows default) even when fully reachable — test with a TCP port check.
- RDP was enabled on both test VMs for interactive work (revert with `fDenyTSConnections = 1`).
- A redundant pair's two archives usually **overlap** in time (same tags) — good for exercising
  compare / gap-analysis / fine-grained backfill — while non-overlapping periods give large one-sided
  gaps for the bulk-backfill path. See [`sync-workflow.md`](sync-workflow.md).
