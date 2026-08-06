using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Data-driven resource lists (<see cref="ResourceAmount"/>) applied to a bag — the remote
    /// config path. Rows are matched by <see cref="ResourceDefinition.Id"/>, so these run on a
    /// plain bag with no key type involved.
    /// </summary>
    [TestFixture]
    internal sealed class ResourceConfigTests
    {
        private readonly List<Object> _cleanup = new List<Object>();
        private T Track<T>(T o) where T : Object { _cleanup.Add(o); return o; }

        private ResourceDefinition<TestResourceId> _gold;
        private ResourceDefinition<TestResourceId> _gem;
        private ResourceBag _bag;

        [SetUp]
        public void SetUp()
        {
            _gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            _gem = Track(TestResourceDefinition.Create(TestResourceId.Gem));
            var bp = Track(TestBlueprint.Create(new[] { _gold, _gem }));
            _bag = new ResourceBag("p", bp);
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
        public void Grant_FixedRows_Credit()
        {
            _bag.Grant(new[]
            {
                new ResourceAmount("Gold", 100),
                new ResourceAmount("Gem", 5),
            }, "event_reward");

            Assert.AreEqual(100, _bag.GetAmount(_gold));
            Assert.AreEqual(5, _bag.GetAmount(_gem));
        }

        [Test]
        public void Grant_RandomRow_AsksRollForTheInclusiveRange()
        {
            int seenMin = -1, seenMaxExclusive = -1;
            _bag.Grant(new[] { new ResourceAmount("Gold", 100, 300) }, "reward",
                roll: (min, maxExclusive) =>
                {
                    seenMin = min;
                    seenMaxExclusive = maxExclusive;
                    return 300;                       // the top of the range must be reachable
                });

            Assert.AreEqual(100, seenMin);
            Assert.AreEqual(301, seenMaxExclusive, "maxExclusive, matching Random.Range(int, int)");
            Assert.AreEqual(300, _bag.GetAmount(_gold));
        }

        [Test]
        public void Grant_RollOutsideRange_IsClamped()
        {
            _bag.Grant(new[] { new ResourceAmount("Gold", 10, 20) }, "reward", roll: (_, __) => 9999);
            Assert.AreEqual(20, _bag.GetAmount(_gold));
        }

        [Test]
        public void Grant_RandomRowWithoutRoll_PaysMinimumAndWarns()
        {
            // Silently always paying the floor is the kind of live-ops bug nobody reports.
            LogAssert.Expect(LogType.Warning, new Regex("no roll function"));

            _bag.Grant(new[] { new ResourceAmount("Gold", 100, 300) }, "reward");

            Assert.AreEqual(100, _bag.GetAmount(_gold));
        }

        [Test]
        public void Grant_UnknownId_SkipsThatRowOnly()
        {
            LogAssert.Expect(LogType.Warning, new Regex("not in this bag's blueprint"));

            _bag.Grant(new[]
            {
                new ResourceAmount("Gold", 50),
                new ResourceAmount("NopeNotAResource", 999),
                new ResourceAmount("Gem", 2),
            }, "reward");

            Assert.AreEqual(50, _bag.GetAmount(_gold), "rows before the bad one still land");
            Assert.AreEqual(2, _bag.GetAmount(_gem), "and so do rows after it");
        }

        [Test]
        public void TrySpendAll_Success_DebitsEveryRow()
        {
            _bag.Add(_gold, 100, "seed");
            _bag.Add(_gem, 10, "seed");

            var ok = _bag.TrySpendAll(new[]
            {
                new ResourceAmount("Gold", 30),
                new ResourceAmount("Gem", 2),
            }, "craft");

            Assert.IsTrue(ok);
            Assert.AreEqual(70, _bag.GetAmount(_gold));
            Assert.AreEqual(8, _bag.GetAmount(_gem));
        }

        [Test]
        public void TrySpendAll_UnknownId_RefusesTheWholeTransaction()
        {
            // The asymmetry with Grant: skipping a cost row would undercharge, so nothing moves.
            _bag.Add(_gold, 100, "seed");
            LogAssert.Expect(LogType.Warning, new Regex("refusing the whole cost list"));

            var ok = _bag.TrySpendAll(new[]
            {
                new ResourceAmount("Gold", 30),
                new ResourceAmount("NopeNotAResource", 1),
            }, "craft");

            Assert.IsFalse(ok);
            Assert.AreEqual(100, _bag.GetAmount(_gold), "not even the known row was charged");
        }

        [Test]
        public void TrySpendAll_ShortBalance_RollsBack()
        {
            _bag.Add(_gold, 100, "seed");

            var ok = _bag.TrySpendAll(new[]
            {
                new ResourceAmount("Gold", 30),
                new ResourceAmount("Gem", 1),      // known, but the balance is 0
            }, "craft");

            Assert.IsFalse(ok);
            Assert.AreEqual(100, _bag.GetAmount(_gold));
        }

        [Test]
        public void CanAfford_ReadsBalancesWithoutSpending()
        {
            _bag.Add(_gold, 100, "seed");
            var cost = new[] { new ResourceAmount("Gold", 100) };

            Assert.IsTrue(_bag.CanAfford(cost));
            Assert.AreEqual(100, _bag.GetAmount(_gold), "checking must not charge");

            Assert.IsFalse(_bag.CanAfford(new[] { new ResourceAmount("Gold", 101) }));
        }

        [Test]
        public void CanAfford_UnknownId_IsFalse()
        {
            LogAssert.Expect(LogType.Warning, new Regex("refusing the whole cost list"));
            Assert.IsFalse(_bag.CanAfford(new[] { new ResourceAmount("NopeNotAResource", 1) }));
        }

        [Test]
        public void Grant_ReportsWhatEachRowResolvedTo()
        {
            // Without this the caller cannot show a random reward: the roll happens inside.
            var granted = new List<(ResourceDefinition resource, int amount)>();
            granted.Add((_gem, 999));                  // pre-existing content must be cleared

            _bag.Grant(new[]
            {
                new ResourceAmount("Gold", 100, 300),
                new ResourceAmount("Gem", 5),
            }, "reward", roll: (_, __) => 250, granted: granted);

            Assert.AreEqual(2, granted.Count);
            Assert.AreSame(_gold, granted[0].resource);
            Assert.AreEqual(250, granted[0].amount, "the rolled amount, not the row's minimum");
            Assert.AreSame(_gem, granted[1].resource);
            Assert.AreEqual(5, granted[1].amount);
        }

        [Test]
        public void Grant_SkippedRowIsNotReported()
        {
            var granted = new List<(ResourceDefinition resource, int amount)>();
            LogAssert.Expect(LogType.Warning, new Regex("not in this bag's blueprint"));

            _bag.Grant(new[]
            {
                new ResourceAmount("NopeNotAResource", 1),
                new ResourceAmount("Gold", 7),
            }, "reward", granted: granted);

            Assert.AreEqual(1, granted.Count);
            Assert.AreSame(_gold, granted[0].resource);
        }

        [Test]
        public void EmptyList_IsANoOpAndSucceeds()
        {
            var empty = new ResourceAmount[0];

            _bag.Grant(empty, "reward");
            Assert.AreEqual(0, _bag.GetAmount(_gold));
            Assert.IsTrue(_bag.TrySpendAll(empty, "craft"), "nothing to charge is not a failure");
            Assert.IsTrue(_bag.CanAfford(empty));
        }

        [Test]
        public void IdMatchesTheEnumMemberName()
        {
            // The whole reason config can name resources without asset references.
            Assert.AreEqual("Gold", _gold.Id);
            _bag.Grant(new[] { new ResourceAmount(TestResourceId.Gold.ToString(), 1) }, "reward");
            Assert.AreEqual(1, _bag.GetAmount(_gold));
        }
    }
}
