using System.Collections.Generic;
using Hlight.ResourceBag.Rules;
using NUnit.Framework;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class SubstituteRuleTests
    {
        private ResourceDefinition<TestResourceId> _coin;
        private ResourceDefinition<TestResourceId> _gem;
        private SubstituteRule _rule;
        private BagBlueprint _bp;
        private ResourceBag _bag;

        [SetUp]
        public void SetUp()
        {
            _gem = TestResourceDefinition.Create(TestResourceId.Gem);
            _rule = TestRuleFactory.Create<SubstituteRule>();
            TestResourceDefinition.SetPrivate(_rule, "substitute", _gem);
            TestResourceDefinition.SetPrivate(_rule, "substituteRatio", 0.1f);
            TestResourceDefinition.SetPrivate(_rule, "rounding", RoundingMode.Ceil);
            _coin = TestResourceDefinition.Create(TestResourceId.Coin,
                rules: new ResourceRule[] { _rule });
            _bp = TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { _coin, _gem });
            _bag = new ResourceBag("p", _bp);
            
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            UnityEngine.Object.DestroyImmediate(_rule);
            UnityEngine.Object.DestroyImmediate(_bp);
            UnityEngine.Object.DestroyImmediate(_coin);
            UnityEngine.Object.DestroyImmediate(_gem);
        }

        [Test]
        public void Spend_NoCoinsButHasGems_SubstituteDeducts()
        {
            _bag.Add(_gem, 5, "seed");
            // Spend 10 coin → 10 * 0.1 ceil = 1 gem.
            Assert.IsTrue(_bag.TrySpend(_coin, 10, "buy"));
            Assert.AreEqual(0, _bag.GetAmount(_coin));
            Assert.AreEqual(4, _bag.GetAmount(_gem));
        }

        [Test]
        public void Spend_InsufficientSubstitute_FallsThroughToPrimary()
        {
            // No coin, no gem → primary path also fails.
            Assert.IsFalse(_bag.TrySpend(_coin, 10, "buy"));
        }

        [Test]
        public void Spend_RoundingCeil_FractionRoundsUp()
        {
            _bag.Add(_gem, 5, "seed");
            // Spend 3 coin → 3 * 0.1 = 0.3 → ceil = 1.
            Assert.IsTrue(_bag.TrySpend(_coin, 3, "buy"));
            Assert.AreEqual(4, _bag.GetAmount(_gem));
        }

        [Test]
        public void Spend_OwnerCanAfford_SubstituteNotUsed()
        {
            // B1. Substitution is a fallback. It used to fire whenever the substitute was
            // affordable, draining premium currency while soft currency sat unspent.
            _bag.Add(_coin, 1000, "seed");
            _bag.Add(_gem, 10, "seed");

            Assert.IsTrue(_bag.TrySpend(_coin, 5, "buy"));
            Assert.AreEqual(995, _bag.GetAmount(_coin));
            Assert.AreEqual(10, _bag.GetAmount(_gem));
        }

        [Test]
        public void Spend_OwnerExactlyCoversCost_SubstituteNotUsed()
        {
            _bag.Add(_coin, 10, "seed");
            _bag.Add(_gem, 10, "seed");

            // Exactly enough coin. "Cannot cover" means strictly short, so the coin pays.
            Assert.IsTrue(_bag.TrySpend(_coin, 10, "buy"));
            Assert.AreEqual(0, _bag.GetAmount(_coin));
            Assert.AreEqual(10, _bag.GetAmount(_gem), "the substitute must stay untouched");
        }

        [Test]
        public void Spend_OwnerPartiallyShort_SubstituteUsed()
        {
            _bag.Add(_coin, 2, "seed");
            _bag.Add(_gem, 10, "seed");

            Assert.IsTrue(_bag.TrySpend(_coin, 10, "buy"));
            Assert.AreEqual(2, _bag.GetAmount(_coin), "primary untouched when substituting");
            Assert.AreEqual(9, _bag.GetAmount(_gem));
        }

        [Test]
        public void Spend_AlreadyRejected_SubstituteStandsDown()
        {
            var reject = TestRuleFactory.Create<RejectFirstRule>();
            var life = TestResourceDefinition.Create(TestResourceId.Life,
                rules: new ResourceRule[] { reject, _rule });
            var bp = TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { life, _gem });
            var bag = new ResourceBag("p", bp);
            bag.Add(_gem, 10, "seed");

            Assert.IsFalse(bag.TrySpend(life, 1, "buy"));
            Assert.AreEqual(10, bag.GetAmount(_gem), "no gem may be charged after a Reject");

            bag.Dispose();
            UnityEngine.Object.DestroyImmediate(bp);
            UnityEngine.Object.DestroyImmediate(life);
            UnityEngine.Object.DestroyImmediate(reject);
        }

        private sealed class RejectFirstRule : ResourceRule
        {
            public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
                => owner != null ? new Instance(this, owner, bag) : null;

            private sealed class Instance : AttachedRule
            {
                public Instance(RejectFirstRule cfg, ResourceDefinition owner, ResourceBag bag)
                    : base(cfg, owner, bag) { }

                public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
                {
                    if (intent.Resource == Owner) intent.Outcome = SpendOutcome.Reject;
                }
            }
        }
    }
}
