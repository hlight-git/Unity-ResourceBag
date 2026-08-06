using System;
using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>Concrete blueprint for the tests' key family — one class per family, as projects do.</summary>
    internal sealed class TestBagBlueprint : BagBlueprint<TestResourceId> { }

    /// <summary>Factory for in-memory blueprint SOs.</summary>
    internal static class TestBlueprint
    {
        /// <summary>
        /// Convenience overload — wraps each def into an entry. <paramref name="maxAmounts"/>
        /// is positional and may be shorter than <paramref name="resources"/>; anything it does
        /// not cover stays uncapped. Use the entry overload when a test also needs a seed.
        /// </summary>
        public static TestBagBlueprint Create(
            ResourceDefinition<TestResourceId>[] resources,
            ResourceRule[] extraRules = null,
            int[] maxAmounts = null)
        {
            var entries = resources == null
                ? Array.Empty<BagBlueprint<TestResourceId>.Entry>()
                : WrapAsEntries(resources, maxAmounts);
            return Create(entries, extraRules);
        }

        /// <summary>Single capped def — the shape most rule tests want.</summary>
        public static TestBagBlueprint CreateCapped(
            ResourceDefinition<TestResourceId> resource,
            int maxAmount,
            ResourceRule[] extraRules = null)
            => Create(new[] { resource }, extraRules, new[] { maxAmount });

        /// <summary>Entry-based overload for tests that need preset initial amounts.</summary>
        public static TestBagBlueprint Create(
            BagBlueprint<TestResourceId>.Entry[] entries,
            ResourceRule[] extraRules = null)
        {
            var bp = ScriptableObject.CreateInstance<TestBagBlueprint>();
            bp.name = "TestBlueprint";
            TestResourceDefinition.SetPrivate(bp, "resources",
                entries ?? Array.Empty<BagBlueprint<TestResourceId>.Entry>());
            TestResourceDefinition.SetPrivate(bp, "extraRules", extraRules ?? Array.Empty<ResourceRule>());
            return bp;
        }

        /// <summary>One entry, spelled out — shorter than building the struct at the call site.</summary>
        public static BagBlueprint<TestResourceId>.Entry Entry(
            ResourceDefinition<TestResourceId> resource, int initialAmount = 0, int maxAmount = 0)
            => new BagBlueprint<TestResourceId>.Entry
            {
                resource = resource,
                initialAmount = initialAmount,
                maxAmount = maxAmount,
            };

        private static BagBlueprint<TestResourceId>.Entry[] WrapAsEntries(
            ResourceDefinition<TestResourceId>[] resources, int[] maxAmounts)
        {
            var entries = new BagBlueprint<TestResourceId>.Entry[resources.Length];
            for (int i = 0; i < resources.Length; i++)
            {
                entries[i] = Entry(resources[i], 0,
                    maxAmounts != null && i < maxAmounts.Length ? maxAmounts[i] : 0);
            }
            return entries;
        }
    }
}
