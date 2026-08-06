using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Abstract ScriptableObject config for a resource rule. Acts as a factory: the bag
    /// calls <see cref="Attach"/> per (rule, owner) pair, which returns a runtime
    /// <see cref="AttachedRule"/> that owns its own state as ordinary fields.
    /// </summary>
    /// <remarks>
    /// The rule SO holds shared config (interval, ratio, etc.). The same SO can be placed
    /// in multiple <see cref="ResourceDefinition.Rules"/> lists and the bag will produce a
    /// separate <see cref="AttachedRule"/> instance per owner — each with its own state.
    /// </remarks>
    public abstract class ResourceRule : ScriptableObject
    {
        /// <summary>
        /// Factory: produce a runtime <see cref="AttachedRule"/> for this (rule, owner, bag)
        /// triple. <paramref name="owner"/> is the def whose <see cref="ResourceDefinition.Rules"/>
        /// listed this rule, or <c>null</c> when attached via blueprint extraRules.
        /// </summary>
        public abstract AttachedRule Attach(ResourceBag bag, ResourceDefinition owner);
    }
}
