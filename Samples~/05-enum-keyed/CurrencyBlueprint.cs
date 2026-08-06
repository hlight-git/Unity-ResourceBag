using Hlight.ResourceBag;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.EnumKeyed
{
    /// <summary>
    /// One blueprint class per key family. Being typed on <see cref="CurrencyId"/> is what makes the
    /// Inspector refuse a def from any other family, and what lets
    /// <c>ResourceBag&lt;CurrencyId&gt;</c> take this asset and nothing else.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Samples/Enum Keyed Blueprint", fileName = "CurrencyBlueprint")]
    public sealed class CurrencyBlueprint : BagBlueprint<CurrencyId> { }
}
