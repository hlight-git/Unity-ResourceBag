using System;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// One row of a resource list that arrives as data rather than as authored assets — an
    /// event reward, an IAP payload, a crafting cost. Keyed by
    /// <see cref="ResourceDefinition.Id"/> (which for a keyed def is the enum member name), so
    /// remote config can name resources without holding asset references:
    /// <code>
    /// { "id": "Coin",  "amount": 100, "amountMax": 300 }   // random 100..300
    /// { "id": "Gem",   "amount": 5 }                       // fixed 5
    /// </code>
    /// </summary>
    /// <remarks>
    /// Deliberately a name and not an enum ordinal: reordering an enum member would silently
    /// remap every reward already published in config. The name is the same string persistence
    /// uses, so config and save data speak one vocabulary.
    /// <para>
    /// A typo in config is a runtime warning, not a compile error. That is inherent — config is
    /// data — and <see cref="ResourceBagConfigExtensions"/> is where it gets reported.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct ResourceAmount
    {
        /// <summary><see cref="ResourceDefinition.Id"/> of the resource — the enum member name for a keyed def.</summary>
        public string id;

        /// <summary>The amount, or the lower bound when <see cref="amountMax"/> is higher.</summary>
        public int amount;

        /// <summary>Upper bound for a random amount. 0 (or ≤ <see cref="amount"/>) means fixed.</summary>
        public int amountMax;

        public ResourceAmount(string id, int amount, int amountMax = 0)
        {
            this.id = id;
            this.amount = amount;
            this.amountMax = amountMax;
        }

        /// <summary>True when this row asks for a random amount rather than a fixed one.</summary>
        public bool IsRandom => amountMax > amount;
    }
}
