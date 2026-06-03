using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class SampleBucketerTests
    {
        private static readonly DateTime Anchor = new DateTime(2025, 1, 1, 12, 0, 0);

        private static (DateTime, float, double) S(int seconds)
            => (Anchor.AddSeconds(seconds), (float)seconds, 100.0);

        [TestMethod]
        public void GroupByBucket_NullInput_ReturnsEmpty()
        {
            var result = SampleBucketer.GroupByBucket(null, TimeSpan.FromMinutes(10));
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GroupByBucket_EmptyInput_ReturnsEmpty()
        {
            var result = SampleBucketer.GroupByBucket(
                new List<(DateTime, float, double)>(), TimeSpan.FromMinutes(10));
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GroupByBucket_SingleSample_OneBucketOneSample()
        {
            var input = new List<(DateTime, float, double)> { S(0) };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromMinutes(10));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].Count);
        }

        [TestMethod]
        public void GroupByBucket_AllWithinBucketSize_SingleBucket()
        {
            // Five samples spaced 1 minute apart, bucket size 10 minutes — all fit.
            var input = new List<(DateTime, float, double)>
            {
                S(0), S(60), S(120), S(180), S(240)
            };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromMinutes(10));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(5, result[0].Count);
        }

        [TestMethod]
        public void GroupByBucket_GapExceedsBucketSize_SplitsIntoTwo()
        {
            // 3 samples close, then 30-min gap, then 3 more
            var input = new List<(DateTime, float, double)>
            {
                S(0), S(60), S(120),
                S(2000), S(2060), S(2120)
            };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromMinutes(10));

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(3, result[0].Count);
            Assert.AreEqual(3, result[1].Count);
        }

        [TestMethod]
        public void GroupByBucket_ExactlyAtBucketSize_StartsNewBucket()
        {
            // bucketSize = 10s. Sample at +10s is exactly at the boundary.
            // Predicate is `< bucketSize`, so 10s does NOT fit — new bucket.
            var input = new List<(DateTime, float, double)> { S(0), S(10) };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromSeconds(10));

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].Count);
            Assert.AreEqual(1, result[1].Count);
        }

        [TestMethod]
        public void GroupByBucket_JustUnderBucketSize_FitsInSameBucket()
        {
            // bucketSize = 10s, sample at +9s. 9 < 10 → same bucket.
            var input = new List<(DateTime, float, double)> { S(0), S(9) };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromSeconds(10));

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2, result[0].Count);
        }

        [TestMethod]
        public void GroupByBucket_BucketStartResetsToNextSampleTime()
        {
            // Critical behavior: when a new bucket starts, its boundary is measured from
            // the next sample's TIME, not from the previous bucket's nominal end.
            // bucketSize = 10s. Series: 0, 100, 105, 109, 200, 209.
            // - Bucket A: {0}             (next sample 100 is 100s away → new)
            // - Bucket B: {100, 105, 109} (105/109 are within 10s of 100; 200 is 100s away → new)
            // - Bucket C: {200, 209}      (209 is 9s away from 200 → same bucket)
            var input = new List<(DateTime, float, double)>
            {
                S(0), S(100), S(105), S(109), S(200), S(209)
            };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromSeconds(10));

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1, result[0].Count);
            Assert.AreEqual(3, result[1].Count);
            Assert.AreEqual(2, result[2].Count);
        }

        [TestMethod]
        public void GroupByBucket_DenseClusters_BatchesStayReasonablySized()
        {
            // Synthetic stress: 1000 consecutive samples 1s apart, bucket 10s.
            // Each bucket should hold ~10 samples; total ~100 buckets.
            var list = new List<(DateTime, float, double)>();
            for (int i = 0; i < 1000; i++) list.Add(S(i));

            var result = SampleBucketer.GroupByBucket(list, TimeSpan.FromSeconds(10));

            Assert.AreEqual(100, result.Count);
            foreach (var b in result)
                Assert.AreEqual(10, b.Count);
        }

        [TestMethod]
        public void GroupByBucket_AllSamplesPreservedInOrder()
        {
            var input = new List<(DateTime, float, double)>
            {
                S(0), S(5), S(20), S(25), S(60), S(65)
            };
            var result = SampleBucketer.GroupByBucket(input, TimeSpan.FromSeconds(10));

            // Flatten and verify the sample sequence is identical to input.
            var flat = new List<(DateTime, float, double)>();
            foreach (var b in result) flat.AddRange(b);

            CollectionAssert.AreEqual(input, flat);
        }
    }
}
