using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class RetryHelperTests
    {
        [TestMethod]
        public void Retry_SucceedsFirstTry_ReturnsValueWithoutSleeping()
        {
            int calls = 0;
            var slept = new List<int>();

            var result = RetryHelper.Retry(
                () => { calls++; return 42; },
                maxAttempts: 3,
                sleep: ms => slept.Add(ms));

            Assert.AreEqual(42, result);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, slept.Count, "Sleep should not be called on first-try success.");
        }

        [TestMethod]
        public void Retry_FailsTwiceThenSucceeds_SleepsBetweenFailures()
        {
            int calls = 0;
            var slept = new List<int>();

            var result = RetryHelper.Retry(
                () =>
                {
                    calls++;
                    if (calls < 3) throw new InvalidOperationException("transient");
                    return "ok";
                },
                maxAttempts: 3,
                delaysMs: new[] { 100, 200, 400 },
                sleep: ms => slept.Add(ms));

            Assert.AreEqual("ok", result);
            Assert.AreEqual(3, calls);
            CollectionAssert.AreEqual(new[] { 100, 200 }, slept);
        }

        [TestMethod]
        public void Retry_AllAttemptsFail_ThrowsWithInnerException()
        {
            int calls = 0;
            var inner = new ArgumentException("boom");

            try
            {
                RetryHelper.Retry<int>(
                    () => { calls++; throw inner; },
                    maxAttempts: 2,
                    delaysMs: new[] { 1 },
                    sleep: _ => { });
                Assert.Fail("Expected InvalidOperationException.");
            }
            catch (InvalidOperationException ex)
            {
                Assert.AreSame(inner, ex.InnerException);
                Assert.AreEqual(2, calls);
            }
        }

        [TestMethod]
        public void Retry_NoSleepAfterFinalAttempt()
        {
            // Verify the final iteration doesn't call sleep — saves wall time on failure.
            var slept = new List<int>();

            try
            {
                RetryHelper.Retry<int>(
                    () => throw new Exception("x"),
                    maxAttempts: 3,
                    delaysMs: new[] { 10, 20, 40 },
                    sleep: ms => slept.Add(ms));
            }
            catch (InvalidOperationException) { /* expected */ }

            CollectionAssert.AreEqual(new[] { 10, 20 }, slept,
                "Should sleep between attempts 1→2 and 2→3, but not after the final attempt.");
        }

        [TestMethod]
        public void Retry_DelaysShorterThanAttempts_StopsSleepingAtBoundary()
        {
            var slept = new List<int>();

            try
            {
                RetryHelper.Retry<int>(
                    () => throw new Exception("x"),
                    maxAttempts: 5,
                    delaysMs: new[] { 10 },          // only 1 delay provided
                    sleep: ms => slept.Add(ms));
            }
            catch (InvalidOperationException) { /* expected */ }

            CollectionAssert.AreEqual(new[] { 10 }, slept,
                "When delays array runs out, no further sleeps should occur.");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Retry_NullAction_Throws()
        {
            RetryHelper.Retry<int>(null, maxAttempts: 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Retry_ZeroAttempts_Throws()
        {
            RetryHelper.Retry(() => 1, maxAttempts: 0);
        }

        [TestMethod]
        public void Retry_DefaultDelaysMs_HasThreeEntries()
        {
            // Documents the default backoff schedule so a silent change shows up in tests.
            CollectionAssert.AreEqual(new[] { 500, 1000, 2000 }, RetryHelper.DefaultDelaysMs);
        }
    }
}
