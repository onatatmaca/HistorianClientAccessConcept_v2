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

## Recovering Historian archives from a crashed plant VM (2026-08)
Plant PCs (Riverbend `CYRISV1`, Driftwood `CYDRSV1`) crashed; we received their VMware VMs
(~60 GB `.zip` → 120 GB `-flat.vmdk`). Reusable way to pull the archives out WITHOUT booting the VM:
- **Extract the flat `.vmdk`** (it's the raw disk; needs ~120 GB free), then **read its NTFS with
  7-Zip** (it has VMDK + NTFS handlers). Disk is GPT; C: = partition `3.Basic data partition.ntfs`;
  archives are at `\Proficy Historian Data\Archives\`. 7-Zip's CLI only sees partitions (can't nest
  into the NTFS) → use **7-Zip File Manager (GUI, no admin)** to extract just that folder.
- **7-Zip without admin/UAC**: `msiexec /a 7z*.msi /qn TARGETDIR=<dir>` unpacks `7z.exe`/`7z.dll`/
  `7zFM.exe` (the normal installer needs elevation; the admin-extract does not).
- **Real vs empty archive** = byte-scan non-zero %: a healthy archive is ≈ 50-80% non-zero; a file
  recovered off a DAMAGED disk comes back ≈ 0.00% (dir entry + size recovered, data clusters lost →
  zero-filled, and NOT sparse). Round size (500,000,000) = pre-allocated/active; non-round = closed
  (but can STILL be empty if the disk recovery failed).
- **Validate headlessly**: `ihArchiveDefrag_x64.exe -y -v <src> <dest> [cfg.ihc]` reads + CRC-verifies
  every tag → `Verification Done (Success) (Examined N values, M tags)`. Per-tag `Failed to defrag /
  DataNode invalid (CRC)` = corrupt or a non-archive format (e.g. a SCADA store-and-forward buffer).
  It OVERWRITES the passed `.ihc` with a generated minimal one — keep a copy of the real config.
- MigrateIHA is GUI-only (both x86 AND x64 are MFC apps, no CLI). Collector `.msq` store-and-forward
  buffers are pre-allocated zeros (no data). A truncated backup `.zip` can be partly salvaged by
  scanning local headers + inflating, but a corrupt deflate stream stops early.
- **Outcome**: Riverbend archives were all zero-filled = unrecoverable (recovered off a damaged disk);
  Driftwood archives were intact (`User_005` = 133 tags / 65.9M values, CRC-verified; ~419 tags,
  ~2025 → Jul 2026). Full per-file results in agent memory `project_riverbend_recovery`.

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
