using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hlight.ResourceBag.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagAuthoringWarningTests
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
        public void DuplicateDefReference_Warns()
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold, gold }));

            LogAssert.Expect(LogType.Warning, new Regex("Duplicate ResourceDefinition"));

            var bag = new ResourceBag("p", bp);
            bag.Dispose();
        }

        [Test]
        public void DuplicateId_AcrossDifferentDefs_Warns()
        {
            // Easy to hit: ResourceDefinition<TKey>.Id comes from Key, so two defs with the
            // same Key collide. A collision makes SaveTo overwrite and load double-write.
            var first = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var second = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            second.name = "Gold_copy";
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { first, second }));

            LogAssert.Expect(LogType.Warning, new Regex("Duplicate ResourceDefinition Id"));

            var bag = new ResourceBag("p", bp);
            bag.Dispose();
        }

        [Test]
        public void InitialAmount_SeedsAndBypassesCap()
        {
            // Documented behaviour: the preset writes straight past cap clamping. Both fields
            // now sit on the same entry, which is what makes the contradiction visible enough
            // for the blueprint's OnValidate to warn about it in the Inspector.
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.Create(new[]
            {
                new BagBlueprint<TestResourceId>.Entry { resource = gold, initialAmount = 150, maxAmount = 100 },
            }));

            var bag = new ResourceBag("p", bp);
            Assert.AreEqual(150, bag.GetAmount(gold), "the seed ignores the cap");
            Assert.AreEqual(100, bag.GetMaxAmount(gold), "but the cap is still in force for later adds");
            bag.Dispose();
        }

        [Test]
        public void AllBlueprintDefs_PresentInSaveEvenWhenZero()
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold, gem }));

            var bag = new ResourceBag("p", bp);
            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);

            Assert.AreEqual(2, snapshot.Amounts.Count,
                "pre-seeding means every in-scope def is tracked from construction");
            bag.Dispose();
        }

        [Test]
        public void Ctor_OwnedClock_SeededBeforeRulesAttach()
        {
            // The subtlest of the persistence ordering properties: a rule reads
            // Bag.Clock.Now in its own constructor to pick a default next-fire time, so
            // the clock must already hold the restored timeline by then. Task 5's suite
            // only ever injected a clock, so a regression that seeded the owned clock
            // after rule attachment would have gone unnoticed — and this task is the one
            // that restructures that very block.
            var utc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

            var rule = Track(TestRuleFactory.Create<PeriodicDeltaRule>());
            TestResourceDefinition.SetPrivate(rule, "intervalSec", 10f);
            TestResourceDefinition.SetPrivate(rule, "amountPerInterval", 1);
            TestResourceDefinition.SetPrivate(rule, "stateKey", "periodic");
            var energy = Track(TestResourceDefinition.Create(TestResourceId.Energy,
                rules: new ResourceRule[] { rule }));
            var bp = Track(TestBlueprint.CreateCapped(energy, 5));

            // Saved 100000s ago. Under the default 8h cap the restored timeline lands at
            // roughly utc - 71200 — far enough behind real UTC to make the ordering
            // observable rather than a coin flip.
            var snapshot = new BagSnapshot
            {
                Clock = new BagClockState { timeline = utc - 100000, lastSeenUtc = utc - 100000 },
            };

            var bag = new ResourceBag("p", bp, snapshot);   // no clock injected → bag owns one

            Assert.Less(bag.Clock.Now, utc - 60000,
                "the restored timeline should sit far behind real UTC");

            bag.SaveTo(snapshot);
            Assert.AreEqual(1, snapshot.RuleState.Count);
            // The rule's state class is private, so read the row through a mirror — which also
            // pins the wire shape this test depends on.
            var nextFireAt = JsonUtility.FromJson<PeriodicStateMirror>(snapshot.RuleState[0].value).nextFireAt;
            Assert.Less(nextFireAt, utc,
                "the rule's default next-fire must derive from the restored timeline, " +
                "not from a fresh UTC read taken before seeding");

            bag.Dispose();
        }

        [Test]
        public void TrySpendAll_Failure_LeavesNoTraceAndNoEvents()
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold, gem }));
            var bag = new ResourceBag("p", bp);

            bag.Add(gold, 10, "seed");
            bag.Add(gem, 10, "seed");

            // Nothing settles, so nothing is announced: no debit followed by a restore, which
            // used to make a failed purchase look like two real movements to any subscriber.
            var seen = new List<ResourceChange>();
            bag.Changed += seen.Add;

            var ok = bag.TrySpendAll(new (ResourceDefinition, int)[] { (gold, 3), (gem, 99) }, "buy");

            Assert.IsFalse(ok);
            Assert.AreEqual(10, bag.GetAmount(gold), "the affordable entry was never charged");
            Assert.AreEqual(10, bag.GetAmount(gem));
            Assert.AreEqual(0, seen.Count, "a transaction that did not happen emits no events");
            bag.Dispose();
        }

        /// <summary>Mirror of PeriodicDeltaRule's private state class — the persisted shape.</summary>
        [Serializable]
        private sealed class PeriodicStateMirror
        {
            public double nextFireAt;
        }

        private sealed class CreditOffBlueprintOnSpendRule : ResourceRule
        {
            public ResourceDefinition Target;
            public int Amount = 5;

            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                private readonly CreditOffBlueprintOnSpendRule _cfg;
                public Instance(CreditOffBlueprintOnSpendRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { _cfg = cfg; }

                public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner || _cfg.Target == null) return;
                    sideEffects.Add(new ResourceSideEffect(_cfg.Target, _cfg.Amount, "off_blueprint_credit"));
                }
            }
        }

        [Test]
        public void TrySpendAll_Rollback_ZeroesKeyCreatedBySideEffectOutsideBlueprint()
        {
            // A rule credits whatever def its serialized fields name, and nothing checks that
            // against the blueprint entries — so a side effect can create an _amounts key after the
            // snapshot was taken. If rollback only replays snapshot keys, that credit survives a
            // failed TrySpendAll and the documented atomicity is a lie.
            var energy = Track(TestResourceDefinition.Create(TestResourceId.Energy));
            var gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var offBlueprint = Track(TestResourceDefinition.Create(TestResourceId.Xp));

            var rule = Track(ScriptableObject.CreateInstance<CreditOffBlueprintOnSpendRule>());
            rule.Target = offBlueprint;   // deliberately NOT in the blueprint
            TestResourceDefinition.SetPrivate(energy, "rules", new ResourceRule[] { rule });

            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { energy, gem }));
            var bag = new ResourceBag("p", bp);
            bag.Add(energy, 10, "seed");
            bag.Add(gem, 1, "seed");

            var ok = bag.TrySpendAll(new (ResourceDefinition, int)[] { (energy, 3), (gem, 99) }, "buy");

            Assert.IsFalse(ok);
            Assert.AreEqual(10, bag.GetAmount(energy), "the spent resource must be restored");
            Assert.AreEqual(0, bag.GetAmount(offBlueprint),
                "the off-blueprint credit must be rolled back too");

            bag.Dispose();
        }
    }
}
