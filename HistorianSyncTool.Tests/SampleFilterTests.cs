using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class SampleFilterTests
    {
        private static readonly DateTime Anchor = new DateTime(2025, 1, 1, 12, 0, 0);

        private static (DateTime, object, double) S(int secs, object value, double q = 100.0)
            => (Anchor.AddSeconds(secs), value, q);

        // ── ToSecondTicks: the identity every diff, verify and journal is keyed on ──

        [TestMethod]
        public void ToSecondTicks_TwoInstantsAnHourApartNeverShareAKey()
        {
            // THE regression. Keyed on local ticks, the two halves of the autumn change-over
            // hour collapse onto one key, so a mirror outage inside it looks already-present in
            // every HashSet lookup and is never restored. Written against real UTC instants and
            // converted to local, so it holds in any time zone (in a zone without DST the two
            // are trivially distinct; in W. Europe these two ARE the repeated hour).
            var utcA = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc);
            var utcB = new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc);

            Assert.AreNotEqual(
                SampleFilter.ToSecondTicks(utcA.ToLocalTime()),
                SampleFilter.ToSecondTicks(utcB.ToLocalTime()),
                "two readings a real hour apart must never share a key");
        }

        [TestMethod]
        public void ToSecondTicks_IsTheSameKeyWhicheverFrameYouPassIt()
        {
            // The planner passes local times, the journal passes UTC. They must agree, or a
            // revert would delete at a different instant than the diff decided to write.
            var utc = new DateTime(2026, 3, 14, 9, 17, 42, DateTimeKind.Utc);

            Assert.AreEqual(SampleFilter.ToSecondTicks(utc),
                            SampleFilter.ToSecondTicks(utc.ToLocalTime()));
        }

        [TestMethod]
        public void ToSecondTicks_TruncatesSubSecondToTheStoredSecond()
        {
            var utc = new DateTime(2026, 3, 14, 9, 17, 42, DateTimeKind.Utc).AddMilliseconds(123);

            Assert.AreEqual(SampleFilter.ToSecondTicks(new DateTime(2026, 3, 14, 9, 17, 42, DateTimeKind.Utc)),
                            SampleFilter.ToSecondTicks(utc),
                            "12:54:30.123 has to match the 12:54:30 Historian stores");
        }

        [TestMethod]
        public void ToSecondTicks_UtcInputIsUnchanged_SoLegacyJournalsStillRevert()
        {
            // Journal files on disk hold UTC second ticks. If this ever started re-converting
            // them, every existing entry would revert at an instant 1-2 h off — i.e. delete real
            // plant data. Pin it.
            var utc = new DateTime(2026, 7, 1, 13, 45, 0, DateTimeKind.Utc);
            long expected = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond);

            Assert.AreEqual(expected, SampleFilter.ToSecondTicks(utc));
        }

        // ── Parse (no clipping) ────────────────────────────────────────────────────

        [TestMethod]
        public void Parse_NullInput_ReturnsEmpty()
        {
            var result = SampleFilter.Parse(null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Parse_EmptyInput_ReturnsEmpty()
        {
            var result = SampleFilter.Parse(Enumerable.Empty<(DateTime, object, double)>());
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Parse_DropsNullValues()
        {
            var input = new[] { S(0, 1.5f), S(1, null), S(2, 3.5f) };
            var result = SampleFilter.Parse(input);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1.5f, result[0].Value);
            Assert.AreEqual(3.5f, result[1].Value);
        }

        [TestMethod]
        public void Parse_DropsUnparseableValues()
        {
            var input = new[] { S(0, 1.5f), S(1, "nope"), S(2, 3.5f) };
            var result = SampleFilter.Parse(input);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1.5f, result[0].Value);
            Assert.AreEqual(3.5f, result[1].Value);
        }

        [TestMethod]
        public void Parse_ParsesStringFloats()
        {
            // Historian sometimes returns boxed strings for float tags.
            var input = new[] { S(0, "2.5"), S(1, "not"), S(2, "10") };
            var result = SampleFilter.Parse(input);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(2.5f, result[0].Value);
            Assert.AreEqual(10f, result[1].Value);
        }

        [TestMethod]
        public void Parse_PreservesQuality()
        {
            var input = new[] { S(0, 1.5f, 75.0), S(1, 2.5f, 100.0) };
            var result = SampleFilter.Parse(input);

            Assert.AreEqual(75.0, result[0].Quality);
            Assert.AreEqual(100.0, result[1].Quality);
        }

        // ── ParseAndClip (half-open [start, end)) ──────────────────────────────────

        [TestMethod]
        public void ParseAndClip_NullInput_ReturnsEmpty()
        {
            var result = SampleFilter.ParseAndClip(null, Anchor, Anchor.AddMinutes(10));
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ParseAndClip_AllInRange_KeepsAll()
        {
            var input = new[] { S(10, 1f), S(20, 2f), S(30, 3f) };
            var result = SampleFilter.ParseAndClip(input, Anchor, Anchor.AddSeconds(60));
            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void ParseAndClip_BeforeStart_Skipped()
        {
            var input = new[] { S(-10, 0f), S(0, 1f), S(10, 2f) };
            var result = SampleFilter.ParseAndClip(input, Anchor, Anchor.AddSeconds(60));

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1f, result[0].Value);
            Assert.AreEqual(2f, result[1].Value);
        }

        [TestMethod]
        public void ParseAndClip_StartBoundaryIncluded()
        {
            // Half-open [start, end) — time == start is in.
            var input = new[] { S(0, 1f) };
            var result = SampleFilter.ParseAndClip(input, Anchor, Anchor.AddSeconds(10));

            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void ParseAndClip_EndBoundaryExcluded()
        {
            // Half-open [start, end) — time == end is OUT.
            var input = new[] { S(10, 1f) };
            var result = SampleFilter.ParseAndClip(input, Anchor, Anchor.AddSeconds(10));

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void ParseAndClip_AfterEnd_BreaksEarly()
        {
            // Once we hit >= end we should stop scanning, so an unparseable value beyond
            // the end shouldn't be processed at all.
            int touched = 0;
            IEnumerable<(DateTime, object, double)> Source()
            {
                touched++; yield return S(0, 1f);
                touched++; yield return S(5, 2f);
                touched++; yield return S(10, 3f);  // == end, triggers break
                touched++; yield return S(15, "x"); // would throw if touched
            }

            var result = SampleFilter.ParseAndClip(Source(), Anchor, Anchor.AddSeconds(10));

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(3, touched, "Iteration must stop on the first time >= end.");
        }

        [TestMethod]
        public void ParseAndClip_DropsNullAndUnparseableWithinRange()
        {
            var input = new[] { S(0, 1.5f), S(1, null), S(2, "bad"), S(3, 3.5f) };
            var result = SampleFilter.ParseAndClip(input, Anchor, Anchor.AddSeconds(60));

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1.5f, result[0].Value);
            Assert.AreEqual(3.5f, result[1].Value);
        }

        [TestMethod]
        public void ParseAndClip_PreservesTimeAndQuality()
        {
            var input = new[] { S(5, 9.5f, 88.0) };
            var result = SampleFilter.ParseAndClip(input, Anchor, Anchor.AddSeconds(60));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Anchor.AddSeconds(5), result[0].Time);
            Assert.AreEqual(9.5f, result[0].Value);
            Assert.AreEqual(88.0, result[0].Quality);
        }
    }
}
