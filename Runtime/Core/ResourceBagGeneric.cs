using System;
using System.Collections.Generic;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// A bag whose resources are keyed by <typeparamref name="TKey"/>. Same runtime as
    /// <see cref="ResourceBag"/>, plus the enum-keyed API — and the compiler now rejects a key
    /// from another family, and a blueprint from another family:
    /// <code>
    /// var bag = new ResourceBag&lt;CurrencyId&gt;("player", currencyBlueprint);
    /// bag.Add(CurrencyId.Coin, 100, "reward");   // ok
    /// bag.Add(BoosterId.Hammer, 1);              // does not compile
    /// </code>
    /// </summary>
    /// <remarks>
    /// The def-keyed API is inherited unchanged, which is what rules use: a rule sees its bag
    /// as <see cref="ResourceBag"/> (it cannot know the family) and works from the def refs it
    /// was authored with.
    /// <para>
    /// One runtime failure remains and no type can remove it: an enum member whose def was
    /// never authored into the blueprint throws <see cref="KeyNotFoundException"/>, because
    /// that mapping lives in an asset rather than in the type system.
    /// </para>
    /// </remarks>
    public class ResourceBag<TKey> : ResourceBag where TKey : struct, Enum
    {
        private readonly BagBlueprint<TKey> _typedBlueprint;

        /// <summary>The authored scope, narrowed to this bag's key family.</summary>
        public new BagBlueprint<TKey> Blueprint => _typedBlueprint;

        public ResourceBag(string id, BagBlueprint<TKey> blueprint, BagSnapshot snapshot = null,
                           IBagClock clock = null, IBagServiceLocator locator = null)
            : base(id, blueprint, snapshot, clock, locator)
        {
            _typedBlueprint = blueprint;
        }

        /// <summary>Resolve <paramref name="key"/> to its def. Throws when the scope has no def for it.</summary>
        public ResourceDefinition Resolve(TKey key) => _typedBlueprint.ResolveByKey(key);

        public void Add(TKey key, int amount, string reason = BagReasons.Unspecified)
            => Add(Resolve(key), amount, reason);

        public bool TrySpend(TKey key, int amount, string reason = BagReasons.Unspecified)
            => TrySpend(Resolve(key), amount, reason);

        /// <summary>
        /// All-or-nothing multi-debit within this family. Mixing families in one transaction is
        /// impossible by construction — a bag holds one family — so the def-keyed overload
        /// inherited from <see cref="ResourceBag"/> exists for costs that reach outside the
        /// blueprint (a rule-credited def, say).
        /// </summary>
        public bool TrySpendAll(IReadOnlyList<(TKey key, int amount)> items,
                                string reason = BagReasons.Unspecified)
        {
            if (items == null || items.Count == 0) return true;

            var mapped = new (ResourceDefinition, int)[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                mapped[i] = (Resolve(items[i].key), items[i].amount);
            }

            return TrySpendAll(mapped, reason);
        }

        public int GetAmount(TKey key) => GetAmount(Resolve(key));

        public bool HasAtLeast(TKey key, int amount) => HasAtLeast(Resolve(key), amount);

        public void ResetAmount(TKey key, string reason = BagReasons.Unspecified)
            => ResetAmount(Resolve(key), reason);

        public int GetMaxAmount(TKey key) => GetMaxAmount(Resolve(key));

        public void SetMaxAmount(TKey key, int maxAmount) => SetMaxAmount(Resolve(key), maxAmount);

        // C# converts any zero-valued constant to any enum, so `bag.Add(0, 5)` would otherwise
        // compile and quietly mean the enum's first member. These take that call and refuse it
        // at compile time. Only on the two methods that move a player's balance — the rest are
        // not worth the noise.

        [Obsolete("Pass a " + nameof(TKey) + " member, not a number.", error: true)]
        public void Add(int key, int amount, string reason = BagReasons.Unspecified)
            => throw new NotSupportedException();

        [Obsolete("Pass a " + nameof(TKey) + " member, not a number.", error: true)]
        public bool TrySpend(int key, int amount, string reason = BagReasons.Unspecified)
            => throw new NotSupportedException();
    }
}
