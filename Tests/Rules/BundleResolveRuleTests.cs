using System.Collections.Generic;
using Hlight.ResourceBag.Rules;
using NUnit.Framework;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class BundleResolveRuleTests
    {
        private ResourceDefinition<TestResourceId> _pack;
        private ResourceDefinition<TestResourceId> _coin;
        private ResourceDefinition<TestResourceId> _gem;
        private BundleResolveRule _rule;
        private BagBlueprint _bp;
        private ResourceBag _bag;

        [SetUp]
        public void SetUp()
        {
            _coin = TestResourceDefinition.Create(TestResourceId.Coin);
            _gem = TestResourceDefinition.Create(TestResourceId.Gem);
            _rule = TestRuleFactory.Create<BundleResolveRule>();
            TestResourceDefinition.SetPrivate(_rule, "entries", new[]
            {
                new BundleResolveRule.BundleEntry { resource = _coin, amount = 50 },
                new BundleResolveRule.BundleEntry { resource = _gem, amount = 2 },
            });
            _pack = TestResourceDefinition.Create(TestResourceId.Pack,
                rules: new ResourceRule[] { _rule });
            _bp = TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { _pack, _coin, _gem });
            _bag = new ResourceBag("p", _bp);
            
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            UnityEngine.Object.DestroyImmediate(_rule);
            UnityEngine.Object.DestroyImmediate(_bp);
            UnityEngine.Object.DestroyImmediate(_pack);
            UnityEngine.Object.DestroyImmediate(_coin);
            UnityEngine.Object.DestroyImmediate(_gem);
        }

        [Test]
        public void Add_Bundle_ExpandsIntoEntries_PackNotStored()
        {
            _bag.Add(_pack, 1, "reward");
            Assert.AreEqual(0, _bag.GetAmount(_pack));
            Assert.AreEqual(50, _bag.GetAmount(_coin));
            Assert.AreEqual(2, _bag.GetAmount(_gem));
        }

        [Test]
        public void Add_BundleMultipleUnits_ScalesEntries()
        {
            _bag.Add(_pack, 3, "reward");
            Assert.AreEqual(150, _bag.GetAmount(_coin));
            Assert.AreEqual(6, _bag.GetAmount(_gem));
        }

        [Test]
        public void Add_BundleEntryOverflowsInt_ClampsToPositiveCredit()
        {
            // entries[0] is (coin, 50); 50 * 100_000_000 overflows int as a raw product.
            // An unguarded multiply would wrap negative, and ChangeInternal routes a
            // negative side-effect Delta as a debit — turning this Add into a spend.
            _bag.Add(_pack, 100_000_000, "reward");
            Assert.AreEqual(int.MaxValue, _bag.GetAmount(_coin));
            Assert.Greater(_bag.GetAmount(_coin), 0,
                "overflow must clamp to a positive credit, never flip sign into a debit");
        }

        [Test]
        public void Add_BundleEmitsResolveBundleReason()
        {
            var changes = new List<ResourceChange>();
            _bag.Changed += c => changes.Add(c);
            _bag.Add(_pack, 1, "reward");
            int hits = 0;
            for (int i = 0; i < changes.Count; i++)
            {
                if (changes[i].Reason == RuleReasons.ResolveBundle) hits++;
            }
            Assert.AreEqual(2, hits);
        }
    }
}
