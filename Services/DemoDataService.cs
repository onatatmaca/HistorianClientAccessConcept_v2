using Proficy.Historian.ClientAccess.API;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// A fake Historian pair held in memory, used by <c>--demo</c>.
    ///
    /// Two jobs:
    ///  1. every screen can be built and screenshot-verified without a live server, and
    ///  2. the app can be shown to a customer with no Historian at all.
    ///
    /// SAFETY: not one method calls <c>base</c>. A demo session therefore cannot read from
    /// or write to a real server even if a live connection existed — the override list below
    /// covers every method on <see cref="HistorianDataService"/> that touches the API.
    ///
    /// Samples are GENERATED on demand from the tag name (deterministic — the same window
    /// always renders identically, so screenshots are comparable), never stored for the whole
    /// archive. Writes and deletes are kept as overlays on top of the generated stream, so a
    /// restore really does change what the next read returns and an undo really removes it.
    ///
    /// Times handed out are LOCAL, exactly like the real service above the API boundary.
    /// </summary>
    public class DemoDataService : HistorianDataService
    {
        /// <summary>Upper bound per read so a careless 2-year window can't exhaust x86 memory.</summary>
        private const int MaxSamplesPerRead = 250000;

        private readonly ServerConnection _mainConn;
        private readonly List<DemoTag> _tags = new List<DemoTag>();

        // Overlays: side -> tag -> second-ticks -> value. Populated by writes, cleared by deletes.
        private readonly Dictionary<bool, Dictionary<string, SortedDictionary<long, float>>> _written =
            new Dictionary<bool, Dictionary<string, SortedDictionary<long, float>>>();
        private readonly Dictionary<bool, Dictionary<string, HashSet<long>>> _deleted =
            new Dictionary<bool, Dictionary<string, HashSet<long>>>();

        /// <summary>The instant demo data stops — a couple of minutes ago, so the live-edge
        /// guard behaves the same as against real collectors.</summary>
        private readonly DateTime _dataEnd;

        /// <summary>Start of the generated archive (90 days back — long enough for the
        /// month/day axis, short enough to stay quick).</summary>
        private readonly DateTime _dataStart;

        public DemoDataService(ServerConnection mainConnection)
        {
            _mainConn = mainConnection;
            _dataEnd   = DateTime.Now.AddMinutes(-3);
            _dataStart = _dataEnd.Date.AddDays(-90);
            _written[true]  = new Dictionary<string, SortedDictionary<long, float>>(StringComparer.OrdinalIgnoreCase);
            _written[false] = new Dictionary<string, SortedDictionary<long, float>>(StringComparer.OrdinalIgnoreCase);
            _deleted[true]  = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
            _deleted[false] = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
            BuildTags();
        }

        /// <summary>Suggested time range for the demo, so the app opens on populated data.</summary>
        public DateTime SuggestedFrom => _dataEnd.Date.AddDays(-13);
        public DateTime SuggestedTo   => _dataEnd;

        // ── Tag catalogue ──────────────────────────────────────────────────────────

        private sealed class DemoTag
        {
            public string Name;
            public int CadenceSeconds;
            /// <summary>True = the two servers record on their own clocks (the real plant
            /// case) — the mirror's timestamps are offset, so an exact diff would be phantom.</summary>
            public bool Independent;
            public bool OnMirror = true;
            public double Base, Amplitude;
            /// <summary>Outages as (dayOffsetFromEnd, hours, onMirror).</summary>
            public List<(double DaysBack, double Hours, bool OnMirror)> Outages = new List<(double, double, bool)>();
        }

        private void BuildTags()
        {
            // Names follow the real Genthin convention (STAT6. prefix, .F_CV suffix) so the
            // demo looks like the plant the customer knows.
            string[] groups = { "TEMP", "DRUCK", "DURCHFLUSS", "FUELLSTAND", "LEISTUNG", "GASDRUCK" };
            string[] areas  = { "GAA", "WS", "GRS01", "SCALE" };

            int i = 0;
            foreach (var grp in groups)
            {
                for (int n = 1; n <= 13; n++)
                {
                    string area = areas[(i + n) % areas.Length];
                    var t = new DemoTag
                    {
                        Name           = string.Format("STAT6.{0}_{1:00}_{2}.F_CV", grp, n, area),
                        CadenceSeconds = new[] { 10, 30, 60, 120, 300 }[(i + n) % 5],
                        Independent    = ((i + n) % 3) != 0,   // ~2/3 behave like real redundant collectors
                        Base           = 20 + ((i * 7 + n * 13) % 60),
                        Amplitude      = 2 + ((i + n) % 9)
                    };

                    // Seeded problems, so every visual state is reachable in a screenshot:
                    if (n == 2)  t.Outages.Add((6.0,  9.5, true));    // mirror lost half a day
                    if (n == 5)  t.Outages.Add((3.0,  2.0, false));   // main server lost 2 h
                    if (n == 7) { t.Outages.Add((9.0, 26.0, true));   // both lost the same period
                                  t.Outages.Add((9.0, 26.0, false)); }
                    if (n == 11) t.Outages.Add((1.5,  0.5, true));    // short, recent
                    if (grp == "GASDRUCK" && n == 4) t.OnMirror = false; // exists only on the main server

                    _tags.Add(t);
                    i++;
                }
            }
        }

        // ── Side resolution ────────────────────────────────────────────────────────

        /// <summary>True for the main (primary) server. Demo connections are two distinct
        /// never-connected <see cref="ServerConnection"/> objects, told apart by reference.</summary>
        private bool IsMain(ServerConnection conn)
        {
            return ReferenceEquals(conn, _mainConn);
        }

        private DemoTag Find(string tagName)
        {
            return _tags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));
        }

        // ── Overrides — none of these call base ────────────────────────────────────

        public override List<Tag> BrowseTags(ServerConnection conn, string tagnameMask)
        {
            bool main = IsMain(conn);
            var names = _tags
                .Where(t => main || t.OnMirror)
                .Where(t => MaskMatches(t.Name, tagnameMask))
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            // The combos bind to Tag.Name only, and Tag has a public parameterless ctor
            // with a settable Name (verified by reflection on the 1.6.1.0 assembly).
            return names.Select(n => new Tag { Name = n }).ToList();
        }

        public override bool TagExists(ServerConnection conn, string tagName)
        {
            var t = Find(tagName);
            return t != null && (IsMain(conn) || t.OnMirror);
        }

        public override List<(DateTime Time, float Value, double Quality)> ReadRawInRange(
            ServerConnection conn, string tagName, DateTime start, DateTime end)
        {
            return Generate(IsMain(conn), tagName, Local(start), Local(end));
        }

        public override List<(DateTime Time, float Value, double Quality)> ReadRaw(
            ServerConnection conn, string tagName, DateTime from)
        {
            return Generate(IsMain(conn), tagName, Local(from), _dataEnd);
        }

        public override List<(DateTime Time, float Value, double Quality)> ReadInterpolated(
            ServerConnection conn, string tagName, DateTime from, DateTime to, int count)
        {
            var raw = Generate(IsMain(conn), tagName, Local(from), Local(to));
            if (raw.Count <= count || count <= 0) return raw;
            var step = (double)raw.Count / count;
            var result = new List<(DateTime, float, double)>(count);
            for (int i = 0; i < count; i++) result.Add(raw[(int)(i * step)]);
            return result;
        }

        public override List<string> WriteFloatSamples(
            ServerConnection conn, string tagName, List<DateTime> times, List<float> values)
        {
            var q = Enumerable.Repeat(DataQuality.Good, times.Count).ToArray();
            return WriteFloatSamplesWithQuality(conn, tagName, times.ToArray(), values.ToArray(), q);
        }

        public override List<string> WriteFloatSamplesWithQuality(
            ServerConnection conn, string tagName,
            DateTime[] times, float[] values, DataQuality[] qualities)
        {
            var messages = new List<string>();
            var tag = Find(tagName);
            if (tag == null) { messages.Add("Tag " + tagName + ": unknown tag (demo)"); return messages; }

            bool main = IsMain(conn);
            var store = Store(_written, main, tagName);
            var gone  = DeletedSet(main, tagName);
            for (int i = 0; i < times.Length; i++)
            {
                long key = Key(times[i]);
                store[key] = values[i];
                gone.Remove(key);
            }
            return messages;
        }

        public override (int Expected, int Actual) VerifyWrite(
            ServerConnection conn, string tagName, DateTime start, DateTime end, int expectedCount)
        {
            return (expectedCount, ReadRawInRange(conn, tagName, start, end).Count);
        }

        public override List<string> DeleteSamples(
            ServerConnection conn, string tagName, IList<DateTime> times)
        {
            var messages = new List<string>();
            if (times == null || times.Count == 0) return messages;

            bool main = IsMain(conn);
            var store = Store(_written, main, tagName);
            var gone  = DeletedSet(main, tagName);
            foreach (var t in times)
            {
                long key = Key(t);
                store.Remove(key);
                gone.Add(key);          // also hides a generated sample at that second
            }
            return messages;
        }

        // ── Generation ─────────────────────────────────────────────────────────────

        private List<(DateTime Time, float Value, double Quality)> Generate(
            bool main, string tagName, DateTime from, DateTime to)
        {
            var result = new List<(DateTime, float, double)>();
            var tag = Find(tagName);
            if (tag == null) return result;
            if (!main && !tag.OnMirror) return result;      // point does not exist on the mirror

            if (from < _dataStart) from = _dataStart;
            if (to   > _dataEnd)   to   = _dataEnd;
            if (to <= from) return result;

            // Independent collectors: the mirror records on its own clock, a few seconds off —
            // the situation that made an exact-timestamp diff report thousands of phantom
            // "missing" readings on the real plant pair.
            int offset = (!main && tag.Independent) ? 3 + (Hash(tagName) % 40) : 0;

            var outages = tag.Outages
                .Where(o => o.OnMirror != main)
                .Select(o => (Start: _dataEnd.AddDays(-o.DaysBack), End: _dataEnd.AddDays(-o.DaysBack).AddHours(o.Hours)))
                .ToList();

            var gone  = DeletedSet(main, tagName);
            long stepTicks = TimeSpan.TicksPerSecond * tag.CadenceSeconds;

            // Align to the cadence grid so both sides sit on comparable timestamps.
            long firstTick = ((from.Ticks + stepTicks - 1) / stepTicks) * stepTicks;
            for (long ticks = firstTick; ticks <= to.Ticks; ticks += stepTicks)
            {
                var t = new DateTime(ticks, DateTimeKind.Local).AddSeconds(offset);
                if (t < from || t > to) continue;
                if (outages.Any(o => t >= o.Start && t < o.End)) continue;
                long key = Key(t);
                if (gone.Contains(key)) continue;

                result.Add((t, Value(tag, t), 100.0));
                if (result.Count >= MaxSamplesPerRead) break;
            }

            // Overlay whatever a restore wrote here.
            SortedDictionary<long, float> written;
            if (_written[main].TryGetValue(tagName, out written) && written.Count > 0)
            {
                var have = new HashSet<long>(result.Select(r => Key(r.Item1)));
                foreach (var kv in written)
                {
                    if (have.Contains(kv.Key)) continue;
                    var t = new DateTime(kv.Key, DateTimeKind.Local);
                    if (t < from || t > to) continue;
                    result.Add((t, kv.Value, 100.0));
                }
                result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            }

            return result;
        }

        private static float Value(DemoTag tag, DateTime t)
        {
            double minutes = t.TimeOfDay.TotalMinutes + t.DayOfYear * 1440.0;
            double slow = Math.Sin(minutes / 720.0 * Math.PI);        // ~daily swing
            double fast = Math.Sin(minutes / 37.0 * Math.PI) * 0.25;  // process noise
            return (float)Math.Round(tag.Base + tag.Amplitude * (slow + fast), 2);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Overlay key. Revert hands back journal timestamps tagged <c>Kind=Utc</c> while
        /// writes arrive as local — normalise both to local whole seconds so a write and its
        /// undo address the same slot.
        /// </summary>
        private static long Key(DateTime t)
        {
            return SampleFilter.ToSecondTicks(Local(t));
        }

        private static DateTime Local(DateTime t)
        {
            return t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t;
        }

        /// <summary>Stable hash of a tag name — deterministic across runs (string.GetHashCode
        /// is not), so the same window always renders the same picture in a screenshot.</summary>
        private static int Hash(string s)
        {
            int h = 17;
            foreach (char c in s ?? "") h = unchecked(h * 31 + c);
            return Math.Abs(h);
        }

        private static SortedDictionary<long, float> Store(
            Dictionary<bool, Dictionary<string, SortedDictionary<long, float>>> map, bool main, string tag)
        {
            SortedDictionary<long, float> d;
            if (!map[main].TryGetValue(tag, out d)) { d = new SortedDictionary<long, float>(); map[main][tag] = d; }
            return d;
        }

        private HashSet<long> DeletedSet(bool main, string tag)
        {
            HashSet<long> d;
            if (!_deleted[main].TryGetValue(tag, out d)) { d = new HashSet<long>(); _deleted[main][tag] = d; }
            return d;
        }

        /// <summary>Historian-style mask match: '*' any run, '?' one character.</summary>
        private static bool MaskMatches(string name, string mask)
        {
            if (string.IsNullOrWhiteSpace(mask) || mask == "*") return true;
            string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(mask.Trim())
                                        .Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
