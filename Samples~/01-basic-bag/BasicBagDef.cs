using UnityEngine;

namespace Hlight.ResourceBag.Samples.BasicBag
{
    public enum BasicBagResourceId
    {
        Heart,
        Coin,
    }

    /// <summary>
    /// `ResourceDefinition` là abstract, nên project nào cũng cần một lớp con như thế này.
    /// `Id` tự suy từ `Key`. Attribute là thứ cho phép tạo resource từ Project window.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/01 Basic Bag Def")]
    public sealed class BasicBagDef : ResourceDefinition<BasicBagResourceId> { }
}
