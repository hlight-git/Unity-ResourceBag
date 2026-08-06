using UnityEngine;

namespace Hlight.ResourceBag.Samples.BasicBag
{
    /// <summary>
    /// 01 — Bag cơ bản. Kéo `BasicBagSample.prefab` vào scene, bấm Play.
    /// Mọi tương tác đi qua enum, nên MonoBehaviour chỉ cần blueprint.
    /// </summary>
    public class BasicBagSample : MonoBehaviour
    {
        [SerializeField] private BasicBagBlueprint blueprint;

        private ResourceBag<BasicBagResourceId> _bag;

        private void Start()
        {
            _bag = new ResourceBag<BasicBagResourceId>("player", blueprint);

            _bag.Changed += c => Debug.Log(
                $"[Bag] {c.Resource?.Id} {c.OldAmount}→{c.NewAmount} ({c.Reason})");

            _bag.Add(BasicBagResourceId.Heart, 3, "test");
            _bag.Add(BasicBagResourceId.Coin, 100, "test");

            // Heart cap 5 trong blueprint, nên 10 chỉ vào được 2.
            _bag.Add(BasicBagResourceId.Heart, 10, "overflow_test");

            bool ok = _bag.TrySpend(BasicBagResourceId.Heart, 1, "use");
            Debug.Log($"Spent 1 heart? {ok}. " +
                      $"Hearts={_bag.GetAmount(BasicBagResourceId.Heart)} " +
                      $"Coins={_bag.GetAmount(BasicBagResourceId.Coin)}");
        }

        private void OnDestroy() => _bag?.Dispose();
    }
}
