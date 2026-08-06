using UnityEngine;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Abstract base for enum-keyed resource definitions. Derives
    /// <see cref="ResourceDefinition.Id"/> from the enum key so the concrete subclass
    /// is a single line:
    /// <code>
    /// public sealed class CurrencyDef : ResourceDefinition&lt;CurrencyId&gt; { }
    /// </code>
    /// The blueprint auto-discovers instances at first <see cref="BagBlueprint.ResolveByKey{TKey}"/>
    /// call — no per-bag registration boilerplate needed.
    /// </summary>
    public abstract class ResourceDefinition<TKey> : ResourceDefinition
        where TKey : struct, System.Enum
    {
        [SerializeField] private TKey key;

        /// <summary>
        /// Enum key that identifies this resource within its domain. This value doubles as
        /// the persistence key (see <see cref="ResourceDefinition.Id"/>), so renaming an enum
        /// member orphans any save data written under the old name.
        /// </summary>
        public TKey Key => key;

        /// <inheritdoc />
        public override string Id => key.ToString();
    }
}
