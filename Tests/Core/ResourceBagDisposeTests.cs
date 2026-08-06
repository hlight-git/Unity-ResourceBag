using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagDisposeTests
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

        [Test]
        public void Dispose_DetachesAllRulesInReverseOrder()
        {
            RecordingRule.ResetSequence();

            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);

            var r1 = Track(ScriptableObject.CreateInstance<RecordingRule>());
            var r2 = Track(ScriptableObject.CreateInstance<RecordingRule>());
            var r3 = Track(ScriptableObject.CreateInstance<RecordingRule>());

            bag.AttachRule(r1);
            bag.AttachRule(r2);
            bag.AttachRule(r3);

            bag.Dispose();

            Assert.AreEqual(1, r1.DetachCount);
            Assert.AreEqual(1, r2.DetachCount);
            Assert.AreEqual(1, r3.DetachCount);

            // DetachCount alone can't distinguish forward from reverse order — it passes
            // identically either way. The shared sequence counter is the only observable
            // proof: reverse attach order means r3 (attached last) detaches first.
            Assert.Less(r3.DetachOrder, r2.DetachOrder,
                "r3 attached last, so reverse-order detach must visit it before r2");
            Assert.Less(r2.DetachOrder, r1.DetachOrder,
                "r2 attached before r1, so reverse-order detach must visit r2 before r1");
        }

        [Test]
        public void Dispose_Twice_NoThrowAndIdempotent()
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);
            bag.Dispose();
            Assert.DoesNotThrow(() => bag.Dispose());
        }

        [Test]
        public void Dispose_ClearsAmounts()
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold));
            var bp = Track(TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { gold }));
            var bag = new ResourceBag("p", bp);
            bag.Add(gold, 10, "seed");
            bag.Dispose();
            Assert.AreEqual(0, bag.GetAmount(gold));
        }
    }
}
