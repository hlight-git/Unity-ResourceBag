using UnityEngine;

namespace Hlight.ResourceBag.Samples.WithRegen
{
    public enum RegenResourceId
    {
        Heart,
    }

    /// <summary>
    /// `ResourceDefinition` là abstract nên project nào cũng cần một lớp con như thế này.
    /// `Id` tự suy từ `Key`.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/02 Regen Def")]
    public sealed class RegenDef : ResourceDefinition<RegenResourceId> { }
}
