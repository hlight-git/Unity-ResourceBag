using System.Collections.Generic;
using System.Globalization;
using Hlight.ResourceBag.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagPersistenceTests
    {
        private readonly List<Object> _cleanup = new List<Object>();
        private T Track<T>(T o) where T : Object { _cleanup.Add(o); return o; }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _cleanup.Count; i++)
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
            _cleanup.Clear();
        }

        private (ResourceDefinition<TestResourceId> gold, BagBlueprint bp) MakeBag(int cap = 100)
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.CreateCapped(gold, cap));
            return (gold, bp);
        }

        [Test]
        public void SaveTo_WritesAmountsKeyedById()
        {
            var (gold, bp) = MakeBag();
            var bag = new ResourceBag("p", bp);
            bag.Add(gold, 25, "seed");

            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);

            Assert.AreEqual(1, snapshot.Amounts.Count);
            Assert.AreEqual("Gold", snapshot.Amounts[0].id);
            Assert.AreEqual(25, snapshot.Amounts[0].amount);
            Assert.AreEqual(BagSnapshot.CurrentVersion, snapshot.Version);
            bag.Dispose();
        }

        [Test]
        public void SaveTo_Null_Throws()
        {
            var (_, bp) = MakeBag();
            var bag = new ResourceBag("p", bp);
            Assert.Throws<System.ArgumentNullException>(() => bag.SaveTo(null));
            bag.Dispose();
        }

        [Test]
        public void SaveTo_ClearsBufferBeforeFilling()
        {
            var (gold, bp) = MakeBag();
            var bag = new ResourceBag("p", bp);
            bag.Add(gold, 5, "seed");

            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);
            bag.SaveTo(snapshot);

            Assert.AreEqual(1, snapshot.Amounts.Count, "reused buffer must not accumulate");
            bag.Dispose();
        }

        [Test]
        public void Ctor_RestoresAmounts()
        {
            var (gold, bp) = MakeBag();
            var first = new ResourceBag("p", bp);
            first.Add(gold, 42, "seed");
            var snapshot = new BagSnapshot();
            first.SaveTo(snapshot);
            first.Dispose();

            var second = new ResourceBag("p", bp, snapshot);
            Assert.AreEqual(42, second.GetAmount(gold));
            second.Dispose();
        }

        [Test]
        public void Ctor_DoesNotFireChanged()
        {
            var (gold, bp) = MakeBag();
            var snapshot = new BagSnapshot();
            snapshot.Amounts.Add(new BagSnapshot.AmountEntry { id = "Gold", amount = 10 });

            // Constructing is the only chance to load, so no subscriber can exist yet;
            // the bag must simply come up in the right state.
            var bag = new ResourceBag("p", bp, snapshot);
            var fired = false;
            bag.Changed += _ => fired = true;

            Assert.AreEqual(10, bag.GetAmount(gold));
            Assert.IsFalse(fired);
            bag.Dispose();
        }

        [Test]
        public void Ctor_NegativeAmount_ClampedToZero()
        {
            var (gold, bp) = MakeBag();
            var snapshot = new BagSnapshot();
            snapshot.Amounts.Add(new BagSnapshot.AmountEntry { id = "Gold", amount = -500 });

            var bag = new ResourceBag("p", bp, snapshot);
            Assert.AreEqual(0, bag.GetAmount(gold));
            bag.Dispose();
        }

        [Test]
        public void Ctor_AmountOverCap_ClampedToCap()
        {
            var (gold, bp) = MakeBag(cap: 100);
            var snapshot = new BagSnapshot();
            snapshot.Amounts.Add(new BagSnapshot.AmountEntry { id = "Gold", amount = 999999 });

            var bag = new ResourceBag("p", bp, snapshot);
            Assert.AreEqual(100, bag.GetAmount(gold));
            bag.Dispose();
        }

        [Test]
        public void Ctor_UnlimitedCap_NotClamped()
        {
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new[] { gem }));   // uncapped
            var snapshot = new BagSnapshot();
            snapshot.Amounts.Add(new BagSnapshot.AmountEntry { id = "Gem", amount = 5_000_000 });

            var bag = new ResourceBag("p", bp, snapshot);
            Assert.AreEqual(5_000_000, bag.GetAmount(gem));
            bag.Dispose();
        }

        [Test]
        public void Ctor_UnknownId_Ignored()
        {
            var (gold, bp) = MakeBag();
            var snapshot = new BagSnapshot();
            snapshot.Amounts.Add(new BagSnapshot.AmountEntry { id = "not_in_blueprint", amount = 7 });

            var bag = new ResourceBag("p", bp, snapshot);
            Assert.AreEqual(0, bag.GetAmount(gold));
            bag.Dispose();
        }

        [Test]
        public void Ctor_NullSnapshot_IsFreshBag()
        {
            var (gold, bp) = MakeBag();
            var bag = new ResourceBag("p", bp, null);
            Assert.AreEqual(0, bag.GetAmount(gold));
            bag.Dispose();
        }

        [Test]
        public void Ctor_NewerVersion_WarnsAndStillLoads()
        {
            var (gold, bp) = MakeBag();
            var snapshot = new BagSnapshot { Version = BagSnapshot.CurrentVersion + 1 };
            snapshot.Amounts.Add(new BagSnapshot.AmountEntry { id = "Gold", amount = 3 });

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("newer than supported"));

            var bag = new ResourceBag("p", bp, snapshot);
            Assert.AreEqual(3, bag.GetAmount(gold));
            bag.Dispose();
        }

        [Test]
        public void RuleState_RoundTrips()
        {
            var clock = new FakeBagClock();
            var rule = Track(TestRuleFactory.Create<PeriodicDeltaRule>());
            TestResourceDefinition.SetPrivate(rule, "intervalSec", 10f);
            TestResourceDefinition.SetPrivate(rule, "amountPerInterval", 1);
            TestResourceDefinition.SetPrivate(rule, "stateKey", "periodic");
            var energy = Track(TestResourceDefinition.Create(TestResourceId.Energy,
                rules: new ResourceRule[] { rule }));
            var bp = Track(TestBlueprint.CreateCapped(energy, 5));

            var first = new ResourceBag("p", bp, null, clock);
            clock.Advance(7);                       // 3s still to go on a 10s interval
            var snapshot = new BagSnapshot();
            first.SaveTo(snapshot);
            first.Dispose();

            Assert.AreEqual(1, snapshot.RuleState.Count);
            Assert.AreEqual("Energy:periodic", snapshot.RuleState[0].key);

            var second = new ResourceBag("p", bp, snapshot, clock);
            clock.Advance(2);                       // 9s total — not yet due
            second.Tick();
            Assert.AreEqual(0, second.GetAmount(energy));

            clock.Advance(2);                       // 11s total — due
            second.Tick();
            Assert.AreEqual(1, second.GetAmount(energy));
            second.Dispose();
        }

        [Test]
        public void RuleState_ClearedStateKeyFallsBackToDefault()
        {
            // An empty stateKey would make the bag treat the rule as stateless and drop the
            // countdown on every launch, silently. The rule falls back to its own default.
            var rule = Track(TestRuleFactory.Create<PeriodicDeltaRule>());
            TestResourceDefinition.SetPrivate(rule, "intervalSec", 10f);
            TestResourceDefinition.SetPrivate(rule, "stateKey", "");
            var energy = Track(TestResourceDefinition.Create(TestResourceId.Energy,
                rules: new ResourceRule[] { rule }));
            var bp = Track(TestBlueprint.CreateCapped(energy, 5));

            var snapshot = new BagSnapshot();
            var bag = new ResourceBag("p", bp, null, new FakeBagClock());
            bag.SaveTo(snapshot);

            Assert.AreEqual(1, snapshot.RuleState.Count);
            Assert.AreEqual("Energy:periodic", snapshot.RuleState[0].key);
            bag.Dispose();
        }

        [Test]
        public void RuleState_StatelessRuleNotWritten()
        {
            var (gold, bp) = MakeBag();
            var recording = Track(ScriptableObject.CreateInstance<RecordingRule>());
            var bag = new ResourceBag("p", bp);
            bag.AttachRule(recording);

            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);

            Assert.AreEqual(0, snapshot.RuleState.Count);
            bag.Dispose();
        }

        [Test]
        public void RuleState_UnknownKeyIgnored()
        {
            var (gold, bp) = MakeBag();
            var snapshot = new BagSnapshot();
            snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry { key = "nope:nope", value = "1" });

            Assert.DoesNotThrow(() =>
            {
                var bag = new ResourceBag("p", bp, snapshot);
                bag.Dispose();
            });
        }

        [Test]
        public void RuleState_ImplausibleValue_ResetsToFirstRunAndWarns()
        {
            // The §3.5 drift guard. A corrupt save or an IBagClock swapped for one on a
            // different epoch restores a next-fire time millions of seconds away. The
            // clamp inside OnTick stops that from crashing, but the resulting behaviour
            // would still be wrong, so LoadState treats an implausible value as no
            // state at all. Added after Task 4's review found the guard shipped untested.
            var clock = new FakeBagClock();
            var rule = Track(TestRuleFactory.Create<PeriodicDeltaRule>());
            TestResourceDefinition.SetPrivate(rule, "intervalSec", 10f);
            TestResourceDefinition.SetPrivate(rule, "amountPerInterval", 1);
            TestResourceDefinition.SetPrivate(rule, "stateKey", "periodic");
            var energy = Track(TestResourceDefinition.Create(TestResourceId.Energy,
                rules: new ResourceRule[] { rule }));
            var bp = Track(TestBlueprint.CreateCapped(energy, 5));

            var snapshot = new BagSnapshot();
            snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry
            {
                key = "Energy:periodic",
                // 100 days out, past the 30-day guard
                value = "{\"nextFireAt\":" + (clock.Now + 100.0 * 24 * 3600)
                    .ToString("R", CultureInfo.InvariantCulture) + "}",
            });

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("implausible"));

            var bag = new ResourceBag("p", bp, snapshot, clock);

            // Treated as a first run: due one interval from now, not in 100 days.
            clock.Advance(11);
            bag.Tick();
            Assert.AreEqual(1, bag.GetAmount(energy));
            bag.Dispose();
        }

        [TestCase("{not json", true)]
        [TestCase("", false)]
        public void RuleState_CorruptValue_ResetsToFirstRun(string stored, bool expectWarning)
        {
            // A hand-edited or truncated save must not reach the rule's arithmetic. The
            // ancestor of this test was about NaN specifically, which used to slip past the
            // drift guard (`NaN <= 0` is false and so is `Math.Abs(NaN - now) > drift`) and
            // then became `missed = (int)NaN` downstream; deserialization now refuses
            // anything it cannot turn into a state object at all.
            var clock = new FakeBagClock();
            var rule = Track(TestRuleFactory.Create<PeriodicDeltaRule>());
            TestResourceDefinition.SetPrivate(rule, "intervalSec", 10f);
            TestResourceDefinition.SetPrivate(rule, "amountPerInterval", 1);
            TestResourceDefinition.SetPrivate(rule, "stateKey", "periodic");
            var energy = Track(TestResourceDefinition.Create(TestResourceId.Energy,
                rules: new ResourceRule[] { rule }));
            var bp = Track(TestBlueprint.CreateCapped(energy, 5));

            var snapshot = new BagSnapshot();
            snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry
            {
                key = "Energy:periodic",
                value = stored,
            });

            // An empty value is "nothing was persisted" and passes quietly; a malformed one
            // is a corrupt save and must not look like a fresh install.
            if (expectWarning)
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("not valid PeriodicState data"));
            }

            var bag = new ResourceBag("p", bp, snapshot, clock);

            // Treated as a first run rather than propagating the broken value.
            clock.Advance(11);
            bag.Tick();
            Assert.AreEqual(1, bag.GetAmount(energy));
            bag.Dispose();
        }

        [Test]
        public void OwnedClock_StateIsSaved()
        {
            var (gold, bp) = MakeBag();
            var bag = new ResourceBag("p", bp);          // no clock injected → bag owns one

            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);

            Assert.Greater(snapshot.Clock.timeline, 0);
            Assert.Greater(snapshot.Clock.lastSeenUtc, 0);
            bag.Dispose();
        }

        [Test]
        public void InjectedClock_StateIsNotTouched()
        {
            var (gold, bp) = MakeBag();
            var snapshot = new BagSnapshot
            {
                Clock = new BagClockState { timeline = 12345, lastSeenUtc = 67890 },
            };

            // An outside package owns an injected clock's persistence; the bag must not
            // fight it for the same field.
            var bag = new ResourceBag("p", bp, snapshot, new FakeBagClock());
            bag.SaveTo(snapshot);

            Assert.AreEqual(12345, snapshot.Clock.timeline);
            Assert.AreEqual(67890, snapshot.Clock.lastSeenUtc);
            bag.Dispose();
        }
    }
}
