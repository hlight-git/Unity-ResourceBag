using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Rule state: one row per stateful rule, the value produced by
    /// <see cref="AttachedRule{TState}"/> (or by a rule's own Serialize override).
    /// </summary>
    [TestFixture]
    internal sealed class RuleStateTests
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

        private BagBlueprint MakeBag(params ResourceRule[] rules)
        {
            var gold = Track(TestResourceDefinition.Create(TestResourceId.Gold, rules: rules));
            return Track(TestBlueprint.Create(new[] { gold }));
        }

        [Test]
        public void TypedState_AllFieldsRoundTripInOneRow()
        {
            var rule = Track(TestRuleFactory.Create<MultiFieldStateRule>());
            rule.Seed = new MultiFieldStateRule.Data
            {
                next = 1780512345.5, streak = 4, tag = "boosted", flagged = true,
            };
            var bp = MakeBag(rule);

            var snapshot = new BagSnapshot();
            var first = new ResourceBag("p", bp);
            first.SaveTo(snapshot);
            first.Dispose();

            // Several values, still one row — and no type name in it, so renaming the rule
            // class can never invalidate a save.
            Assert.AreEqual(1, snapshot.RuleState.Count);
            Assert.AreEqual("Gold:state", snapshot.RuleState[0].key);
            StringAssert.DoesNotContain("MultiFieldStateRule", snapshot.RuleState[0].value);

            // Different constructor defaults, so a restored value cannot be mistaken for the
            // seed never having changed.
            rule.Seed = new MultiFieldStateRule.Data();

            var second = new ResourceBag("p", bp, snapshot);
            second.Tick();
            Assert.AreEqual(1780512345.5, rule.Mirrored.next);   // exact, not "close enough"
            Assert.AreEqual(4, rule.Mirrored.streak);
            Assert.AreEqual("boosted", rule.Mirrored.tag);
            Assert.IsTrue(rule.Mirrored.flagged);
            second.Dispose();
        }

        [Test]
        public void TypedState_CorruptValueKeepsDefaultsAndWarns()
        {
            var rule = Track(TestRuleFactory.Create<MultiFieldStateRule>());
            rule.Seed = new MultiFieldStateRule.Data { next = 9, tag = "seed" };
            var bp = MakeBag(rule);

            var snapshot = new BagSnapshot();
            snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry { key = "Gold:state", value = "{not json" });

            LogAssert.Expect(LogType.Warning, new Regex("not valid Data data"));

            var bag = new ResourceBag("p", bp, snapshot);
            bag.Tick();

            Assert.AreEqual(9, rule.Mirrored.next);            // constructor's seed, not zeroed
            Assert.AreEqual("seed", rule.Mirrored.tag);
            bag.Dispose();
        }

        [Test]
        public void CustomFormat_RuleDecidesEncoding()
        {
            var rule = Track(TestRuleFactory.Create<CustomFormatStateRule>());
            rule.Seed = 0.1 + 0.2;                             // 0.30000000000000004
            var bp = MakeBag(rule);

            var snapshot = new BagSnapshot();
            var first = new ResourceBag("p", bp);
            first.SaveTo(snapshot);
            first.Dispose();

            // The override wrote a bare number, not JSON.
            Assert.AreEqual(rule.LastWritten, snapshot.RuleState[0].value);
            StringAssert.DoesNotContain("{", snapshot.RuleState[0].value);

            rule.Seed = 0;
            var second = new ResourceBag("p", bp, snapshot);
            second.Tick();
            Assert.AreEqual(0.1 + 0.2, rule.Mirrored);
            second.Dispose();
        }

        [Test]
        public void OnStateLoaded_RepairsRestoredValue()
        {
            // Decoding says "this is a number"; judging whether the number is usable is the
            // separate hook — the split PeriodicDeltaRule relies on for a fire time that came
            // from a foreign clock epoch.
            var rule = Track(TestRuleFactory.Create<CustomFormatStateRule>());
            rule.Seed = 5;
            var bp = MakeBag(rule);

            var snapshot = new BagSnapshot();
            snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry { key = "Gold:state", value = "-40" });

            var bag = new ResourceBag("p", bp, snapshot);
            bag.Tick();

            Assert.AreEqual(0, rule.Mirrored, "the loaded value was decoded, then clamped");
            bag.Dispose();
        }

        [Test]
        public void CustomFormat_UnparsableValueKeepsConstructorState()
        {
            var rule = Track(TestRuleFactory.Create<CustomFormatStateRule>());
            rule.Seed = 5;
            var bp = MakeBag(rule);

            var snapshot = new BagSnapshot();
            snapshot.RuleState.Add(new BagSnapshot.RuleStateEntry { key = "Gold:state", value = "garbage" });

            var bag = new ResourceBag("p", bp, snapshot);
            bag.Tick();

            Assert.AreEqual(5, rule.Mirrored);
            bag.Dispose();
        }

        [Test]
        public void StatelessRule_WritesNothing()
        {
            var recording = Track(ScriptableObject.CreateInstance<RecordingRule>());
            var bp = MakeBag(recording);
            var bag = new ResourceBag("p", bp);

            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);

            Assert.AreEqual(0, snapshot.RuleState.Count);
            bag.Dispose();
        }

        [Test]
        public void EmptyValue_WritesNoRow()
        {
            // Writing the row would come back through LoadState as unparsable and be reported
            // as a corrupt save — one the package itself wrote.
            var rule = Track(TestRuleFactory.Create<EmptyValueStateRule>());
            var bp = MakeBag(rule);
            var bag = new ResourceBag("p", bp);

            var snapshot = new BagSnapshot();
            bag.SaveTo(snapshot);

            Assert.AreEqual(0, snapshot.RuleState.Count);
            Assert.DoesNotThrow(() => new ResourceBag("p", bp, snapshot).Dispose());
            bag.Dispose();
        }

        [Test]
        public void DuplicateKey_WarnsOnSave()
        {
            // Two attachments on one def with the same StateKey: the second would silently
            // start cold on load, so saving says so.
            var a = Track(TestRuleFactory.Create<FixedKeyStateRule>());
            var b = Track(TestRuleFactory.Create<FixedKeyStateRule>());
            var bp = MakeBag(a, b);
            var bag = new ResourceBag("p", bp);

            LogAssert.Expect(LogType.Warning, new Regex("written twice"));
            bag.SaveTo(new BagSnapshot());

            bag.Dispose();
        }
    }
}
