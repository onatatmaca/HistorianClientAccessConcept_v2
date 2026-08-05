using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Persists each backfill run to <c>logs/backfill-journal/{id}.json</c> so it can be
    /// reverted later — a revert deletes exactly the timestamps recorded here. Uses the
    /// built-in DataContractJsonSerializer (no NuGet). All IO failures are swallowed so
    /// journaling never crashes a backfill.
    /// </summary>
    public static class BackfillJournalService
    {
        private static readonly object Gate = new object();
        private static readonly DataContractJsonSerializer Serializer =
            new DataContractJsonSerializer(typeof(BackfillJournalEntry));

        public static string JournalDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "backfill-journal");

        /// <summary>
        /// Write (or overwrite, by Id) a journal entry to disk. Returns false if it could not
        /// be written.
        ///
        /// Journaling still must never CRASH a restore — the samples are already on the server
        /// by the time we get here, and throwing would lose the in-memory record as well. But
        /// it must never be SILENT either: the journal is the only thing that makes a restore
        /// undoable, so a run that could not be journaled is a run that can never be reverted.
        /// This used to swallow every failure and return normally, so the caller set JournalId
        /// unconditionally and the report said "N readings restored" with no hint that the undo
        /// had been lost. A read-only install folder — exactly what a single-folder deployment
        /// into Program Files produces — triggers it on every run. Callers must tell the user.
        /// </summary>
        public static bool Save(BackfillJournalEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id)) return false;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(JournalDirectory);
                    string path = Path.Combine(JournalDirectory, entry.Id + ".json");
                    using (var fs = File.Create(path))
                        Serializer.WriteObject(fs, entry);
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(
                    "Backfill journal could not be saved (" + entry.Id + "): " + ex);
                return false;
            }
        }

        /// <summary>Load all journal entries, newest run first. Corrupt files are skipped.</summary>
        public static List<BackfillJournalEntry> LoadAll()
        {
            var result = new List<BackfillJournalEntry>();
            try
            {
                if (!Directory.Exists(JournalDirectory)) return result;
                foreach (var file in Directory.GetFiles(JournalDirectory, "*.json"))
                {
                    try
                    {
                        using (var fs = File.OpenRead(file))
                            result.Add((BackfillJournalEntry)Serializer.ReadObject(fs));
                    }
                    catch { /* skip a corrupt entry, keep the rest */ }
                }
            }
            catch { }
            return result.OrderByDescending(e => e.RunLocal).ToList();
        }

        public static string NewId() =>
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }
}
