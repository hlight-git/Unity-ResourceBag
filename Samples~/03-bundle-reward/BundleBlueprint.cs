using Hlight.ResourceBag;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.BundleReward
{
    /// <summary>
    /// One blueprint class per key family. Being typed on <see cref="BundleResourceId"/> is what makes the
    /// Inspector refuse a def from any other family, and what lets
    /// <c>ResourceBag&lt;BundleResourceId&gt;</c> take this asset and nothing else.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Samples/Bundle Reward Blueprint", fileName = "BundleBlueprint")]
    public sealed class BundleBlueprint : BagBlueprint<BundleResourceId> { }
}
