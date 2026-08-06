using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Key-agnostic half of an authored scope: the extra rules, and the persistence lookup
    /// every bag needs regardless of how its resources are keyed. The authored resource list
    /// lives on <see cref="BagBlueprint{TKey}"/>, which is what projects subclass.
    /// </summary>
    /// <remarks>
    /// The split exists because <see cref="ResourceBag"/> only ever iterates entries and maps
    /// saved ids back onto defs — neither needs the key type — while authoring wants the field
    /// typed so the Inspector refuses a def from another family.
    /// <para>
    /// Per-resource rules (each def's <see cref="ResourceDefinition.Rules"/>) are always
    /// honored at construction. Use <see cref="ExtraRules"/> for cross-cutting rules that
    /// don't naturally live on a single def.
    /// </para>
    /// </remarks>
    public abstract class BagBlueprint : ScriptableObject
    {
        [SerializeField] private ResourceRule[] extraRules = Array.Empty<ResourceRule>();

        /// <summary>Extra rules attached AFTER per-resource rules. Owner is null for these.</summary>
        public ResourceRule[] ExtraRules => extraRules;

        /// <summary>
        /// In-scope resources with this scope's policy for each. Supplied by the typed
        /// subclass; <see cref="ResourceBag"/> reads nothing else about the authored list.
        /// </summary>
        public abstract IReadOnlyList<ResourceEntry> Entries { get; }

        // ------------------------------- Lookups -------------------------------
        //
        // Lives here rather than on the bag because it is a pure function of the authored
        // list — it reads nothing about a bag's runtime state. It also means N bags built from
        // one blueprint share one map, and the duplicate-Id check runs once per blueprint
        // instead of once per bag construction.

        private Dictionary<string, ResourceDefinition> _byId;

        /// <summary>
        /// Builds the Id lookup now if it has not been built, which is also when authoring
        /// problems get reported. <see cref="ResourceBag"/> calls this at construction.
        /// </summary>
        /// <remarks>
        /// Without this the check would be purely lazy, and a blueprint whose bag never loads
        /// a snapshot would never build the map — so a duplicate Id, which matters precisely
        /// for persistence, could go unreported for a whole session and first surface as two
        /// resources quietly sharing one saved value.
        /// </remarks>
        public void EnsureLookupsBuilt() => GetOrBuildIdMap();

        /// <summary>
        /// Finds the in-scope def whose <see cref="ResourceDefinition.Id"/> matches
        /// <paramref name="id"/>. Used to map persisted keys — and remote-config rows — back
        /// onto defs.
        /// </summary>
        public bool TryGetById(string id, out ResourceDefinition def)
        {
            if (string.IsNullOrEmpty(id))
            {
                def = null;
                return false;
            }

            return GetOrBuildIdMap().TryGetValue(id, out def);
        }

        /// <summary>Drops the cached lookups. A subclass calls this when its authored list changes.</summary>
        protected void InvalidateLookups() => _byId = null;

        private Dictionary<string, ResourceDefinition> GetOrBuildIdMap()
        {
            if (_byId != null) return _byId;

            var entries = Entries;
            _byId = new Dictionary<string, ResourceDefinition>(entries?.Count ?? 0);
            if (entries == null) return _byId;

            for (int i = 0; i < entries.Count; i++)
            {
                var def = entries[i].Resource;
                if (def == null) continue;

                // Two different defs sharing an Id collide in persistence: SaveTo writes one
                // over the other and loading feeds the same value into both. Easy to hit,
                // because Id comes from Key — two defs with the same Key produce the same Id.
                if (_byId.TryGetValue(def.Id, out var clash) && clash != def)
                {
                    Debug.LogWarning(
                        $"[BagBlueprint:{name}] Duplicate ResourceDefinition Id '{def.Id}' on two " +
                        $"different defs ('{clash.name}' and '{def.name}'). Persistence keys will collide.");
                }

                _byId[def.Id] = def;
            }

            return _byId;
        }
    }
}
