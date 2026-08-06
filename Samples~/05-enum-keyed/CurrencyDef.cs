using UnityEngine;

namespace Hlight.ResourceBag.Samples.EnumKeyed
{
    /// <summary>
    /// One-line concrete subclass — Id derived automatically from <c>Key.ToString()</c>.
    /// Author one CurrencyDef.asset per <see cref="CurrencyId"/> value; set the Key
    /// field in the Inspector to the matching enum value.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/05 Currency Def")]
    public sealed class CurrencyDef : ResourceDefinition<CurrencyId> { }
}
