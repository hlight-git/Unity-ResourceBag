using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Pins the authoring path the docs call the recommended pattern: a one-line
    /// <see cref="ResourceDefinition{TKey}"/> subclass whose <c>key</c> field is set in the
    /// Inspector and whose <see cref="ResourceDefinition.Id"/> follows from it.
    /// </summary>
    /// <remarks>
    /// Every other test sets <c>key</c> by reflection, which proves nothing about the
    /// Inspector: reflection reaches private fields whether or not Unity serializes them.
    /// These tests go through <see cref="SerializedObject"/> — the same API the Inspector
    /// uses — against a real resource written to disk and reloaded, so a regression in
    /// Unity's handling of a serialized field declared on a generic base class shows up
    /// here rather than as a designer reporting an empty Inspector.
    /// </remarks>
    [TestFixture]
    internal sealed class ResourceDefinitionSerializationTests
    {
        private const string TempDir = "Assets/__ResourceBagSerializationTests";

        [SetUp]
        public void SetUp()
        {
            if (!Directory.Exists(TempDir)) AssetDatabase.CreateFolder("Assets", "__ResourceBagSerializationTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.DeleteAsset(TempDir);
        }

        [Test]
        public void KeyField_IsVisibleToSerializedObject()
        {
            var def = ScriptableObject.CreateInstance<TestCurrencyDef>();
            try
            {
                var so = new SerializedObject(def);
                var property = so.FindProperty("key");

                Assert.IsNotNull(property,
                    "ResourceDefinition<TKey>.key must be reachable through SerializedObject — " +
                    "if it is not, the Inspector shows no Key field and the whole enum-keyed " +
                    "pattern cannot be authored.");
                Assert.AreEqual(SerializedPropertyType.Enum, property.propertyType);
            }
            finally
            {
                Object.DestroyImmediate(def);
            }
        }

        [Test]
        public void KeySetViaSerializedObject_IsWrittenToDisk()
        {
            var path = TempDir + "/GemDef.asset";

            var created = ScriptableObject.CreateInstance<TestCurrencyDef>();
            AssetDatabase.CreateAsset(created, path);

            // Exactly what a designer does in the Inspector, not a reflection shortcut.
            var so = new SerializedObject(created);
            so.FindProperty("key").enumValueIndex = (int)TestCurrencyId.Gem;
            so.FindProperty("displayName").stringValue = "GemDisplay";
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            // Read the YAML Unity wrote. This is the claim that matters — that the field
            // declared on the generic base actually reaches the file — and it needs no
            // unload/reimport dance to prove, which is what made an earlier version of
            // this test flaky rather than informative.
            var yaml = File.ReadAllText(path);

            Assert.That(yaml, Does.Contain("key: " + (int)TestCurrencyId.Gem),
                "Unity must serialize ResourceDefinition<TKey>.key into the asset file — " +
                "without it the Inspector edit is lost on reload and the enum-keyed " +
                "pattern silently degrades to every def resolving as the zero enum member." +
                "\nFile was:\n" + yaml);
            Assert.That(yaml, Does.Contain("displayName: GemDisplay"),
                "fields inherited from the non-generic base must serialize too");

            // And the in-memory object agrees with what was written.
            Assert.AreEqual(TestCurrencyId.Gem, created.Key);
            Assert.AreEqual("Gem", created.Id, "Id is derived from Key.ToString()");
        }

        [Test]
        public void AuthoredDefAndBlueprint_DriveARealBag()
        {
            // The end-to-end authoring path the README's "start here" describes: author the
            // defs, author the blueprint, hand the blueprint to a bag, operate by enum.
            var coinPath = TempDir + "/CoinDef.asset";
            var blueprintPath = TempDir + "/Blueprint.asset";

            var coin = ScriptableObject.CreateInstance<TestCurrencyDef>();
            AssetDatabase.CreateAsset(coin, coinPath);
            var coinSo = new SerializedObject(coin);
            coinSo.FindProperty("key").enumValueIndex = (int)TestCurrencyId.Coin;
            coinSo.ApplyModifiedPropertiesWithoutUndo();

            var blueprint = ScriptableObject.CreateInstance<TestCurrencyBlueprint>();
            AssetDatabase.CreateAsset(blueprint, blueprintPath);
            var bpSo = new SerializedObject(blueprint);
            var resources = bpSo.FindProperty("resources");
            resources.arraySize = 1;
            var entry = resources.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("resource").objectReferenceValue = coin;
            entry.FindPropertyRelative("initialAmount").intValue = 25;
            entry.FindPropertyRelative("maxAmount").intValue = 40;   // cap is scope policy, so it lives here
            bpSo.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();

            var bag = new ResourceBag<TestCurrencyId>("player",
                AssetDatabase.LoadAssetAtPath<TestCurrencyBlueprint>(blueprintPath));
            try
            {
                Assert.AreEqual(25, bag.GetAmount(TestCurrencyId.Coin), "initialAmount seeds the bag");
                Assert.AreEqual(40, bag.GetMaxAmount(TestCurrencyId.Coin), "the entry's cap reaches the bag");

                bag.Add(TestCurrencyId.Coin, 10, "quest");
                Assert.AreEqual(35, bag.GetAmount(TestCurrencyId.Coin));
                Assert.IsTrue(bag.TrySpend(TestCurrencyId.Coin, 5, "shop"));
                Assert.AreEqual(30, bag.GetAmount(TestCurrencyId.Coin));

                bag.Add(TestCurrencyId.Coin, 100, "windfall");
                Assert.AreEqual(40, bag.GetAmount(TestCurrencyId.Coin), "clamped at the entry's cap");
            }
            finally
            {
                bag.Dispose();
            }
        }
    }
}
