using UnityEngine;

namespace Hlight.ResourceBag.Samples.CrossBag
{
    public enum CrossBagResourceId
    {
        Coin,
        EventTicket,
    }

    /// <summary>
    /// `ResourceDefinition` là abstract nên project nào cũng cần một lớp con như thế này.
    /// `Id` tự suy từ `Key`.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/04 Cross Bag Def")]
    public sealed class CrossBagDef : ResourceDefinition<CrossBagResourceId> { }
}
