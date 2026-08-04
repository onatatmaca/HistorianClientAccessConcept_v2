using HistorianSyncTool.UI;
using System;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Display names for the two servers — the ONLY place that turns the internal role
    /// labels into something a plant technician reads.
    ///
    /// The internal labels "Primary" / "Secondary" are load-bearing and MUST NOT change:
    /// they are compared in <c>MainForm.ShowTagSelectionDialog</c>, stored in every backfill
    /// journal entry (<c>SourceLabel</c> / <c>TargetLabel</c>) and persisted inside
    /// <c>Settings.ScheduleDirection</c> ("PrimaryToSecondary"). Renaming them would orphan
    /// the journals written so far, and an entry whose labels no longer match cannot be
    /// reverted — i.e. copied data could no longer be undone.
    ///
    /// So: rename on screen, never in storage.
    /// </summary>
    public static class ServerNaming
    {
        /// <summary>Internal label of the primary/main server. Persisted — do not localise.</summary>
        public const string PrimaryLabel = "Primary";

        /// <summary>Internal label of the secondary/mirror server. Persisted — do not localise.</summary>
        public const string SecondaryLabel = "Secondary";

        public static bool IsPrimary(string internalLabel)
        {
            return string.Equals(internalLabel, PrimaryLabel, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Lower-case role for use inside a sentence: "main server" / "mirror".</summary>
        public static string Role(string internalLabel)
        {
            return Loc.T(IsPrimary(internalLabel) ? "role.main" : "role.mirror");
        }

        /// <summary>Capitalised role for a label or a heading: "Main server" / "Mirror server".</summary>
        public static string RoleTitle(string internalLabel)
        {
            return Loc.T(IsPrimary(internalLabel) ? "role.main.title" : "role.mirror.title");
        }

        /// <summary>
        /// What the user sees: "GENTHIN — main server". Falls back to the role alone when the
        /// hostname is not known yet (before the first connect).
        /// </summary>
        public static string Display(string internalLabel, string host)
        {
            string role = Role(internalLabel);
            return string.IsNullOrWhiteSpace(host) ? RoleTitle(internalLabel) : host.Trim() + " — " + role;
        }

        /// <summary>
        /// Compact form for grid cells and tight labels: the hostname when known, otherwise
        /// the role. Never both — these appear in narrow columns.
        /// </summary>
        public static string Short(string internalLabel, string host)
        {
            return string.IsNullOrWhiteSpace(host) ? RoleTitle(internalLabel) : host.Trim();
        }
    }
}
