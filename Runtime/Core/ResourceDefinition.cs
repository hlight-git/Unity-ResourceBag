using System;
using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// What a resource <i>is</i>: identity, presentation, and the rules composed onto it.
    /// Authored as a ScriptableObject asset.
    /// </summary>
    /// <remarks>
    /// Deliberately holds no per-scope policy. How much of this resource a given bag may hold
    /// lives on the blueprint entry's <c>maxAmount</c>, next to that entry's
    /// starting amount, because a cap is a decision the scope makes rather than a property
    /// of the resource — the same Heart can cap at 5 in a player bag and 10 in an event bag.
    /// <para>
    /// For enum-keyed access, extend <see cref="ResourceDefinition{TKey}"/> instead — it
    /// derives <see cref="Id"/> from the key automatically.
    /// </para>
    /// </remarks>
    public abstract class ResourceDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private ResourceRule[] rules = Array.Empty<ResourceRule>();

        /// <summary>Stable unique identifier used as a persistence key. Override in each concrete subclass.</summary>
        public abstract string Id { get; }

        /// <summary>Human-readable display name.</summary>
        public string DisplayName => displayName;

        /// <summary>Optional icon sprite.</summary>
        public Sprite Icon => icon;

        /// <summary>Rules composed onto this resource (auto-attached to any bag that includes this def).</summary>
        public ResourceRule[] Rules => rules;
    }
}
