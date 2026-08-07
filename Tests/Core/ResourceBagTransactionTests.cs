using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// A spend runs against a tentative state and is written only if the whole thing clears.
    /// These pin the two properties that follow from that: a failure leaves no trace, and a
    /// rule deciding mid-transaction sees what earlier entries already took.
    /// </summary>
    [TestFixture]
    internal sealed class ResourceBagTransactionTests
    {
        private readonly List<UnityEngine.Object> _cleanup = new List<UnityEngine.Object>();
        private T Track<T>(T o) where T : UnityEngine.Object { _cleanup.Add(o); return o; }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _cleanup.Count; i++)
                if (_cleanup[i] != null) UnityEngine.Object.DestroyImmediate(_cleanup[i]);
            _cleanup.Clear();
        }

        [Test]
        public void FailedSingleSpend_DoesNotLetItsSideEffectsLand()
        {
            // The hole the tentative layer closes: a debit that fails on a short balance used to
            // apply its rules' side effects anyway, so a purchase that never happened could
            // still pay something out. Only an outright Reject discarded them.
            var rule = Track(ScriptableObject.CreateInstance<CreditOnSpendRule>());
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin,
                rules: new ResourceRule[] { rule }));
            var scrap = Track(TestResourceDefinition.Create(TestResourceId.Scrap));
            rule.Credit = scrap;
            var bp = Track(TestBlueprint.Create(new[] { coin, scrap }));
            var bag = new ResourceBag("p", bp);

            bag.Add(coin, 5, "seed");

            var ok = bag.TrySpend(coin, 50, "buy");   // balance is 5, so this cannot clear

            Assert.IsFalse(ok);
            Assert.AreEqual(5, bag.GetAmount(coin), "nothing was charged");
            Assert.AreEqual(0, bag.GetAmount(scrap), "and the rule's side effect did not land");
            bag.Dispose();
        }

        [Test]
        public void RuleSeesWhatEarlierEntriesAlreadySpent()
        {
            // Two entries competing for one balance: the second must be judged against what the
            // first already took, not against the amount the transaction started with.
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin));
            var bp = Track(TestBlueprint.Create(new[] { coin }));
            var bag = new ResourceBag("p", bp);
            bag.Add(coin, 10, "seed");

            var ok = bag.TrySpendAll(new (ResourceDefinition, int)[]
            {
                (coin, 6),
                (coin, 6),      // only 4 left tentatively → the set cannot clear
            }, "buy");

            Assert.IsFalse(ok);
            Assert.AreEqual(10, bag.GetAmount(coin), "and the first entry was rolled into nothing");
            bag.Dispose();
        }

        [Test]
        public void SuccessfulSpendAll_AnnouncesOncePerEntry_AfterItSettles()
        {
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin));
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new[] { coin, gem }));
            var bag = new ResourceBag("p", bp);
            bag.Add(coin, 100, "seed");
            bag.Add(gem, 10, "seed");

            var seen = new List<ResourceChange>();
            // Reading inside the handler must already show the settled state.
            var amountsWhenNotified = new List<int>();
            bag.Changed += c => { seen.Add(c); amountsWhenNotified.Add(bag.GetAmount(c.Resource)); };

            var ok = bag.TrySpendAll(new (ResourceDefinition, int)[] { (coin, 30), (gem, 2) }, "craft");

            Assert.IsTrue(ok);
            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual(70, bag.GetAmount(coin));
            Assert.AreEqual(8, bag.GetAmount(gem));
            CollectionAssert.AreEqual(new[] { 70, 8 }, amountsWhenNotified,
                "handlers run after the write, so a handler never reads a pre-commit value");
            bag.Dispose();
        }

        [Test]
        public void Bind_FiresOnChangeOnly_AndUnbinds()
        {
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin));
            var bp = Track(TestBlueprint.CreateCapped(coin, 100));
            var bag = new ResourceBag("p", bp);

            var seen = new List<int>();
            var unbind = bag.Bind(coin, seen.Add);

            Assert.AreEqual(0, seen.Count, "binding pushes nothing — read GetAmount for the current value");

            bag.Add(coin, 40, "reward");
            bag.Add(coin, 200, "windfall");          // clamped at 100: a real change, then…
            bag.Add(coin, 5, "windfall");            // …already at cap: zero delta, not a change

            CollectionAssert.AreEqual(new[] { 40, 100 }, seen);

            unbind();
            bag.Add(coin, 1, "after_unbind");
            Assert.AreEqual(2, seen.Count, "unbind stops it");
            bag.Dispose();
        }

        [Test]
        public void Bind_IsSilentForAFailedTransaction()
        {
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin));
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new[] { coin, gem }));
            var bag = new ResourceBag("p", bp);
            bag.Add(coin, 100, "seed");

            var seen = new List<int>();
            var unbind = bag.Bind(coin, seen.Add);

            var ok = bag.TrySpendAll(new (ResourceDefinition, int)[] { (coin, 30), (gem, 1) }, "buy");

            Assert.IsFalse(ok);
            Assert.AreEqual(0, seen.Count,
                "no tween, no sound, no counter animation for a purchase that did not happen");
            unbind();
            bag.Dispose();
        }

        [Test]
        public void Bind_OnlyHearsItsOwnResource()
        {
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin));
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new[] { coin, gem }));
            var bag = new ResourceBag("p", bp);

            var seen = new List<int>();
            var unbind = bag.Bind(coin, seen.Add);

            bag.Add(gem, 5, "reward");
            Assert.AreEqual(0, seen.Count);

            bag.Add(coin, 5, "reward");
            CollectionAssert.AreEqual(new[] { 5 }, seen);

            unbind();
            bag.Dispose();
        }

        [Test]
        public void AThrowingRule_DoesNotLeaveTheBagStuckMidTransaction()
        {
            // Without the finally, a rule that throws would leave the transaction open for the
            // rest of the session: every later write would sit unwritten in the tentative layer
            // and every event stay buffered — a bag that looks alive and records nothing.
            var rule = Track(ScriptableObject.CreateInstance<ThrowingRule>());
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin,
                rules: new ResourceRule[] { rule }));
            var bp = Track(TestBlueprint.Create(new[] { coin }));
            var bag = new ResourceBag("p", bp);

            rule.Throw = true;
            Assert.Throws<InvalidOperationException>(() => bag.Add(coin, 5, "boom"));
            Assert.AreEqual(0, bag.GetAmount(coin), "the throwing operation wrote nothing");

            rule.Throw = false;
            var seen = new List<int>();
            var unbind = bag.Bind(coin, seen.Add);

            bag.Add(coin, 7, "after");

            Assert.AreEqual(7, bag.GetAmount(coin), "the bag still commits after a rule threw");
            CollectionAssert.AreEqual(new[] { 7 }, seen, "and still announces");
            unbind();
            bag.Dispose();
        }

        /// <summary>Test-only rule that throws from inside the pipeline.</summary>
        private sealed class ThrowingRule : ResourceRule
        {
            public bool Throw;

            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                private readonly ThrowingRule _cfg;

                public Instance(ThrowingRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { _cfg = cfg; }

                public override void OnBeforeAdd(ref ResourceIntent intent,
                                                 List<ResourceSideEffect> sideEffects)
                {
                    if (_cfg.Throw) throw new InvalidOperationException("rule blew up");
                }
            }
        }

        /// <summary>Test-only rule: every spend of the owner credits something else.</summary>
        private sealed class CreditOnSpendRule : ResourceRule
        {
            public ResourceDefinition Credit;

            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                private readonly CreditOnSpendRule _cfg;

                public Instance(CreditOnSpendRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { _cfg = cfg; }

                public override void OnBeforeSpend(ref ResourceIntent intent,
                                                   List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner || _cfg.Credit == null) return;
                    sideEffects.Add(new ResourceSideEffect(_cfg.Credit, 3, "spend_bonus"));
                }
            }
        }
    }
}
