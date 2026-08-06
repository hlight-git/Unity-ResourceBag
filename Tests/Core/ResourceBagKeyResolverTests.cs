using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    internal enum TestCurrencyId { Coin, Gem, Heart }

    internal sealed class TestCurrencyDef : ResourceDefinition<TestCurrencyId> { }

    internal sealed class TestCurrencyBlueprint : BagBlueprint<TestCurrencyId> { }

    /// <summary>
    /// The enum-keyed API on <see cref="ResourceBag{TKey}"/>.
    /// </summary>
    /// <remarks>
    /// What is deliberately NOT tested here: passing a key from another family, or wiring a
    /// blueprint of another family. Both are compile errors now — and a def of another family
    /// cannot even be authored into this blueprint, since the entry field is typed — so there
    /// is no runtime behaviour left to assert. The proof is that such code does not build.
    /// </remarks>
    [TestFixture]
    internal sealed class ResourceBagKeyResolverTests
    {
        private readonly List<Object> _cleanup = new List<Object>();
        private T Track<T>(T o) where T : Object { _cleanup.Add(o); return o; }

        private ResourceDefinition<TestCurrencyId> _coin;
        private ResourceDefinition<TestCurrencyId> _gem;
        private TestCurrencyBlueprint _bp;
        private ResourceBag<TestCurrencyId> _bag;

        private ResourceDefinition<TestCurrencyId> MakeKeyed(TestCurrencyId key)
        {
            var def = Track(ScriptableObject.CreateInstance<TestCurrencyDef>());
            def.name = key.ToString();
            TestResourceDefinition.SetPrivate(def, "key", key);
            TestResourceDefinition.SetPrivate(def, "rules", System.Array.Empty<ResourceRule>());
            return def;
        }

        [SetUp]
        public void SetUp()
        {
            _coin = MakeKeyed(TestCurrencyId.Coin);
            _gem = MakeKeyed(TestCurrencyId.Gem);

            // Heart is deliberately left unauthored: an enum member without a def is the one
            // key-related failure the type system cannot catch.
            _bp = Track(ScriptableObject.CreateInstance<TestCurrencyBlueprint>());
            _bp.name = "TestCurrencyBlueprint";
            TestResourceDefinition.SetPrivate(_bp, "resources", new[]
            {
                new BagBlueprint<TestCurrencyId>.Entry { resource = _coin },
                new BagBlueprint<TestCurrencyId>.Entry { resource = _gem },
            });
            TestResourceDefinition.SetPrivate(_bp, "extraRules", System.Array.Empty<ResourceRule>());

            _bag = new ResourceBag<TestCurrencyId>("p", _bp);
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            for (int i = 0; i < _cleanup.Count; i++)
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
            _cleanup.Clear();
        }

        [Test]
        public void IdDerivesFromKey()
        {
            Assert.AreEqual("Coin", _coin.Id);
        }

        [Test]
        public void Resolve_Hit_ReturnsDef()
        {
            Assert.AreSame(_coin, _bag.Resolve(TestCurrencyId.Coin));
            Assert.AreSame(_coin, _bag.Blueprint.ResolveByKey(TestCurrencyId.Coin));
        }

        [Test]
        public void Resolve_MemberWithoutDef_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => _bag.Resolve(TestCurrencyId.Heart));
        }

        [Test]
        public void TryResolveByKey_MemberWithoutDef_ReturnsFalse()
        {
            Assert.IsFalse(_bp.TryResolveByKey(TestCurrencyId.Heart, out var def));
            Assert.IsNull(def);
        }

        [Test]
        public void TypedApi_AddAndGetAmount()
        {
            _bag.Add(TestCurrencyId.Coin, 250, "quest");
            Assert.AreEqual(250, _bag.GetAmount(TestCurrencyId.Coin));
        }

        [Test]
        public void TypedApi_TrySpendAndHasAtLeast()
        {
            _bag.Add(TestCurrencyId.Coin, 100, "seed");
            Assert.IsTrue(_bag.HasAtLeast(TestCurrencyId.Coin, 50));
            Assert.IsTrue(_bag.TrySpend(TestCurrencyId.Coin, 40, "buy"));
            Assert.AreEqual(60, _bag.GetAmount(TestCurrencyId.Coin));
        }

        [Test]
        public void TypedApi_TrySpendAll_IsAtomic()
        {
            _bag.Add(TestCurrencyId.Coin, 100, "seed");
            _bag.Add(TestCurrencyId.Gem, 1, "seed");

            var ok = _bag.TrySpendAll(new (TestCurrencyId, int)[]
            {
                (TestCurrencyId.Coin, 10),
                (TestCurrencyId.Gem, 99),          // cannot be covered
            }, "craft");

            Assert.IsFalse(ok);
            Assert.AreEqual(100, _bag.GetAmount(TestCurrencyId.Coin), "the coin debit was rolled back");
            Assert.AreEqual(1, _bag.GetAmount(TestCurrencyId.Gem));
        }

        [Test]
        public void TypedApi_ResetAmount()
        {
            _bag.Add(TestCurrencyId.Gem, 5, "seed");
            _bag.ResetAmount(TestCurrencyId.Gem, "wipe");
            Assert.AreEqual(0, _bag.GetAmount(TestCurrencyId.Gem));
        }

        [Test]
        public void TypedApi_ReasonIsOptional()
        {
            _bag.Add(TestCurrencyId.Coin, 3);
            Assert.AreEqual(3, _bag.GetAmount(TestCurrencyId.Coin));
        }

        [Test]
        public void TypedApi_GetAndSetMaxAmount()
        {
            Assert.AreEqual(0, _bag.GetMaxAmount(TestCurrencyId.Coin));
            _bag.SetMaxAmount(TestCurrencyId.Coin, 500);
            Assert.AreEqual(500, _bag.GetMaxAmount(TestCurrencyId.Coin));
        }

        [Test]
        public void KeyedDef_PersistsUnderEnumName()
        {
            _bag.Add(TestCurrencyId.Coin, 7, "seed");
            var snapshot = new BagSnapshot();
            _bag.SaveTo(snapshot);

            var found = false;
            for (int i = 0; i < snapshot.Amounts.Count; i++)
                if (snapshot.Amounts[i].id == "Coin" && snapshot.Amounts[i].amount == 7) found = true;

            Assert.IsTrue(found, "Id comes from Key.ToString(), so the save key is the enum member name");
        }
    }
}
