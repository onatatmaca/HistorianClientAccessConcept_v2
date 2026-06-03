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
