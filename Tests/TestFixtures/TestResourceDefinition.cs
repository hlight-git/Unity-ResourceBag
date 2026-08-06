using System.Reflection;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Key family every test def belongs to. Members are named after what the tests call
    /// them, and <see cref="ResourceDefinition.Id"/> is the member name — so an assertion on
    /// a saved id reads "Gold", not "gold".
    /// </summary>
    internal enum TestResourceId
    {
        Gold,
        Gem,
        Energy,
        Coin,
        Xp,
        Scrap,
        Refund,
        Pack,
        Mana,
        Life,
    }

    /// <summary>Concrete keyed def for tests — a def has to belong to a family to enter a blueprint.</summary>
    internal sealed class TestResourceDef : ResourceDefinition<TestResourceId> { }

    /// <summary>
    /// Factory for in-memory ResourceDefinition SOs. Uses reflection to set private
    /// serialized fields without needing UnityEditor.SerializedObject (keeps helpers
    /// usable in EditMode without an asset file).
    /// </summary>
    internal static class TestResourceDefinition
    {
        // No maxAmount: a cap is per-scope policy and lives on the blueprint entry.
        // Use TestBlueprint.Create(..., maxAmounts:) or TestBlueprint.CreateCapped(...).
        public static ResourceDefinition<TestResourceId> Create(TestResourceId key,
            string displayName = null, ResourceRule[] rules = null)
        {
            var def = ScriptableObject.CreateInstance<TestResourceDef>();
            def.name = key.ToString();
            SetPrivate(def, "key", key);
            SetPrivate(def, "displayName", displayName ?? key.ToString());
            SetPrivate(def, "rules", rules ?? System.Array.Empty<ResourceRule>());
            return def;
        }

        /// <summary>
        /// Sets a private/serialized field by name, walking up the inheritance chain
        /// so base-class fields are found even when called on a derived type.
        /// </summary>
        public static void SetPrivate(object target, string fieldName, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                t = t.BaseType;
            }
            if (f == null)
            {
                throw new System.InvalidOperationException(
                    $"Field '{fieldName}' not found on {target.GetType().Name} or its base types");
            }
            f.SetValue(target, value);
        }
    }
}
