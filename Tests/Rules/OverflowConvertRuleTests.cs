using System.Collections.Generic;
using Hlight.ResourceBag.Rules;
using NUnit.Framework;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class OverflowConvertRuleTests
    {
        private ResourceDefinition<TestResourceId> _coin;
        private ResourceDefinition<TestResourceId> _scrap;
        private OverflowConvertRule _rule;
        private BagBlueprint _bp;
        private ResourceBag _bag;

        [SetUp]
        public void SetUp()
        {
            _scrap = TestResourceDefinition.Create(TestResourceId.Scrap); // unlimited
            _rule = TestRuleFactory.Create<OverflowConvertRule>();
            TestResourceDefinition.SetPrivate(_rule, "convertTo", _scrap);
            TestResourceDefinition.SetPrivate(_rule, "conversionRatio", 1f);
            TestResourceDefinition.SetPrivate(_rule, "rounding", RoundingMode.Ceil);
            _coin = TestResourceDefinition.Create(TestResourceId.Coin,
                rules: new ResourceRule[] { _rule });
            _bp = TestBlueprint.Create(new[] { _coin, _scrap }, maxAmounts: new[] { 100, 0 });
            _bag = new ResourceBag("p", _bp);
            
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            UnityEngine.Object.DestroyImmediate(_rule);
            UnityEngine.Object.DestroyImmediate(_bp);
            UnityEngine.Object.DestroyImmediate(_coin);
            UnityEngine.Object.DestroyImmediate(_scrap);
        }

        [Test]
        public void Add_NoOverflow_NoSideEffect()
        {
            _bag.Add(_coin, 50, "reward");
            Assert.AreEqual(50, _bag.GetAmount(_coin));
            Assert.AreEqual(0, _bag.GetAmount(_scrap));
        }

        [Test]
        public void Add_ExceedsCap_SurplusGoesToConvertTo()
        {
            _bag.Add(_coin, 90, "seed");
            _bag.Add(_coin, 30, "reward"); // 20 over cap
            Assert.AreEqual(100, _bag.GetAmount(_coin));
            Assert.AreEqual(20, _bag.GetAmount(_scrap));
        }

        [Test]
        public void Add_UnlimitedCap_NoConversion()
        {
            _bag.SetMaxAmount(_coin, 0); // unlimited
            _bag.Add(_coin, 5_000, "reward");
            Assert.AreEqual(5_000, _bag.GetAmount(_coin));
            Assert.AreEqual(0, _bag.GetAmount(_scrap));
        }

        [Test]
        public void Add_RatioAndCeilRounding_AppliesToOverflow()
        {
            TestResourceDefinition.SetPrivate(_rule, "conversionRatio", 0.5f);
            _bag.Add(_coin, 90, "seed");
            _bag.Add(_coin, 11, "reward"); // 1 over cap, 0.5 ceil → 1
            Assert.AreEqual(100, _bag.GetAmount(_coin));
            Assert.AreEqual(1, _bag.GetAmount(_scrap));
        }

        [Test]
        public void Add_AlreadyOverCap_ConvertsOnlyTheIncomingAmount()
        {
            // B2. overflow was (current + delta) - cap without bounding by delta, so an
            // already-over-cap balance minted more than the add was worth.
            var bp = TestBlueprint.Create(new[]
            {
                // Seeding above the cap is legal — the seed bypasses clamping — which is one
                // of the two ways a bag can open already over its limit.
                new BagBlueprint<TestResourceId>.Entry { resource = _coin, initialAmount = 150, maxAmount = 100 },
                new BagBlueprint<TestResourceId>.Entry { resource = _scrap },
            });
            var bag = new ResourceBag("over", bp);

            bag.Add(_coin, 10, "reward");

            Assert.AreEqual(150, bag.GetAmount(_coin));
            Assert.AreEqual(10, bag.GetAmount(_scrap), "at most the 10 that came in may convert");

            bag.Dispose();
            UnityEngine.Object.DestroyImmediate(bp);
        }

        [Test]
        public void Add_CapLoweredBelowCurrent_ConvertsOnlyTheIncomingAmount()
        {
            _bag.Add(_coin, 100, "seed");
            _bag.SetMaxAmount(_coin, 50);

            _bag.Add(_coin, 10, "reward");

            Assert.AreEqual(100, _bag.GetAmount(_coin));
            Assert.AreEqual(10, _bag.GetAmount(_scrap));
        }
    }
}
