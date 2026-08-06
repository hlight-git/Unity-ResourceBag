using System;
using System.Collections.Generic;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Everything an <see cref="ResourceBag"/> persists: amounts, rule state, and — when
    /// the bag owns its clock — clock state. Pass it to the bag's constructor to
    /// restore, and to <see cref="ResourceBag.SaveTo"/> to capture.
    /// </summary>
    /// <remarks>
    /// A plain DTO, not a persistence layer: the package never performs IO. Hand it to
    /// whatever the project already uses.
    /// <para>
    /// Entries are <see cref="List{T}"/> of serializable structs rather than
    /// dictionaries, because Unity's <c>JsonUtility</c> cannot serialize a dictionary
    /// and support elsewhere varies. Lists of serializable structs work everywhere,
    /// which matters when an outside package supplies the serializer.
    /// </para>
    /// </remarks>
    [Serializable]
    public class BagSnapshot
    {
        /// <summary>Schema version this build writes.</summary>
        public const int CurrentVersion = 1;

        /// <summary>One resource amount, keyed by <see cref="ResourceDefinition.Id"/>.</summary>
        [Serializable]
        public struct AmountEntry
        {
            public string id;
            public int amount;
        }

        /// <summary>
        /// One stateful rule's persisted state, keyed <c>"{ownerId}:{AttachedRule.StateKey}"</c>.
        /// The value is whatever the rule's <see cref="AttachedRule.SaveState"/> produced —
        /// JSON for an <see cref="AttachedRule{TState}"/>, or any text a rule encodes itself.
        /// </summary>
        [Serializable]
        public struct RuleStateEntry
        {
            public string key;
            public string value;
        }

        /// <summary>Schema version of this instance. Written by <see cref="ResourceBag.SaveTo"/>.</summary>
        public int Version = CurrentVersion;

        public List<AmountEntry> Amounts = new List<AmountEntry>();

        public List<RuleStateEntry> RuleState = new List<RuleStateEntry>();

        /// <summary>Clock state. Untouched when the bag was given a clock to use.</summary>
        public BagClockState Clock;
    }
}
