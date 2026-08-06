using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Proves a dependency actually arrives through <see cref="IBagServiceLocator"/>, and that
    /// resolving lazily survives registration happening after the bag was built.
    /// </summary>
    /// <remarks>
    /// This seam had no coverage at all: <c>FakeBagServiceLocator</c> existed but was wired to
    /// nothing, so "the shape compiles" was the only evidence it worked. The eager pattern the
    /// docs used to recommend — resolve in OnAttach — is the case these tests show breaking,
    /// because OnAttach runs inside the ResourceBag constructor.
    /// </remarks>
    [TestFixture]
    internal sealed class ResourceBagLocatorTests
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

        /// <summary>The kind of project dependency a rule reaches for.</summary>
        private interface IBonusPolicy
        {
            int Multiplier { get; }
        }

        private sealed class FixedBonusPolicy : IBonusPolicy
        {
            public FixedBonusPolicy(int multiplier) => Multiplier = multiplier;
            public int Multiplier { get; }
        }

        /// <summary>
        /// Resolves lazily on first use and caches on success — the pattern the interface's
        /// docs prescribe. Deliberately keeps the locator reference, which is what makes a
        /// later registration recoverable.
        /// </summary>
        private sealed class LazyBonusRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                private IBonusPolicy _policy;
                private bool _resolved;

                public Instance(LazyBonusRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                private IBonusPolicy Policy
                {
                    get
                    {
                        if (_resolved) return _policy;
                        _resolved = Bag.Locator != null && Bag.Locator.TryProvide(out _policy);
                        return _policy;
                    }
                }

                public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner) return;
                    var policy = Policy;
                    if (policy != null) intent.Delta *= policy.Multiplier;
                }
            }
        }

        /// <summary>Resolves eagerly in OnAttach — the pattern the docs used to recommend.</summary>
        private sealed class EagerBonusRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                private IBonusPolicy _policy;

                public Instance(EagerBonusRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public override void OnAttach()
                {
                    Bag.Locator?.TryProvide(out _policy);
                }

                public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource != Owner) return;
                    if (_policy != null) intent.Delta *= _policy.Multiplier;
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
        public void DependencyRegisteredBeforeConstruction_ReachesTheRule()
        {
            var locator = new FakeBagServiceLocator().Register<IBonusPolicy>(new FixedBonusPolicy(3));
            var rule = Track(ScriptableObject.CreateInstance<LazyBonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, locator);
            bag.Add(coin, 10, "quest");

            Assert.AreEqual(30, bag.GetAmount(coin), "the rule must have received the policy");
            bag.Dispose();
        }

        [Test]
        public void LazyResolve_PicksUpADependencyRegisteredAfterConstruction()
        {
            // The case the eager pattern cannot handle: the service shows up in a later
            // bootstrap phase, after the bag already exists.
            var locator = new FakeBagServiceLocator();
            var rule = Track(ScriptableObject.CreateInstance<LazyBonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, locator);

            bag.Add(coin, 10, "before_registration");
            Assert.AreEqual(10, bag.GetAmount(coin), "nothing registered yet, so no bonus");

            locator.Register<IBonusPolicy>(new FixedBonusPolicy(2));

            bag.Add(coin, 10, "after_registration");
            Assert.AreEqual(30, bag.GetAmount(coin), "10 + (10 * 2) — the late service was picked up");
            bag.Dispose();
        }

        [Test]
        public void EagerResolveInOnAttach_MissesALateDependencyForever()
        {
            // Documents why the recommendation changed. OnAttach runs inside the constructor,
            // so a dependency registered afterwards is never seen — silently, with no retry.
            var locator = new FakeBagServiceLocator();
            var rule = Track(ScriptableObject.CreateInstance<EagerBonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp, null, null, locator);
            locator.Register<IBonusPolicy>(new FixedBonusPolicy(2));

            bag.Add(coin, 10, "after_registration");

            Assert.AreEqual(10, bag.GetAmount(coin),
                "the eager rule resolved during construction and cannot recover");
            bag.Dispose();
        }

        [Test]
        public void KeyedRegistration_DisambiguatesTwoInstancesOfOneType()
        {
            var locator = new FakeBagServiceLocator()
                .Register<IBonusPolicy>(new FixedBonusPolicy(2), "weekday")
                .Register<IBonusPolicy>(new FixedBonusPolicy(5), "weekend");

            Assert.IsTrue(locator.TryProvide<IBonusPolicy>(out var weekend, "weekend"));
            Assert.AreEqual(5, weekend.Multiplier);
            Assert.IsTrue(locator.TryProvide<IBonusPolicy>(out var weekday, "weekday"));
            Assert.AreEqual(2, weekday.Multiplier);
        }

        [Test]
        public void NoLocator_RuleRunsWithoutItsDependency()
        {
            // A bag built without a locator must not throw; the rule simply has no policy.
            var rule = Track(ScriptableObject.CreateInstance<LazyBonusRule>());
            var (coin, bp) = Scope(rule);

            var bag = new ResourceBag("player", bp);
            Assert.IsNull(bag.Locator);

            bag.Add(coin, 10, "quest");
            Assert.AreEqual(10, bag.GetAmount(coin));
            bag.Dispose();
        }
    }
}
