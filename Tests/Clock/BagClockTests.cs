using System;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class BagClockTests
    {
        private const double Tol = 2.0;

        private static double Utc()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        [Test]
        public void FirstLaunch_NoSavedState_AnchorsToUtc_GrantsNoOfflineTime()
        {
            using var clock = new BagClock();
            Assert.That(clock.Now, Is.EqualTo(Utc()).Within(Tol));
        }

        [Test]
        public void SavedInPast_WithinCap_GrantsElapsed()
        {
            var utc = Utc();
            using var clock = new BagClock(
                new BagClockState { timeline = utc - 60, lastSeenUtc = utc - 60 },
                maxOfflineSeconds: 3600);
            // offline = 60, under cap → timeline advances the full 60.
            Assert.That(clock.Now, Is.EqualTo(utc).Within(Tol));
        }

        [Test]
        public void SavedInPast_BeyondCap_GrantsOnlyCap()
        {
            var utc = Utc();
            using var clock = new BagClock(
                new BagClockState { timeline = utc - 100000, lastSeenUtc = utc - 100000 },
                maxOfflineSeconds: 3600);
            Assert.That(clock.Now, Is.EqualTo(utc - 100000 + 3600).Within(Tol));
        }

        [Test]
        public void BeyondCap_DoesNotBankRemainder()
        {
            // The bug this design exists to prevent: the uncredited remainder must be
            // discarded, not paid out on later launches.
            var utc = Utc();
            using var clock = new BagClock(
                new BagClockState { timeline = utc - 100000, lastSeenUtc = utc - 100000 },
                maxOfflineSeconds: 3600);

            var afterFirstAnchor = clock.Now;
            clock.Reanchor();

            Assert.That(clock.Now, Is.EqualTo(afterFirstAnchor).Within(Tol),
                "second anchor must grant 0 — the remainder beyond the cap is forfeited");
        }

        [Test]
        public void SavedInFuture_RolledBackClock_GrantsNothing()
        {
            var utc = Utc();
            using var clock = new BagClock(
                new BagClockState { timeline = utc, lastSeenUtc = utc + 100000 },
                maxOfflineSeconds: 3600);
            Assert.That(clock.Now, Is.EqualTo(utc).Within(Tol));
        }

        [Test]
        public void Now_NeverDecreases_AcrossManyReads()
        {
            using var clock = new BagClock();
            var previous = clock.Now;
            for (int i = 0; i < 1000; i++)
            {
                var current = clock.Now;
                Assert.GreaterOrEqual(current, previous);
                previous = current;
            }
        }

        [Test]
        public void Now_NeverDecreases_AcrossRepeatedReanchor()
        {
            using var clock = new BagClock();
            var previous = clock.Now;
            for (int i = 0; i < 100; i++)
            {
                clock.Reanchor();
                var current = clock.Now;
                Assert.GreaterOrEqual(current, previous);
                previous = current;
            }
        }

        [Test]
        public void State_ReflectsRatchetedNow()
        {
            using var clock = new BagClock();
            var now = clock.Now;
            Assert.GreaterOrEqual(clock.State.timeline, now);
            Assert.That(clock.State.lastSeenUtc, Is.EqualTo(Utc()).Within(Tol));
        }

        [Test]
        public void Ctor_NaNTimeline_RecoversAsFirstRunAndWarns()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("implausible"));

            using var clock = new BagClock(new BagClockState { timeline = double.NaN, lastSeenUtc = double.NaN });

            Assert.That(clock.Now, Is.EqualTo(Utc()).Within(Tol));
        }

        [Test]
        public void Ctor_ImplausiblyLargeTimeline_RecoversAsFirstRunAndWarns()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("implausible"));

            using var clock = new BagClock(new BagClockState { timeline = 1e18, lastSeenUtc = 1e18 });

            Assert.That(clock.Now, Is.EqualTo(Utc()).Within(Tol));
        }

        // --------------------- offline grant arithmetic ---------------------
        //
        // Tested directly because a test cannot move Time.unscaledTimeAsDouble or the wall
        // clock, and this is the arithmetic that was wrong: it granted running time twice.

        [Test]
        public void OfflineGrant_ReanchorWhileRunning_GrantsNothing()
        {
            // The regression. Ten seconds of wall clock passed and the app was running for
            // all ten, so the monotonic ratchet already credited them. Granting the wall gap
            // on top is what made a 5s-interval regen fire +2 the first time in the Editor,
            // where Application.focusChanged fires on every switch to the Game view.
            var grant = BagClock.ComputeOfflineGrant(utc: 1000, lastSeenUtc: 990,
                                                     inSession: 10, maxOffline: 3600);

            Assert.AreEqual(0, grant, 1e-9,
                "time the app spent running is already on the timeline and must not be re-granted");
        }

        [Test]
        public void OfflineGrant_TrueSuspend_GrantsTheWholeGap()
        {
            // Suspended: the monotonic source is frozen, so nothing was credited yet.
            var grant = BagClock.ComputeOfflineGrant(utc: 1000, lastSeenUtc: 880,
                                                     inSession: 0, maxOffline: 3600);

            Assert.AreEqual(120, grant, 1e-9);
        }

        [Test]
        public void OfflineGrant_PartlyRunning_GrantsOnlyTheNotRunningPart()
        {
            // 100s of wall clock, 40s of it with the app running → 60s genuinely offline.
            var grant = BagClock.ComputeOfflineGrant(utc: 1000, lastSeenUtc: 900,
                                                     inSession: 40, maxOffline: 3600);

            Assert.AreEqual(60, grant, 1e-9);
        }

        [Test]
        public void OfflineGrant_BeyondCap_ClampsToCap()
        {
            var grant = BagClock.ComputeOfflineGrant(utc: 1_000_000, lastSeenUtc: 0.5,
                                                     inSession: 0, maxOffline: 3600);

            Assert.AreEqual(3600, grant, 1e-9);
        }

        [Test]
        public void OfflineGrant_ClockMovedBack_GrantsNothing()
        {
            var grant = BagClock.ComputeOfflineGrant(utc: 900, lastSeenUtc: 1000,
                                                     inSession: 0, maxOffline: 3600);

            Assert.AreEqual(0, grant, 1e-9);
        }

        [Test]
        public void RepeatedReanchor_DoesNotInflateTheTimeline()
        {
            // End-to-end guard on the same bug: re-anchoring while running must not push the
            // timeline ahead of real UTC, however many times it happens.
            using var clock = new BagClock();
            var utcBefore = Utc();

            for (int i = 0; i < 50; i++) clock.Reanchor();

            Assert.That(clock.Now, Is.LessThanOrEqualTo(utcBefore + Tol),
                "50 re-anchors must not have granted any offline time");
        }

        [Test]
        public void Dispose_Twice_DoesNotThrow()
        {
            var clock = new BagClock();
            clock.Dispose();
            Assert.DoesNotThrow(() => clock.Dispose());
        }

        [Test]
        public void NegativeCap_TreatedAsZero()
        {
            var utc = Utc();
            using var clock = new BagClock(
                new BagClockState { timeline = utc - 5000, lastSeenUtc = utc - 5000 },
                maxOfflineSeconds: -1);
            Assert.That(clock.Now, Is.EqualTo(utc - 5000).Within(Tol));
        }
    }
}
