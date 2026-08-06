using Hlight.ResourceBag;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.CrossBag
{
    /// <summary>
    /// One blueprint class per key family. Being typed on <see cref="CrossBagResourceId"/> is what makes the
    /// Inspector refuse a def from any other family, and what lets
    /// <c>ResourceBag&lt;CrossBagResourceId&gt;</c> take this asset and nothing else.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Samples/Cross Bag Blueprint", fileName = "CrossBagBlueprint")]
    public sealed class CrossBagBlueprint : BagBlueprint<CrossBagResourceId> { }
}
