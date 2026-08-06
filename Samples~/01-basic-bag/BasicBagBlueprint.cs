using Hlight.ResourceBag;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.BasicBag
{
    /// <summary>
    /// One blueprint class per key family. Being typed on <see cref="BasicBagResourceId"/> is what makes the
    /// Inspector refuse a def from any other family, and what lets
    /// <c>ResourceBag&lt;BasicBagResourceId&gt;</c> take this asset and nothing else.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Samples/Basic Bag Blueprint", fileName = "BasicBagBlueprint")]
    public sealed class BasicBagBlueprint : BagBlueprint<BasicBagResourceId> { }
}
