using UnityEngine;

namespace Hlight.ResourceBag.Samples.BundleReward
{
    public enum BundleResourceId
    {
        ChestReward,
        Coin,
        Gem,
        Hammer,
    }

    /// <summary>
    /// `ResourceDefinition` là abstract nên project nào cũng cần một lớp con như thế này.
    /// `Id` tự suy từ `Key`.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/03 Bundle Def")]
    public sealed class BundleDef : ResourceDefinition<BundleResourceId> { }
}
