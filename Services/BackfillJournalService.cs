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

        /// <summary>Write (or overwrite, by Id) a journal entry to disk.</summary>
        public static void Save(BackfillJournalEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id)) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(JournalDirectory);
                    string path = Path.Combine(JournalDirectory, entry.Id + ".json");
                    using (var fs = File.Create(path))
                        Serializer.WriteObject(fs, entry);
                }
            }
            catch { /* journaling must never crash a backfill */ }
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
