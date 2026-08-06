using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagPipelineTests
    {
        private readonly List<UnityEngine.Object> _toCleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _toCleanup.Count; i++)
            {
                if (_toCleanup[i] != null) UnityEngine.Object.DestroyImmediate(_toCleanup[i]);
            }
            _toCleanup.Clear();
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _toCleanup.Add(obj);
            return obj;
        }

        [Test]
        public void Add_CyclicSideEffect_AbortsAtDepthLimitWithError()
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var cyclic = Track(ScriptableObject.CreateInstance<CyclicSideEffectRule>());
            cyclic.Trigger = gold;
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold },
                extraRules: new ResourceRule[] { cyclic }));
            var bag = new ResourceBag("p", bp);
            

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "Side-effect depth exceeded"));

            bag.Add(gold, 1, "boot");
            Assert.IsTrue(bag.GetAmount(gold) >= 1);
            bag.Dispose();
        }

        [Test]
        public void Spend_SkipOutcome_TreatedAsSuccessNoDeduction()
        {
            var skip = Track(ScriptableObject.CreateInstance<SkipPrimaryRule>());
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold, rules: new ResourceRule[] { skip }));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);

            bag.Add(gold, 5, "seed");

            Assert.IsTrue(bag.TrySpend(gold, 5, "buy"));
            Assert.AreEqual(5, bag.GetAmount(gold));
            bag.Dispose();
        }

        private sealed class SkipPrimaryRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                public Instance(SkipPrimaryRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource == Owner) intent.SkipPrimary = true;
                }
            }
        }

        [Test]
        public void Spend_RejectOutcome_ReturnsFalseEvenWithBalance()
        {
            var reject = Track(ScriptableObject.CreateInstance<RejectRule>());
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold,
                rules: new ResourceRule[] { reject }));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);
            

            bag.Add(gold, 10, "seed");
            Assert.IsFalse(bag.TrySpend(gold, 1, "x"));
            Assert.AreEqual(10, bag.GetAmount(gold));
            bag.Dispose();
        }

        private sealed class RejectRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                public Instance(RejectRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag) { }

                public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource == Owner) intent.Outcome = SpendOutcome.Reject;
                }
            }
        }

        [Test]
        public void Spend_RejectWithSideEffect_DiscardsSideEffectAndReturnsFalse()
        {
            // Rule emits a side-effect AND rejects — the side-effect MUST NOT fire because
            // the primary transaction failed. Verifies the "reject = abort everything" semantic.
            var refund = Track(TestResourceDefinition.Create(TestResourceId.Refund));
            var rule = Track(ScriptableObject.CreateInstance<RejectWithSideEffectRule>());
            rule.RefundAsset = refund;
            rule.RefundAmount = 5;
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold,
                rules: new ResourceRule[] { rule }));

            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold, refund }));
            var bag = new ResourceBag("p", bp);
            

            bag.Add(gold, 10, "seed");
            var ok = bag.TrySpend(gold, 1, "buy");

            Assert.IsFalse(ok);
            Assert.AreEqual(10, bag.GetAmount(gold));   // primary untouched
            Assert.AreEqual(0, bag.GetAmount(refund));  // side-effect did NOT fire
            bag.Dispose();
        }

        private sealed class RejectWithSideEffectRule : ResourceRule
        {
            public ResourceDefinition RefundAsset;
            public int RefundAmount;

            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                private readonly RejectWithSideEffectRule _cfg;
                public Instance(RejectWithSideEffectRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { _cfg = cfg; }

                public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner) return;
                    sideEffects.Add(new ResourceSideEffect(_cfg.RefundAsset, _cfg.RefundAmount, "refund"));
                    intent.Outcome = SpendOutcome.Reject;
                }
            }
        }

        private sealed class SelfDetachRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => new Instance(this, owner, bag);

            private sealed class Instance : AttachedRule
            {
                public Instance(SelfDetachRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public override void OnTick(double now) => Bag.DetachRule(Source);
            }
        }

        [Test]
        public void Tick_RuleDetachesItself_DoesNotThrow()
        {
            // B3. A one-shot rule cleaning itself up is legitimate; caching Count and
            // then indexing walked off the end of the shrunken list.
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var detacher = Track(ScriptableObject.CreateInstance<SelfDetachRule>());
            var recorder = Track(ScriptableObject.CreateInstance<RecordingRule>());
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold },
                extraRules: new ResourceRule[] { detacher, recorder }));
            var bag = new ResourceBag("p", bp);

            Assert.DoesNotThrow(() => bag.Tick());
            bag.Dispose();
        }

        [Test]
        public void Add_FromChangedHandler_HitsDepthLimitInsteadOfStackOverflow()
        {
            // B7. MaxSideEffectDepth only counted rule side effects, so a re-entrant
            // public Add restarted at depth 0 and recursed until the stack died.
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);

            bag.Changed += _ => bag.Add(gold, 1, "reentrant");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "depth exceeded"));

            Assert.DoesNotThrow(() => bag.Add(gold, 1, "boot"));
            bag.Dispose();
        }

        private sealed class RejectThenOverwriteRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                public Instance(RejectThenOverwriteRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                // A misbehaving rule that stomps an existing Reject.
                public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource == Owner) intent.Outcome = SpendOutcome.Continue;
                }
            }
        }

        private sealed class DetachDuringAddRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => new Instance(this, owner, bag);

            private sealed class Instance : AttachedRule
            {
                public Instance(DetachDuringAddRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                    => Bag.DetachRule(Source);
            }
        }

        [Test]
        public void Add_RuleDetachesDuringHook_DoesNotThrow()
        {
            // The other half of B3. Task 6 shipped the pipeline-loop fix but its only B3
            // test exercised Tick(), which an earlier task had already made safe — so the
            // cached rule count in the pipeline, the loop this task rewrites, went in
            // uncovered. Detaching from OnBeforeAdd is as legitimate as from OnTick.
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var detacher = Track(ScriptableObject.CreateInstance<DetachDuringAddRule>());
            var recorder = Track(ScriptableObject.CreateInstance<RecordingRule>());
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold },
                extraRules: new ResourceRule[] { detacher, recorder }));
            var bag = new ResourceBag("p", bp);

            Assert.DoesNotThrow(() => bag.Add(gold, 1, "boot"));
            bag.Dispose();
        }

        [Test]
        public void Spend_RejectIsSticky_LaterRuleCannotOverwriteIt()
        {
            // B5. Outcome had no precedence, so last-writer-won and a Reject could be
            // silently undone. Enforced in the pipeline, not left to rule etiquette.
            var reject = Track(ScriptableObject.CreateInstance<RejectRule>());
            var overwrite = Track(ScriptableObject.CreateInstance<RejectThenOverwriteRule>());
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold,
                rules: new ResourceRule[] { reject, overwrite }));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);

            bag.Add(gold, 10, "seed");

            Assert.IsFalse(bag.TrySpend(gold, 1, "buy"));
            Assert.AreEqual(10, bag.GetAmount(gold));
            bag.Dispose();
        }
    }
}
