using NUnit.Framework;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagBasicOpsTests
    {
        private ResourceDefinition<TestResourceId> _gold;
        private ResourceDefinition<TestResourceId> _gem;
        private BagBlueprint _bp;
        private ResourceBag _bag;

        [SetUp]
        public void SetUp()
        {
            _gold = TestResourceDefinition.Create(TestResourceId.Gold);
            _gem = TestResourceDefinition.Create(TestResourceId.Gem);
            _bp = TestBlueprint.Create(new[] { _gold, _gem }, maxAmounts: new[] { 100, 0 }); // gem uncapped
            _bag = new ResourceBag("player", _bp);
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            UnityEngine.Object.DestroyImmediate(_bp);
            UnityEngine.Object.DestroyImmediate(_gold);
            UnityEngine.Object.DestroyImmediate(_gem);
        }

        [Test]
        public void GetAmount_UntrackedAsset_ReturnsZero()
        {
            Assert.AreEqual(0, _bag.GetAmount(_gold));
            Assert.AreEqual(0, _bag.GetAmount(null));
        }

        [Test]
        public void Add_Positive_IncrementsAmount()
        {
            _bag.Add(_gold, 25, "test");
            Assert.AreEqual(25, _bag.GetAmount(_gold));
            Assert.IsTrue(_bag.HasAtLeast(_gold, 25));
        }

        [Test]
        public void Add_ExceedsMaxAmount_ClampedAtMax()
        {
            _bag.Add(_gold, 200, "test");
            Assert.AreEqual(100, _bag.GetAmount(_gold));
        }

        [Test]
        public void Add_UnlimitedCap_NotClamped()
        {
            _bag.Add(_gem, 1_000_000, "test");
            Assert.AreEqual(1_000_000, _bag.GetAmount(_gem));
        }

        [Test]
        public void Add_FiresChangedEvent()
        {
            ResourceChange? captured = null;
            _bag.Changed += c => captured = c;
            _bag.Add(_gold, 10, "reward");
            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(10, captured.Value.Delta);
            Assert.AreEqual(0, captured.Value.OldAmount);
            Assert.AreEqual(10, captured.Value.NewAmount);
            Assert.AreEqual("reward", captured.Value.Reason);
        }

        [Test]
        public void TrySpend_InsufficientBalance_ReturnsFalse()
        {
            _bag.Add(_gold, 5, "seed");
            Assert.IsFalse(_bag.TrySpend(_gold, 10, "buy"));
            Assert.AreEqual(5, _bag.GetAmount(_gold));
        }

        [Test]
        public void TrySpend_Sufficient_DecrementsAndReturnsTrue()
        {
            _bag.Add(_gold, 20, "seed");
            Assert.IsTrue(_bag.TrySpend(_gold, 7, "buy"));
            Assert.AreEqual(13, _bag.GetAmount(_gold));
        }

        [Test]
        public void SetMaxAmount_DoesNotAutoClampExistingAmount()
        {
            _bag.Add(_gem, 500, "seed");
            _bag.SetMaxAmount(_gem, 100);
            Assert.AreEqual(500, _bag.GetAmount(_gem));
            _bag.Add(_gem, 50, "extra");
            Assert.AreEqual(500, _bag.GetAmount(_gem)); // already over max → no headroom
        }

        [Test]
        public void GetMaxAmount_OverrideTakesPrecedence()
        {
            Assert.AreEqual(100, _bag.GetMaxAmount(_gold));
            _bag.SetMaxAmount(_gold, 250);
            Assert.AreEqual(250, _bag.GetMaxAmount(_gold));
        }

        [Test]
        public void ResetAmount_ZerosAmountAndFiresChange()
        {
            _bag.Add(_gold, 30, "seed");
            ResourceChange? captured = null;
            _bag.Changed += c => captured = c;
            _bag.ResetAmount(_gold, "wipe");
            Assert.AreEqual(0, _bag.GetAmount(_gold));
            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(-30, captured.Value.Delta);
            Assert.AreEqual("wipe", captured.Value.Reason);
        }

        [Test]
        public void ResetAmount_AlreadyZero_NoEvent()
        {
            bool fired = false;
            _bag.Changed += _ => fired = true;
            _bag.ResetAmount(_gold, "noop");
            Assert.IsFalse(fired);
        }

        [Test]
        public void TrySpendAll_AnyShortfall_NoMutation()
        {
            _bag.Add(_gold, 5, "seed");
            _bag.Add(_gem, 5, "seed");
            var ok = _bag.TrySpendAll(new (ResourceDefinition, int)[]
            {
                (_gold, 3), (_gem, 99),
            }, "buy");
            Assert.IsFalse(ok);
            Assert.AreEqual(5, _bag.GetAmount(_gold));
            Assert.AreEqual(5, _bag.GetAmount(_gem));
        }

        [Test]
        public void TrySpendAll_AllSufficient_CommitsBoth()
        {
            _bag.Add(_gold, 10, "seed");
            _bag.Add(_gem, 10, "seed");
            var ok = _bag.TrySpendAll(new (ResourceDefinition, int)[]
            {
                (_gold, 3), (_gem, 4),
            }, "buy");
            Assert.IsTrue(ok);
            Assert.AreEqual(7, _bag.GetAmount(_gold));
            Assert.AreEqual(6, _bag.GetAmount(_gem));
        }

        [Test]
        public void Add_NullAssetOrZero_NoOp()
        {
            _bag.Add(null, 10, "x");
            _bag.Add(_gold, 0, "x");
            _bag.Add(_gold, -5, "x");
            Assert.AreEqual(0, _bag.GetAmount(_gold));
        }

        [Test]
        public void Add_OverflowReason_FiredWhenAtCap()
        {
            _bag.Add(_gold, 100, "seed");
            ResourceChange? captured = null;
            _bag.Changed += c => captured = c;
            _bag.Add(_gold, 50, "extra");
            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(0, captured.Value.Delta);
            Assert.AreEqual(BagReasons.Overflow, captured.Value.Reason);
        }
    }
}
