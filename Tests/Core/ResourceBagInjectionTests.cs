using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Proves a dependency reaches a rule through <see cref="IBagInjector"/>, and pins down
    /// what changed by moving this seam from pull to push.
    /// </summary>
    /// <remarks>
    /// The rule's own factory — <see cref="ResourceRule.Attach"/> — is where injection
    /// happens, so an attached instance holds plain readable fields instead of a locator and a
    /// "did I resolve yet" flag. The cost is that push is eager: <c>Attach</c> runs inside the
    /// <see cref="ResourceBag"/> constructor, so a service registered later never arrives
    /// unless the resolver pushes a <c>Func&lt;T&gt;</c>. Both cases are covered below.
    /// </remarks>
    [TestFixture]
    internal sealed class ResourceBagInjectionTests
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

        /// <summary>The kind of project dependency a rule needs.</summary>
        private interface IBonusPolicy
        {
            int Multiplier { get; }
        }

        private sealed class FixedBonusPolicy : IBonusPolicy
        {
            public FixedBonusPolicy(int multiplier) => Multiplier = multiplier;
            public int Multiplier { get; }
        }

        /// <summary>Takes its dependency by push, at the moment the bag builds it.</summary>
        private sealed class BonusRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            {
                if (owner == null) return null;

                var rule = new Instance(this, owner, bag);
                bag.Injector?.Inject(rule);
                return rule;
            }

            internal sealed class Instance : AttachedRule
            {
                public Instance(BonusRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public IBonusPolicy Policy { get; set; }

                public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner) return;
                    if (Policy != null) intent.Delta *= Policy.Multiplier;
                }
            }
        }

        /// <summary>Same, but the dependency arrives as a factory so it can be read later.</summary>
        private sealed class DeferredBonusRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            {
                if (owner == null) return null;

                var rule = new Instance(this, owner, bag);
                bag.Injector?.Inject(rule);
                return rule;
            }

            internal sealed class Instance : AttachedRule
            {
                public Instance(DeferredBonusRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public Func<IBonusPolicy> Policy { get; set; }

                public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner) return;
                    var policy = Policy?.Invoke();
                    if (policy != null) intent.Delta *= policy.Multiplier;
                }
            }
        }

        private (ResourceDefinition<TestResourceId> coin, BagBlueprint bp) Scope(ResourceRule rule)
        {
            var coin = Track(TestResourceDefinition.Create(TestResourceId.Coin, rules: new[] { rule }));
            var bp = Track(TestBlueprint.Create(new[] { coin }));
            return (coin, bp);
        }

        [Test]
        public void DependencyPushedAtAttach_ReachesTheRule()
        {
            var policy = new FixedBonusPolicy(3);
            var injector = new FakeBagInjector()
                .Resolve<BonusRule.Instance>(r => r.Policy = policy);
            var rule = Track(ScriptableObject.CreateInstance<BonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, injector);
            bag.Add(coin, 10, "quest");

            Assert.AreEqual(30, bag.GetAmount(coin), "the rule must have received the policy");
            bag.Dispose();
        }

        [Test]
        public void PushIsEager_ADependencyCreatedAfterConstructionNeverArrives()
        {
            // Attach runs inside the constructor, so whatever the resolver reads at that
            // moment is what the rule keeps. This is the capability lazy pull used to have.
            FixedBonusPolicy policy = null;
            var injector = new FakeBagInjector()
                .Resolve<BonusRule.Instance>(r => r.Policy = policy);
            var rule = Track(ScriptableObject.CreateInstance<BonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, injector);
            policy = new FixedBonusPolicy(2);

            bag.Add(coin, 10, "after_the_service_appeared");

            Assert.AreEqual(10, bag.GetAmount(coin),
                "the instance captured null at attach time and cannot recover");
            bag.Dispose();
        }

        [Test]
        public void PushingAFactory_SurvivesADependencyThatAppearsLater()
        {
            // The supported way to defer: the rule's field type says it is read on use.
            FixedBonusPolicy policy = null;
            var injector = new FakeBagInjector()
                .Resolve<DeferredBonusRule.Instance>(r => r.Policy = () => policy);
            var rule = Track(ScriptableObject.CreateInstance<DeferredBonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, injector);

            bag.Add(coin, 10, "before");
            Assert.AreEqual(10, bag.GetAmount(coin), "nothing to read yet, so no bonus");

            policy = new FixedBonusPolicy(2);

            bag.Add(coin, 10, "after");
            Assert.AreEqual(30, bag.GetAmount(coin), "10 + (10 * 2) — the late service was picked up");
            bag.Dispose();
        }

        [Test]
        public void UnmatchedTarget_IsANoOpRatherThanAFailure()
        {
            // The injector holds a resolver for a different rule type; ours gets nothing and
            // the bag still works. A rule that cannot run without its dependency is the one
            // that must say so, in its own Attach.
            var injector = new FakeBagInjector()
                .Resolve<DeferredBonusRule.Instance>(r => r.Policy = () => new FixedBonusPolicy(5));
            var rule = Track(ScriptableObject.CreateInstance<BonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, injector);
            bag.Add(coin, 10, "quest");

            Assert.AreEqual(10, bag.GetAmount(coin));
            bag.Dispose();
        }

        [Test]
        public void NoInjector_RuleRunsWithoutItsDependency()
        {
            // A bag built without an injector must not throw; the rule simply has no policy.
            var rule = Track(ScriptableObject.CreateInstance<BonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp);
            Assert.IsNull(bag.Injector);

            bag.Add(coin, 10, "quest");
            Assert.AreEqual(10, bag.GetAmount(coin));
            bag.Dispose();
        }
    }
}
