using Hlight.ResourceBag;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.WithRegen
{
    /// <summary>
    /// One blueprint class per key family. Being typed on <see cref="RegenResourceId"/> is what makes the
    /// Inspector refuse a def from any other family, and what lets
    /// <c>ResourceBag&lt;RegenResourceId&gt;</c> take this asset and nothing else.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Samples/With Regen Blueprint", fileName = "RegenBlueprint")]
    public sealed class RegenBlueprint : BagBlueprint<RegenResourceId> { }
}
