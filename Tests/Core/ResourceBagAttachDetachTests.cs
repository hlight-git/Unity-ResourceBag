using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    [TestFixture]
    internal sealed class ResourceBagAttachDetachTests
    {
        private ResourceDefinition<TestResourceId> _gold;
        private BagBlueprint _bp;
        private ResourceBag _bag;
        private RecordingRule _rule;

        [SetUp]
        public void SetUp()
        {
            _gold = TestResourceDefinition.Create(TestResourceId.Gold);
            _bp = TestBlueprint.Create(new ResourceDefinition<TestResourceId>[] { _gold });
            _bag = new ResourceBag("p", _bp);
            _rule = ScriptableObject.CreateInstance<RecordingRule>();
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
        public void AttachRule_Once_CallsOnAttachOnce()
        {
            _bag.AttachRule(_rule);
            Assert.AreEqual(1, _rule.AttachCount);
        }

        [Test]
        public void AttachRule_Duplicate_NoOp()
        {
            _bag.AttachRule(_rule);
            _bag.AttachRule(_rule);
            _bag.AttachRule(_rule);
            Assert.AreEqual(1, _rule.AttachCount);
        }

        [Test]
        public void DetachRule_WithoutAttach_NoOp()
        {
            _bag.DetachRule(_rule);
            Assert.AreEqual(0, _rule.DetachCount);
        }

        [Test]
        public void DetachRule_AfterAttach_CallsOnDetach()
        {
            _bag.AttachRule(_rule);
            _bag.DetachRule(_rule);
            Assert.AreEqual(1, _rule.DetachCount);
        }

        [Test]
        public void AttachRule_Null_NoOp()
        {
            _bag.AttachRule(null);
            _bag.DetachRule(null);
            // No throw.
            Assert.Pass();
        }
    }
}
