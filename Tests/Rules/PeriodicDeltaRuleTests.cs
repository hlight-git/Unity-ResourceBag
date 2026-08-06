using Hlight.ResourceBag.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class PeriodicDeltaRuleTests
    {
        private FakeBagClock _clock;
        private PeriodicDeltaRule _rule;
        private ResourceDefinition<TestResourceId> _energy;
        private BagBlueprint _bp;
        private ResourceBag _bag;

        [SetUp]
        public void SetUp()
        {
            _clock = new FakeBagClock();
            _rule = TestRuleFactory.Create<PeriodicDeltaRule>();
            TestResourceDefinition.SetPrivate(_rule, "intervalSec", 1f);
            TestResourceDefinition.SetPrivate(_rule, "amountPerInterval", 1);
            TestResourceDefinition.SetPrivate(_rule, "stateKey", "periodic");
            _energy = TestResourceDefinition.Create(TestResourceId.Energy,
                rules: new ResourceRule[] { _rule });
            _bp = TestBlueprint.CreateCapped(_energy, 5);
            _bag = new ResourceBag("p", _bp, clock: _clock);
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            Object.DestroyImmediate(_rule);
            Object.DestroyImmediate(_bp);
            Object.DestroyImmediate(_energy);
        }

        [Test]
        public void Regen_OneInterval_AddsAmount()
        {
            _clock.Advance(1);
            _bag.Tick();
            Assert.AreEqual(1, _bag.GetAmount(_energy));
        }

        [Test]
        public void Regen_BelowInterval_NoOp()
        {
            _clock.Advance(0.5);
            _bag.Tick();
            Assert.AreEqual(0, _bag.GetAmount(_energy));
        }

        [Test]
        public void Regen_MultipleIntervals_BatchedIntoOneChange()
        {
            var changes = 0;
            _bag.Changed += _ => changes++;
            _clock.Advance(3);
            _bag.Tick();
            Assert.AreEqual(3, _bag.GetAmount(_energy));
            Assert.AreEqual(1, changes, "catch-up must be one batched Add, not one per interval");
        }

        [Test]
        public void Regen_StopsAtCap()
        {
            _clock.Advance(100);
            _bag.Tick();
            Assert.AreEqual(5, _bag.GetAmount(_energy));
        }

        [Test]
        public void Regen_AtCap_DoesNotBankTime_NoInstantRefillAfterSpend()
        {
            // B4. Sat at cap for 100s, drain it, then tick with no time passing.
            _clock.Advance(100);
            _bag.Tick();
            Assert.AreEqual(5, _bag.GetAmount(_energy));

            _bag.TrySpend(_energy, 5, "play");
            _bag.Tick();

            Assert.AreEqual(0, _bag.GetAmount(_energy),
                "no time has passed since the spend, so nothing may regen");
        }

        [Test]
        public void Regen_AtCap_ThenSpend_WaitsAFullInterval()
        {
            _clock.Advance(100);
            _bag.Tick();
            _bag.TrySpend(_energy, 5, "play");
            _bag.Tick();

            _clock.Advance(0.9);
            _bag.Tick();
            Assert.AreEqual(0, _bag.GetAmount(_energy));

            _clock.Advance(0.2);
            _bag.Tick();
            Assert.AreEqual(1, _bag.GetAmount(_energy));
        }

        [Test]
        public void Decay_NegativeAmount_Spends()
        {
            TestResourceDefinition.SetPrivate(_rule, "amountPerInterval", -1);
            _bag.Dispose();
            _bag = new ResourceBag("p", _bp, clock: _clock);
            _bag.Add(_energy, 3, "seed");

            _clock.Advance(1);
            _bag.Tick();
            Assert.AreEqual(2, _bag.GetAmount(_energy));
        }

        [Test]
        public void Decay_StopsAtZero()
        {
            TestResourceDefinition.SetPrivate(_rule, "amountPerInterval", -1);
            _bag.Dispose();
            _bag = new ResourceBag("p", _bp, clock: _clock);
            _bag.Add(_energy, 2, "seed");

            _clock.Advance(50);
            _bag.Tick();
            Assert.AreEqual(0, _bag.GetAmount(_energy));
        }

        [Test]
        public void Regen_EmitsPeriodicReason()
        {
            string reason = null;
            _bag.Changed += c => reason = c.Reason;
            _clock.Advance(1);
            _bag.Tick();
            Assert.AreEqual(RuleReasons.Periodic, reason);
        }

        [Test]
        public void MultiOwner_SameRuleSO_IndependentTimers()
        {
            var second = TestResourceDefinition.Create(TestResourceId.Mana,
                rules: new ResourceRule[] { _rule });
            var bp = TestBlueprint.Create(new[] { _energy, second }, maxAmounts: new[] { 5, 10 });
            var bag = new ResourceBag("multi", bp, clock: _clock);

            _clock.Advance(1);
            bag.Tick();

            Assert.AreEqual(1, bag.GetAmount(_energy));
            Assert.AreEqual(1, bag.GetAmount(second));

            bag.Dispose();
            Object.DestroyImmediate(bp);
            Object.DestroyImmediate(second);
        }

        [Test]
        public void ZeroInterval_NoOp()
        {
            TestResourceDefinition.SetPrivate(_rule, "intervalSec", 0f);
            _bag.Dispose();
            _bag = new ResourceBag("p", _bp, clock: _clock);

            _clock.Advance(100);
            Assert.DoesNotThrow(() => _bag.Tick());
            Assert.AreEqual(0, _bag.GetAmount(_energy));
        }
    }
}
