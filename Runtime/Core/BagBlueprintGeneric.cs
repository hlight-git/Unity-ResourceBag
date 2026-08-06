using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Authored scope for one key family. Subclass it once per enum — the subclass is what
    /// carries <c>[CreateAssetMenu]</c> and what assets are created from:
    /// <code>
    /// [CreateAssetMenu(menuName = "MyGame/Currency Blueprint")]
    /// public sealed class CurrencyBlueprint : BagBlueprint&lt;CurrencyId&gt; { }
    /// </code>
    /// </summary>
    /// <remarks>
    /// The entry's resource field is typed <see cref="ResourceDefinition{TKey}"/>, so the
    /// Inspector refuses a def from another family outright — the scope cannot be authored
    /// wrong. That is the whole reason this class is generic; the bag itself only reads
    /// <see cref="BagBlueprint.Entries"/>.
    /// </remarks>
    public abstract class BagBlueprint<TKey> : BagBlueprint where TKey : struct, Enum
    {
        /// <summary>
        /// One in-scope resource plus this scope's policy for it: where it starts and how high
        /// it may go.
        /// </summary>
        [Serializable]
        public struct Entry
        {
            public ResourceDefinition<TKey> resource;

            /// <summary>Amount a fresh bag starts with. 0 = start empty. Bypasses the cap and the rule pipeline.</summary>
            public int initialAmount;

            /// <summary>Highest amount this bag may hold. 0 = unlimited.</summary>
            public int maxAmount;
        }

        [SerializeField] private Entry[] resources = Array.Empty<Entry>();

        // The untyped view the bag consumes. Converted once and cached, because a bag walks it
        // at construction and the id map walks it again — both per bag, and the authored list
        // does not change at runtime.
        private ResourceEntry[] _entries;

        private Dictionary<TKey, ResourceDefinition> _byKey;

        /// <inheritdoc />
        public override IReadOnlyList<ResourceEntry> Entries
        {
            get
            {
                if (_entries != null) return _entries;

                _entries = new ResourceEntry[resources?.Length ?? 0];
                if (resources == null) return _entries;
                for (int i = 0; i < resources.Length; i++)
                {
                    var e = resources[i];
                    _entries[i] = new ResourceEntry(e.resource, e.initialAmount, e.maxAmount);
                }

                return _entries;
            }
        }

        /// <summary>
        /// Returns the in-scope def whose <see cref="ResourceDefinition{TKey}.Key"/> equals
        /// <paramref name="key"/>. Throws <see cref="KeyNotFoundException"/> if there is none —
        /// the enum member exists but no def for it was authored into this scope.
        /// </summary>
        public ResourceDefinition ResolveByKey(TKey key)
        {
            if (TryResolveByKey(key, out var def)) return def;
            throw new KeyNotFoundException(
                $"[BagBlueprint:{name}] No ResourceDefinition<{typeof(TKey).Name}> with key '{key}' in Resources.");
        }

        /// <summary>Tries to find the in-scope def for <paramref name="key"/>.</summary>
        public bool TryResolveByKey(TKey key, out ResourceDefinition def)
            => GetOrBuildKeyMap().TryGetValue(key, out def);

        private Dictionary<TKey, ResourceDefinition> GetOrBuildKeyMap()
        {
            if (_byKey != null) return _byKey;

            _byKey = new Dictionary<TKey, ResourceDefinition>(resources?.Length ?? 0);
            if (resources == null) return _byKey;

            for (int i = 0; i < resources.Length; i++)
            {
                var def = resources[i].resource;
                if (def != null) _byKey[def.Key] = def;
            }

            return _byKey;
        }

#if UNITY_EDITOR
        // The caches outlive a bag — they live as long as the loaded asset — so editing the
        // list in the Inspector has to drop them or lookups keep answering from the pre-edit
        // list for the rest of the session.
        private void OnValidate()
        {
            _entries = null;
            _byKey = null;
            InvalidateLookups();

            if (resources == null) return;
            for (int i = 0; i < resources.Length; i++)
            {
                var e = resources[i];
                if (e.resource == null) continue;

                if (e.initialAmount < 0 || e.maxAmount < 0)
                {
                    Debug.LogWarning($"[BagBlueprint:{name}] '{e.resource.Id}' has a negative amount; " +
                                     "0 means empty for initialAmount and unlimited for maxAmount.", this);
                }
                else if (e.maxAmount > 0 && e.initialAmount > e.maxAmount)
                {
                    // Worth saying out loud now that the two sit on the same row: seeding
                    // above the cap is legal (the seed bypasses clamping) but it starts the
                    // bag in a state the rules will then treat as overflowing.
                    Debug.LogWarning($"[BagBlueprint:{name}] '{e.resource.Id}' starts at {e.initialAmount} " +
                                     $"but caps at {e.maxAmount}. The seed bypasses the cap, so the bag " +
                                     "opens already over it.", this);
                }
            }
        }
#endif
    }
}
