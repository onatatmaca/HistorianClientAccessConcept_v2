using System;
using System.Collections.Generic;
using System.Globalization;

namespace HistorianSyncTool.UI
{
    public enum AppLanguage { En = 0, De = 1 }

    /// <summary>
    /// Every user-visible string in one place, English and German side by side.
    ///
    /// Deliberately NOT .resx satellite assemblies: delivery is a single .exe with no
    /// installer (see the product decisions), and a resx switch would need a per-culture
    /// folder next to it. A plain dictionary also keeps EN and DE on the same line, which
    /// is what makes the plain-language pass reviewable.
    ///
    /// The vocabulary rule for this table: no word that only makes sense if you know how
    /// the sync algorithm works. "sample" -> reading, "tag" -> measurement point,
    /// "backfill" -> restore, "gap" -> missing period, "batch" -> (never shown),
    /// "coverage" -> complete. Technical wording survives only in the activity log and
    /// other Advanced-mode surfaces.
    /// </summary>
    public static class Loc
    {
        private static AppLanguage _language = AppLanguage.En;

        /// <summary>Raised after <see cref="Language"/> changes so open forms can re-apply their texts.</summary>
        public static event EventHandler LanguageChanged;

        public static AppLanguage Language
        {
            get { return _language; }
            set
            {
                if (_language == value) return;
                _language = value;
                var h = LanguageChanged;
                if (h != null) h(null, EventArgs.Empty);
            }
        }

        public static AppLanguage Parse(string value)
        {
            return string.Equals(value, "De", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.De
                : AppLanguage.En;
        }

        /// <summary>Looks up a key. An unknown key returns the key itself — visible in
        /// testing, never an exception in front of a customer.</summary>
        public static string T(string key)
        {
            string[] pair;
            if (key != null && Map.TryGetValue(key, out pair))
            {
                string s = pair[(int)_language];
                if (!string.IsNullOrEmpty(s)) return s;
                return pair[0];
            }
            return key ?? "";
        }

        /// <summary>Localised format string, e.g. Fmt("status.readingsLoaded", 1284, "TEMP_05").</summary>
        public static string F(string key, params object[] args)
        {
            string fmt = T(key);
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch (FormatException) { return fmt; }
        }

        // ── EN / DE table ──────────────────────────────────────────────────────────
        // Index 0 = English, index 1 = German.
        private static readonly Dictionary<string, string[]> Map =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // ── Window / header ───────────────────────────────────────────────────
            { "app.title",            new[] { "Historian Data Sync",                    "Historian-Datenabgleich" } },
            { "app.advanced",         new[] { "Advanced",                                "Erweitert" } },
            { "app.advanced.tip",     new[] { "Show the technical view: data tables, activity log, filters and the detailed repair actions.",
                                              "Technische Ansicht einblenden: Datentabellen, Protokoll, Filter und die detaillierten Reparatur-Aktionen." } },
            { "app.demoBanner",       new[] { "DEMO DATA — not connected to a real Historian",
                                              "DEMODATEN — keine Verbindung zu einem echten Historian" } },
            { "app.demoBanner.tip",   new[] { "The application was started with --demo. Everything on screen is generated sample data; no server is contacted and nothing can be written.",
                                              "Die Anwendung wurde mit --demo gestartet. Alle Anzeigen sind erzeugte Beispieldaten; es wird kein Server kontaktiert und nichts geschrieben." } },

            // ── Server roles ──────────────────────────────────────────────────────
            { "role.main",            new[] { "main server",                             "Hauptserver" } },
            { "role.mirror",          new[] { "mirror",                                  "Spiegelserver" } },
            { "role.main.title",      new[] { "Main server",                             "Hauptserver" } },
            { "role.mirror.title",    new[] { "Mirror server",                           "Spiegelserver" } },

            // ── Sidebar: connection ───────────────────────────────────────────────
            { "hdr.connection",       new[] { "SERVERS",                                 "SERVER" } },
            { "lbl.primaryServer",    new[] { "Main server",                             "Hauptserver" } },
            { "lbl.secondaryServer",  new[] { "Mirror server",                           "Spiegelserver" } },
            { "btn.connect",          new[] { "Connect",                                 "Verbinden" } },
            { "conn.notConnected",    new[] { "Not connected",                           "Nicht verbunden" } },
            { "conn.connected",       new[] { "Connected",                               "Verbunden" } },
            { "conn.connecting",      new[] { "Connecting…",                             "Verbinde…" } },
            { "conn.failed",          new[] { "Connection failed",                       "Verbindung fehlgeschlagen" } },
            { "conn.hostTip",         new[] { "Computer name or IP address, port optional —\ne.g. GENTHIN, 192.168.50.186 or GENTHIN:14000",
                                              "Computername oder IP-Adresse, Port optional —\nz. B. GENTHIN, 192.168.50.186 oder GENTHIN:14000" } },

            // ── Sidebar: time range ───────────────────────────────────────────────
            { "hdr.period",           new[] { "TIME RANGE",                              "ZEITRAUM" } },
            { "lbl.startDate",        new[] { "From",                                    "Von" } },
            { "lbl.endDate",          new[] { "To",                                      "Bis" } },
            { "btn.analyze",          new[] { "Check for missing data",                  "Auf fehlende Daten prüfen" } },
            { "btn.analyze.tip",      new[] { "Compares both servers over the chosen time range and shows what each one is missing.",
                                              "Vergleicht beide Server über den gewählten Zeitraum und zeigt, was jeweils fehlt." } },

            // ── Sidebar: measurement points ───────────────────────────────────────
            { "hdr.tags",             new[] { "MEASUREMENT POINTS",                      "MESSSTELLEN" } },
            { "lbl.tagFilter",        new[] { "Show points containing…",                 "Messstellen filtern…" } },
            { "btn.browseTags",       new[] { "Load measurement points",                 "Messstellen laden" } },
            { "btn.serverStats",      new[] { "Server statistics",                       "Serverstatistik" } },
            { "lbl.primaryTag",       new[] { "Point on main server",                    "Messstelle (Hauptserver)" } },
            { "lbl.secondaryTag",     new[] { "Point on mirror server",                  "Messstelle (Spiegelserver)" } },
            { "btn.tagLink.on",       new[] { "⇄  Same point on both servers",           "⇄  Gleiche Messstelle auf beiden Servern" } },
            { "btn.tagLink.off",      new[] { "✕  Points chosen separately",             "✕  Messstellen getrennt gewählt" } },

            // ── Centre: completeness timeline ─────────────────────────────────────
            { "hdr.timeline",         new[] { "DATA COMPLETENESS",                       "DATENVOLLSTÄNDIGKEIT" } },
            { "link.zoomBack",        new[] { "⟲  back",                                 "⟲  zurück" } },
            { "timeline.empty",       new[] { "Connect to the servers and check for missing data to see the timeline.",
                                              "Mit den Servern verbinden und auf fehlende Daten prüfen, um die Zeitleiste zu sehen." } },
            { "timeline.legend.data",       new[] { "data present",                      "Daten vorhanden" } },
            { "timeline.legend.fillable",   new[] { "missing here — the other server has it",
                                                    "fehlt hier — der andere Server hat es" } },
            { "timeline.legend.unfillable", new[] { "missing on both",                   "auf beiden Servern nicht vorhanden" } },
            { "timeline.legend.restored",   new[] { "restored by this tool",             "von diesem Programm wiederhergestellt" } },
            { "timeline.legend.candidates", new[] { "would be restored",                 "würde wiederhergestellt" } },
            { "timeline.notAnalyzed",       new[] { "not checked yet",                   "noch nicht geprüft" } },
            { "timeline.notConnected",      new[] { "not connected",                     "nicht verbunden" } },
            { "timeline.noData",            new[] { "no data",                           "keine Daten" } },
            { "timeline.tip.restored",      new[] { "Restored by this tool",             "Von diesem Programm wiederhergestellt" } },
            { "timeline.tip.restoredBody",  new[] { "These readings were copied here by an earlier repair run.",
                                                    "Diese Messwerte wurden bei einem früheren Reparaturlauf hierher kopiert." } },
            { "timeline.tip.noData",        new[] { "{0}: no data",                      "{0}: keine Daten" } },
            { "timeline.tip.noDataBody",    new[] { "The other server has no data here either — there is nothing to copy.\n(plant shutdown, or the point recorded nothing)",
                                                    "Der andere Server hat hier ebenfalls keine Daten — es gibt nichts zu kopieren.\n(Anlagenstillstand oder die Messstelle hat nichts aufgezeichnet)" } },
            { "timeline.tip.missing",       new[] { "{0}: missing data",                 "{0}: fehlende Daten" } },
            { "timeline.tip.canCopy",       new[] { "The other server HAS data here — it can be restored.",
                                                    "Der andere Server HAT hier Daten — sie können wiederhergestellt werden." } },
            { "timeline.tip.unknown",       new[] { "Missing here (other server not checked).",
                                                    "Fehlt hier (anderer Server nicht geprüft)." } },
            { "timeline.tip.candidates",    new[] { "Would be restored: {0} reading(s)",  "Würde wiederhergestellt: {0} Messwert(e)" } },
            { "timeline.tip.toMirror",      new[] { "The main server has these, the mirror does not → they would be copied to the MIRROR.",
                                                    "Der Hauptserver hat diese, der Spiegelserver nicht → sie würden auf den SPIEGELSERVER kopiert." } },
            { "timeline.tip.toMain",        new[] { "The mirror has these, the main server does not → they would be copied to the MAIN SERVER.",
                                                    "Der Spiegelserver hat diese, der Hauptserver nicht → sie würden auf den HAUPTSERVER kopiert." } },
            { "timeline.tip.zoom",          new[] { "\nClick to zoom the time range to this.",
                                                    "\nKlicken, um den Zeitraum darauf zu zoomen." } },
            { "timeline.strip.independent", new[] { "the two servers record independently (times match {0}) — only real outages are restored",
                                                    "beide Server zeichnen unabhängig auf (Zeitstempel stimmen zu {0} überein) — nur echte Ausfälle werden wiederhergestellt" } },
            { "timeline.strip.differentTags", new[] { "different points selected — see the table on the right",
                                                    "unterschiedliche Messstellen gewählt — siehe Tabelle rechts" } },
            { "timeline.strip.connect",     new[] { "connect both servers to see what could be restored",
                                                    "beide Server verbinden, um zu sehen, was wiederhergestellt werden kann" } },
            { "timeline.rule",              new[] { "   ·   counts as missing after {0} of silence",
                                                    "   ·   gilt nach {0} Stille als fehlend" } },

            // ── Right panel ───────────────────────────────────────────────────────
            { "hdr.missing",          new[] { "WHAT'S MISSING",                          "FEHLENDE DATEN" } },
            { "missing.prompt",       new[] { "Connect to both servers, then click\n'Check for missing data'.",
                                              "Mit beiden Servern verbinden, dann auf\n„Auf fehlende Daten prüfen“ klicken." } },
            { "missing.inSync",       new[] { "In sync — nothing would be restored\nfor the selected point(s).",
                                              "Synchron — für die gewählte(n) Messstelle(n)\nwürde nichts wiederhergestellt." } },
            { "missing.summary",      new[] { "{0} reading(s) missing on the mirror\nand {1} on the main server.",
                                              "{0} Messwert(e) fehlen auf dem Spiegelserver\nund {1} auf dem Hauptserver." } },
            { "missing.summaryEst",   new[] { "About {0} reading(s) missing on the mirror\nand {1} on the main server (estimate).",
                                              "Etwa {0} Messwert(e) fehlen auf dem Spiegelserver\nund {1} auf dem Hauptserver (Schätzung)." } },
            { "missing.inSyncAll",    new[] { "In sync — every measurement point\nholds the same data on both servers.",
                                              "Synchron — jede Messstelle hat auf beiden\nServern dieselben Daten." } },
            { "missing.clickHint",    new[] { "Click a row to zoom the timeline to it.",
                                              "Auf eine Zeile klicken, um die Zeitleiste darauf zu zoomen." } },
            { "grid.point",           new[] { "Point",                                   "Messstelle" } },
            { "grid.missingOn",       new[] { "Missing on",                              "Fehlt auf" } },
            // "Messwerte" does not fit the narrow right-panel column — "Anzahl" does.
            { "grid.readings",        new[] { "Readings",                                "Anzahl" } },
            { "grid.period",          new[] { "Period",                                  "Zeitraum" } },
            { "grid.rowTip",          new[] { "{0} reading(s) of '{1}' would be copied from the {2} to the {3}.",
                                              "{0} Messwert(e) von „{1}“ würden vom {2} auf den {3} kopiert." } },
            { "grid.rowTipRule",      new[] { "\n(Same rule the repair uses — independently recorded values are not copied twice.)\nClick the row to zoom the timeline to this period.",
                                              "\n(Gleiche Regel wie bei der Reparatur — unabhängig aufgezeichnete Werte werden nicht doppelt kopiert.)\nAuf die Zeile klicken, um die Zeitleiste auf diesen Zeitraum zu zoomen." } },

            // ── Centre: data tables ───────────────────────────────────────────────
            { "btn.readPrimary",      new[] { "Load data — main server",                 "Daten laden — Hauptserver" } },
            { "btn.readSecondary",    new[] { "Load data — mirror",                      "Daten laden — Spiegelserver" } },
            { "grid.time",            new[] { "Time",                                    "Zeit" } },
            { "grid.value",           new[] { "Value",                                   "Wert" } },
            { "grid.quality",         new[] { "Quality",                                 "Qualität" } },
            { "grid.emptyPrimary",    new[] { "Choose a measurement point to load the main server's data",
                                              "Messstelle wählen, um die Daten des Hauptservers zu laden" } },
            { "grid.emptySecondary",  new[] { "Choose a measurement point to load the mirror's data",
                                              "Messstelle wählen, um die Daten des Spiegelservers zu laden" } },
            { "quality.good",         new[] { "OK",                                      "OK" } },
            { "quality.uncertain",    new[] { "uncertain",                                "unsicher" } },
            { "quality.bad",          new[] { "bad",                                      "schlecht" } },

            // ── Action groups ─────────────────────────────────────────────────────
            { "hdr.view",             new[] { "VIEW",                                    "ANSICHT" } },
            { "hdr.repair",           new[] { "REPAIR",                                  "REPARATUR" } },
            { "btn.export",           new[] { "Export CSV",                              "CSV exportieren" } },
            { "btn.syncScroll",       new[] { "Link scrolling",                          "Scrollen koppeln" } },
            { "btn.unsyncScroll",     new[] { "Unlink scrolling",                        "Kopplung lösen" } },
            { "btn.compare",          new[] { "Compare",                                 "Vergleichen" } },
            { "btn.rawView",          new[] { "Back to plain list",                      "Zurück zur einfachen Liste" } },
            { "btn.restore",          new[] { "Restore missing data…",                   "Fehlende Daten wiederherstellen…" } },
            { "btn.restore.tip",      new[] { "Shows exactly what would be copied, in both directions, and lets you confirm before anything is written.",
                                              "Zeigt genau, was in beide Richtungen kopiert würde, und fragt vor dem Schreiben nach." } },
            { "btn.copyToPrimary",    new[] { "← Copy to main server",                   "← Auf Hauptserver kopieren" } },
            { "btn.copyToSecondary",  new[] { "Copy to mirror →",                        "Auf Spiegelserver kopieren →" } },
            { "btn.previewBackfill",  new[] { "Preview && restore…",                     "Vorschau && wiederherstellen…" } },
            // Kept short on purpose: the action column is 148 px and the longer German
            // wording ("Reparatur-Verlauf / rückgängig…") was cut off mid-word.
            { "btn.history",          new[] { "Repair history / undo…",                  "Verlauf / rückgängig…" } },

            // ── Activity log ──────────────────────────────────────────────────────
            { "hdr.log",              new[] { "ACTIVITY LOG",                            "PROTOKOLL" } },
            { "btn.clearLog",         new[] { "Clear",                                   "Leeren" } },
            { "btn.copyLog",          new[] { "Copy log",                                "Protokoll kopieren" } },

            // ── Status bar ────────────────────────────────────────────────────────
            { "status.ready",         new[] { "Ready",                                   "Bereit" } },
            { "status.schedule.off",  new[] { "Automatic repair: off",                   "Automatik: aus" } },
            { "status.schedule.running", new[] { "Automatic repair: running…",           "Automatik: läuft…" } },
            { "status.schedule.pending", new[] { "Automatic repair: waiting",            "Automatik: wartet" } },
            { "status.schedule.next", new[] { "Next automatic run: {0}",                 "Nächster Automatiklauf: {0}" } },

            // ── Status / progress messages ────────────────────────────────────────
            { "msg.enterHost",        new[] { "Enter the main server's name or address.",
                                              "Namen oder Adresse des Hauptservers eingeben." } },
            { "msg.connecting",       new[] { "Connecting to the servers…",              "Verbinde mit den Servern…" } },
            { "msg.connectedTo",      new[] { "Connected to {0}.",                       "Verbunden mit {0}." } },
            { "msg.connectedToBoth",  new[] { "Connected to {0} and {1}.",               "Verbunden mit {0} und {1}." } },
            { "msg.connectCancelled", new[] { "Connection cancelled.",                   "Verbindung abgebrochen." } },
            { "msg.connectFailed",    new[] { "Connection failed: {0}",                  "Verbindung fehlgeschlagen: {0}" } },
            { "msg.connectFirst",     new[] { "Connect to a server first.",              "Zuerst mit einem Server verbinden." } },
            { "msg.connectBothFirst", new[] { "Connect to both servers first.",          "Zuerst mit beiden Servern verbinden." } },
            { "msg.loadingPoints",    new[] { "Loading measurement points…",             "Lade Messstellen…" } },
            { "msg.pointsLoaded",     new[] { "Measurement points loaded — main server: {0}, mirror: {1}",
                                              "Messstellen geladen — Hauptserver: {0}, Spiegelserver: {1}" } },
            { "msg.browseCancelled",  new[] { "Loading cancelled.",                      "Laden abgebrochen." } },
            { "msg.browseFailed",     new[] { "Could not load the measurement points: {0}",
                                              "Messstellen konnten nicht geladen werden: {0}" } },
            { "msg.statsLoading",     new[] { "Reading server statistics…",              "Lese Serverstatistik…" } },
            { "msg.statsLoaded",      new[] { "Server statistics are in the activity log.",
                                              "Serverstatistik steht im Protokoll." } },
            { "msg.statsFailed",      new[] { "Server statistics failed: {0}",           "Serverstatistik fehlgeschlagen: {0}" } },
            { "msg.selectPoint",      new[] { "Choose a measurement point first.",       "Zuerst eine Messstelle wählen." } },
            { "msg.notConnectedMain", new[] { "The main server is not connected.",       "Der Hauptserver ist nicht verbunden." } },
            { "msg.notConnectedMirror", new[] { "The mirror server is not connected.",   "Der Spiegelserver ist nicht verbunden." } },
            { "msg.readingMain",      new[] { "Reading data from the main server…",      "Lese Daten vom Hauptserver…" } },
            { "msg.readingMirror",    new[] { "Reading data from the mirror…",           "Lese Daten vom Spiegelserver…" } },
            { "msg.readMain",         new[] { "Main server: {0} reading(s) for '{1}'.",  "Hauptserver: {0} Messwert(e) für „{1}“." } },
            { "msg.readMirror",       new[] { "Mirror: {0} reading(s) for '{1}'.",       "Spiegelserver: {0} Messwert(e) für „{1}“." } },
            { "msg.readCancelled",    new[] { "Reading cancelled.",                      "Lesen abgebrochen." } },
            { "msg.readFailed",       new[] { "Reading failed: {0}",                     "Lesen fehlgeschlagen: {0}" } },
            { "msg.dateOrder",        new[] { "The start must be before the end.",       "Der Beginn muss vor dem Ende liegen." } },
            { "msg.rangeInvalid",     new[] { "The chosen time range is not valid.",     "Der gewählte Zeitraum ist ungültig." } },
            { "msg.comparing",        new[] { "Reading and comparing…",                  "Lese und vergleiche…" } },
            { "msg.compareSummary",   new[] { "Compared: main {0} · mirror {1} · same time {2} · only on main {3} · only on mirror {4} · different value {5}",
                                              "Verglichen: Haupt {0} · Spiegel {1} · gleiche Zeit {2} · nur Haupt {3} · nur Spiegel {4} · abweichender Wert {5}" } },
            { "msg.comparePoints",    new[] { "Choose a measurement point on both servers first.",
                                              "Zuerst auf beiden Servern eine Messstelle wählen." } },
            { "msg.compareCancelled", new[] { "Comparison cancelled.",                   "Vergleich abgebrochen." } },
            { "msg.compareFailed",    new[] { "Comparison failed: {0}",                  "Vergleich fehlgeschlagen: {0}" } },
            { "msg.plainList",        new[] { "Showing the plain list again.",           "Zeige wieder die einfache Liste." } },
            { "msg.checking",         new[] { "Checking '{0}' for missing data…",        "Prüfe „{0}“ auf fehlende Daten…" } },
            { "msg.checkingTwo",      new[] { "Checking for missing data — main '{0}', mirror '{1}'…",
                                              "Prüfe auf fehlende Daten — Haupt „{0}“, Spiegel „{1}“…" } },
            { "msg.checkDone",        new[] { "Check complete — {0} missing period(s) found.",
                                              "Prüfung abgeschlossen — {0} fehlende(r) Zeitraum(e) gefunden." } },
            { "msg.checkCancelled",   new[] { "Check cancelled.",                        "Prüfung abgebrochen." } },
            { "msg.checkFailed",      new[] { "Check failed: {0}",                       "Prüfung fehlgeschlagen: {0}" } },
            { "msg.loadingShared",    new[] { "Looking for points that exist on both servers…",
                                              "Suche Messstellen, die auf beiden Servern existieren…" } },
            { "msg.sharedCount",      new[] { "{0} measurement point(s) exist on both servers.",
                                              "{0} Messstelle(n) existieren auf beiden Servern." } },
            { "msg.noShared",         new[] { "No measurement point exists on both servers.",
                                              "Keine Messstelle existiert auf beiden Servern." } },
            { "msg.sharedFailed",     new[] { "Could not load the measurement points: {0}",
                                              "Messstellen konnten nicht geladen werden: {0}" } },
            { "msg.needBoth",         new[] { "Both servers must be connected to restore data.",
                                              "Für die Wiederherstellung müssen beide Server verbunden sein." } },
            { "msg.restoreDone",      new[] { "Restore finished — {0} reading(s) written.",
                                              "Wiederherstellung abgeschlossen — {0} Messwert(e) geschrieben." } },
            { "msg.restoreDoneAdv",   new[] { "Restore finished: {0}/{1} batches across {2} point(s), {3} reading(s) written.",
                                              "Wiederherstellung abgeschlossen: {0}/{1} Blöcke über {2} Messstelle(n), {3} Messwert(e) geschrieben." } },
            { "msg.restoreCancelled", new[] { "Restore cancelled.",                      "Wiederherstellung abgebrochen." } },
            { "msg.restoreFailed",    new[] { "Restore failed: {0}",                     "Wiederherstellung fehlgeschlagen: {0}" } },
            { "msg.liveEdge",         new[] { "The last {0} seconds are left out — the servers may still be recording there.",
                                              "Die letzten {0} Sekunden bleiben ausgespart — dort wird möglicherweise noch aufgezeichnet." } },
            { "msg.undoConnect",      new[] { "Connect to {0} before undoing that run.",
                                              "Vor dem Rückgängigmachen mit {0} verbinden." } },
            { "msg.undoDone",         new[] { "Undo complete — {0} reading(s) removed from {1}.",
                                              "Rückgängig abgeschlossen — {0} Messwert(e) von {1} entfernt." } },
            { "msg.undoErrors",       new[] { "Undo finished with {0} error(s); {1} reading(s) removed. The run stays in the list so you can try again.",
                                              "Rückgängig mit {0} Fehler(n) beendet; {1} Messwert(e) entfernt. Der Lauf bleibt in der Liste für einen erneuten Versuch." } },
            { "msg.undoCancelled",    new[] { "Undo cancelled — it may have been applied only partly.",
                                              "Rückgängig abgebrochen — möglicherweise nur teilweise angewendet." } },
            { "msg.undoFailed",       new[] { "Undo failed: {0}",                        "Rückgängig fehlgeschlagen: {0}" } },
            { "msg.undoRunning",      new[] { "Undoing {0}/{1}: {2}…",                   "Mache rückgängig {0}/{1}: {2}…" } },
            { "msg.noExport",         new[] { "Nothing to export — load some data first.",
                                              "Nichts zu exportieren — zuerst Daten laden." } },
            { "msg.exported",         new[] { "Exported {0} table(s) to CSV.",           "{0} Tabelle(n) als CSV exportiert." } },
            { "msg.exportFailed",     new[] { "Export failed: {0}",                      "Export fehlgeschlagen: {0}" } },
            { "msg.exportTitle",      new[] { "Export data to CSV",                      "Daten als CSV exportieren" } },
            { "msg.notOnOther",       new[] { "'{0}' does not exist on the other server — the points stay separate for this choice.",
                                              "„{0}“ existiert auf dem anderen Server nicht — die Messstellen bleiben für diese Auswahl getrennt." } },
            { "msg.pointOf",          new[] { "Point {0} of {1}: {2}",                   "Messstelle {0} von {1}: {2}" } },
            { "msg.refreshing",       new[] { "Refreshing after the repair…",            "Aktualisiere nach der Reparatur…" } },

            // ── Progress dialog ───────────────────────────────────────────────────
            { "prog.working",         new[] { "Working…",                                "Vorgang läuft…" } },
            { "prog.starting",        new[] { "Starting…",                               "Startet…" } },
            { "prog.elapsed",         new[] { "Elapsed {0}",                             "Dauer {0}" } },
            { "prog.cancel",          new[] { "Cancel",                                  "Abbrechen" } },
            { "prog.cancelling",      new[] { "Cancelling…",                             "Breche ab…" } },
            { "prog.stopping",        new[] { "Stopping after the current step…",        "Stoppe nach dem aktuellen Schritt…" } },
            { "prog.step",            new[] { "Step {0} of {1}",                         "Schritt {0} von {1}" } },
            { "prog.batch",           new[] { "Batch {0} / {1}",                         "Block {0} / {1}" } },
            { "prog.connecting",      new[] { "Connecting…",                             "Verbinde…" } },
            { "prog.restoring",       new[] { "Restoring data on {0}…",                  "Stelle Daten auf {0} wieder her…" } },
            { "prog.undoing",         new[] { "Undoing a repair run…",                   "Mache Reparaturlauf rückgängig…" } },

            // ── Value chart ───────────────────────────────────────────────────────
            { "hdr.values",           new[] { "MEASURED VALUES",                  "MESSWERTE" } },
            { "chart.empty",          new[] { "Open a measurement point to see its values from both servers.",
                                              "Eine Messstelle öffnen, um ihre Werte von beiden Servern zu sehen." } },

            // ── All-points overview ───────────────────────────────────────────────
            { "hdr.overview",         new[] { "ALL MEASUREMENT POINTS",           "ALLE MESSSTELLEN" } },
            { "ov.search",            new[] { "Search…",                          "Suchen…" } },
            { "ov.main",              new[] { "main",                             "Haupt" } },
            { "ov.mirror",            new[] { "mirror",                           "Spiegel" } },
            { "ov.inSync",            new[] { "in sync",                          "synchron" } },
            { "ov.toRestore",         new[] { "~{0} readings to restore",         "~{0} Messwerte fehlen" } },
            { "ov.notScanned",        new[] { "not checked yet",                  "noch nicht geprüft" } },
            { "ov.notScannedTip",     new[] { "The check stopped at its time limit before reaching this point. Use “Check the rest” or narrow the time range.",
                                              "Die Prüfung erreichte diese Messstelle vor dem Zeitlimit nicht. „Rest prüfen“ verwenden oder den Zeitraum verkleinern." } },
            { "ov.failed",            new[] { "could not be read",                "konnte nicht gelesen werden" } },
            { "ov.tipCoverage",       new[] { "Main server {0} complete · mirror {1} complete",
                                              "Hauptserver {0} vollständig · Spiegelserver {1} vollständig" } },
            { "ov.tipMissingMirror",  new[] { "About {0} reading(s) missing on the mirror.",
                                              "Etwa {0} Messwert(e) fehlen auf dem Spiegelserver." } },
            { "ov.tipMissingMain",    new[] { "About {0} reading(s) missing on the main server.",
                                              "Etwa {0} Messwert(e) fehlen auf dem Hauptserver." } },
            { "ov.tipEstimate",       new[] { "Estimate from a fast check — open the point for the exact number.",
                                              "Schätzung aus einer schnellen Prüfung — für die genaue Zahl die Messstelle öffnen." } },
            { "ov.tipOpen",           new[] { "Click to open this measurement point.",
                                              "Klicken, um diese Messstelle zu öffnen." } },
            { "ov.empty",             new[] { "Connect to the servers to see all measurement points.",
                                              "Mit den Servern verbinden, um alle Messstellen zu sehen." } },
            { "ov.scanning",          new[] { "Checking all measurement points… {0}/{1}",
                                              "Prüfe alle Messstellen… {0}/{1}" } },
            { "ov.summary",           new[] { "{0} point(s) · {1} need attention · checked in {2:F1} s",
                                              "{0} Messstelle(n) · {1} auffällig · geprüft in {2:F1} s" } },
            { "ov.summaryAllOk",      new[] { "{0} point(s) · all in sync · checked in {1:F1} s",
                                              "{0} Messstelle(n) · alle synchron · geprüft in {1:F1} s" } },
            { "ov.truncated",         new[] { "{0} of {1} point(s) checked — the time limit was reached.",
                                              "{0} von {1} Messstelle(n) geprüft — das Zeitlimit wurde erreicht." } },
            { "ov.scanRest",          new[] { "Check the rest",                   "Rest prüfen" } },
            { "ov.back",              new[] { "‹  All measurement points",        "‹  Alle Messstellen" } },
            { "ov.estimateNote",      new[] { "Estimated from a fast check — open a point for exact numbers.",
                                              "Schätzung aus einer schnellen Prüfung — für genaue Zahlen eine Messstelle öffnen." } },

            // ── Preview / restore dialogs ─────────────────────────────────────────
            { "dlg.selectPoints",     new[] { "Choose measurement points to restore",
                                              "Messstellen für die Wiederherstellung wählen" } },
            { "dlg.previewBoth",      new[] { "Preview & restore — both directions",
                                              "Vorschau & Wiederherstellung — beide Richtungen" } },
            { "dlg.direction",        new[] { "Restoring: {0} → {1}",              "Wiederherstellung: {0} → {1}" } },
            { "dlg.loadingStats",     new[] { "Checking each measurement point…",  "Prüfe jede Messstelle…" } },
            { "dlg.statsLoaded",      new[] { "Check complete.",                   "Prüfung abgeschlossen." } },
            { "dlg.comparing",        new[] { "Comparing…",                        "Vergleiche…" } },
            { "dlg.comparingN",       new[] { "Comparing… {0}/{1}",                "Vergleiche… {0}/{1}" } },
            { "dlg.comparingRange",   new[] { "Comparing both servers from {0} to {1}",
                                              "Vergleiche beide Server von {0} bis {1}" } },
            { "dlg.inSyncBoth",       new[] { "In sync — nothing to restore in either direction.",
                                              "Synchron — in beide Richtungen ist nichts wiederherzustellen." } },
            { "dlg.readyCounts",      new[] { "Ready — {0} point(s) to restore on the mirror, {1} on the main server.",
                                              "Bereit — {0} Messstelle(n) auf dem Spiegelserver, {1} auf dem Hauptserver." } },
            { "dlg.noShared",         new[] { "No measurement point exists on both servers.",
                                              "Keine Messstelle existiert auf beiden Servern." } },
            { "dlg.noConnection",     new[] { "Cannot compare — not connected.",   "Vergleich nicht möglich — nicht verbunden." } },
            { "dlg.col.source",       new[] { "On source",                         "Auf Quelle" } },
            { "dlg.col.target",       new[] { "On target",                         "Auf Ziel" } },
            { "dlg.col.willCopy",     new[] { "To restore",                        "Wiederherstellen" } },
            { "dlg.col.range",        new[] { "Period",                            "Zeitraum" } },
            { "dlg.selectAll",        new[] { "Select all",                        "Alle auswählen" } },
            { "dlg.selectNone",       new[] { "Select none",                       "Keine auswählen" } },
            { "dlg.all",              new[] { "All",                               "Alle" } },
            { "dlg.none",             new[] { "None",                              "Keine" } },
            { "dlg.cancel",           new[] { "Cancel",                            "Abbrechen" } },
            { "dlg.start",            new[] { "Restore now",                       "Jetzt wiederherstellen" } },
            { "dlg.close",            new[] { "Close",                             "Schließen" } },
            { "dlg.nothingSelected",  new[] { "Choose at least one measurement point.",
                                              "Mindestens eine Messstelle wählen." } },
            { "dlg.nothingSelectedTitle", new[] { "Nothing selected",              "Nichts ausgewählt" } },
            { "dlg.timelineHint",     new[] { "TIMELINE — choose a point above",   "ZEITLEISTE — oben eine Messstelle wählen" } },
            { "dlg.timelineFor",      new[] { "TIMELINE — {0}",                    "ZEITLEISTE — {0}" } },
            { "dlg.timelineEmpty",    new[] { "Choose a measurement point above to see where its data sits on both servers.",
                                              "Oben eine Messstelle wählen, um zu sehen, wo ihre Daten auf beiden Servern liegen." } },
            { "dlg.trackSource",      new[] { "SOURCE — {0} · {1} readings",       "QUELLE — {0} · {1} Messwerte" } },
            { "dlg.trackTarget",      new[] { "TARGET — {0} · {1} readings",       "ZIEL — {0} · {1} Messwerte" } },
            { "dlg.trackMain",        new[] { "MAIN SERVER · {0} readings",        "HAUPTSERVER · {0} Messwerte" } },
            { "dlg.trackMirror",      new[] { "MIRROR · {0} readings",             "SPIEGELSERVER · {0} Messwerte" } },
            { "dlg.wouldCopy",        new[] { "These reading(s) would be copied from the {0} to the {1}.",
                                              "Diese Messwerte würden vom {0} auf den {1} kopiert." } },
            { "dlg.sideToMirror",     new[] { "Main server → mirror",              "Hauptserver → Spiegelserver" } },
            { "dlg.sideToMain",       new[] { "Mirror → main server",              "Spiegelserver → Hauptserver" } },
            { "dlg.sideToMirrorHint", new[] { "The main server has these — restore them on the mirror",
                                              "Der Hauptserver hat diese — auf dem Spiegelserver wiederherstellen" } },
            { "dlg.sideToMainHint",   new[] { "The mirror has these — restore them on the main server",
                                              "Der Spiegelserver hat diese — auf dem Hauptserver wiederherstellen" } },

            // ── Result report ─────────────────────────────────────────────────────
            { "rep.title",            new[] { "Restore report",                    "Wiederherstellungs-Bericht" } },
            { "rep.bothDirections",   new[] { "both directions",                   "beide Richtungen" } },
            // The batch/attempt figures stay: they are real detail the team relies on when
            // checking a run. Only the jargon nouns around them are gone.
            { "rep.batchesOk",        new[] { "{0} of {1} batches succeeded",      "{0} von {1} Blöcken erfolgreich" } },
            { "rep.timing",           new[] { "Started {0}   ·   finished {1}   ·   took {2}",
                                              "Gestartet {0}   ·   beendet {1}   ·   Dauer {2}" } },
            { "rep.ran",              new[] { "Run at {0}",                        "Lauf am {0}" } },
            { "rep.totals",           new[] { "{0} reading(s) restored across {1} measurement point(s)   ·   {2} failed batch(es)   ·   {3} missing period(s) found",
                                              "{0} Messwert(e) auf {1} Messstelle(n) wiederhergestellt   ·   {2} fehlgeschlagene(r) Block/Blöcke   ·   {3} fehlende(r) Zeitraum/Zeiträume gefunden" } },
            { "rep.totalsShort",      new[] { "{0} reading(s) restored across {1} measurement point(s)",
                                              "{0} Messwert(e) auf {1} Messstelle(n) wiederhergestellt" } },
            { "rep.col.point",        new[] { "Point",                             "Messstelle" } },
            { "rep.col.readings",     new[] { "Readings restored",                 "Wiederhergestellte Messwerte" } },
            { "rep.col.errors",       new[] { "Problems",                          "Probleme" } },
            { "rep.exportCsv",        new[] { "Export CSV",                        "CSV exportieren" } },
            { "rep.exportTxt",        new[] { "Export text",                       "Text exportieren" } },

            // ── History / undo ────────────────────────────────────────────────────
            { "hist.title",           new[] { "Repair history",                    "Reparatur-Verlauf" } },
            { "hist.intro",           new[] { "Earlier repair runs. Choosing a run and undoing it removes exactly the readings that run wrote — data that was already there is never touched.",
                                              "Frühere Reparaturläufe. Beim Rückgängigmachen werden genau die Messwerte entfernt, die dieser Lauf geschrieben hat — bereits vorhandene Daten bleiben unberührt." } },
            { "hist.empty",           new[] { "No repair runs recorded yet.",      "Noch keine Reparaturläufe aufgezeichnet." } },
            { "hist.col.run",         new[] { "Run at",                            "Lauf am" } },
            { "hist.col.duration",    new[] { "Duration",                          "Dauer" } },
            { "hist.col.mode",        new[] { "Started by",                        "Gestartet durch" } },
            { "hist.col.direction",   new[] { "Direction",                         "Richtung" } },
            { "hist.col.points",      new[] { "Points",                            "Messstellen" } },
            { "hist.col.readings",    new[] { "Readings",                          "Messwerte" } },
            { "hist.col.status",      new[] { "Status",                            "Status" } },
            { "hist.enable",          new[] { "Enable undo — I understand this permanently deletes the restored readings",
                                              "Rückgängig freigeben — mir ist klar, dass die wiederhergestellten Messwerte damit endgültig gelöscht werden" } },
            { "hist.revert",          new[] { "Undo selected run…",                "Gewählten Lauf rückgängig machen…" } },
            { "hist.viewReport",      new[] { "Show report…",                      "Bericht anzeigen…" } },

            // ── Cancel-mid-run prompt ─────────────────────────────────────────────
            { "cancel.title",         new[] { "Keep the data copied so far?",            "Bereits kopierte Daten behalten?" } },
            { "cancel.body",          new[] { "The repair was cancelled.\n\n{0} reading(s) had already been copied to {1} before it stopped.\n\nKeep them?\n\nYes  –  keep (you can still undo later from the repair history)\nNo   –  undo now: remove exactly those readings again",
                                              "Die Reparatur wurde abgebrochen.\n\n{0} Messwert(e) waren vor dem Stopp bereits auf {1} kopiert.\n\nBehalten?\n\nJa    –  behalten (später über den Reparatur-Verlauf rückgängig möglich)\nNein  –  jetzt rückgängig: genau diese Messwerte wieder entfernen" } },
        };
    }
}
