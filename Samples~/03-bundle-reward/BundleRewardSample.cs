using UnityEngine;

namespace Hlight.ResourceBag.Samples.BundleReward
{
    /// <summary>
    /// 03 — Bundle nở ra nhiều resource. Kéo `BundleRewardSample.prefab` vào scene, bấm Play.
    /// </summary>
    public class BundleRewardSample : MonoBehaviour
    {
        [SerializeField] private BundleBlueprint blueprint;

        private ResourceBag<BundleResourceId> _bag;

        private void Start()
        {
            _bag = new ResourceBag<BundleResourceId>("bundle-demo", blueprint);
            _bag.Changed += c => Debug.Log($"[Bag] {c.Resource?.Id} +{c.Delta} ({c.Reason})");

            _bag.Add(BundleResourceId.ChestReward, 1, "open_chest");

            Debug.Log($"Coins={_bag.GetAmount(BundleResourceId.Coin)} " +
                      $"Gems={_bag.GetAmount(BundleResourceId.Gem)} " +
                      $"Hammers={_bag.GetAmount(BundleResourceId.Hammer)} " +
                      $"Chests={_bag.GetAmount(BundleResourceId.ChestReward)}");
        }

        private void OnDestroy() => _bag?.Dispose();
    }
}
