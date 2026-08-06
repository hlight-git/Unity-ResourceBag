using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagTickTests
    {
        private ResourceDefinition<TestResourceId> _gold;
        private BagBlueprint _bp;
        private ResourceBag _bag;
        private RecordingRule _rule;
        private FakeBagClock _clock;

        [SetUp]
        public void SetUp()
        {
            _gold = TestResourceDefinition.Create(TestResourceId.Gold);
            _rule = ScriptableObject.CreateInstance<RecordingRule>();
            _bp = TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { _gold },
                extraRules: new ResourceRule[] { _rule });
            _clock = new FakeBagClock();
            _bag = new ResourceBag("p", _bp, clock: _clock);
        }

        [TearDown]
        public void TearDown()
        {
            _bag?.Dispose();
            UnityEngine.Object.DestroyImmediate(_rule);
            UnityEngine.Object.DestroyImmediate(_bp);
            UnityEngine.Object.DestroyImmediate(_gold);
        }

        [Test]
        public void Tick_InvokesAttachedRule()
        {
            _clock.Advance(0.5);
            _bag.Tick();
            Assert.AreEqual(1, _rule.TickCount);
            Assert.AreEqual(_clock.Now, _rule.LastNow);
        }

        [Test]
        public void Tick_MultipleCalls_Accumulate()
        {
            _bag.Tick();
            _clock.Advance(0.2);
            _bag.Tick();
            _clock.Advance(0.3);
            _bag.Tick();
            Assert.AreEqual(3, _rule.TickCount);
            Assert.AreEqual(_clock.Now, _rule.LastNow);
        }
    }
}
