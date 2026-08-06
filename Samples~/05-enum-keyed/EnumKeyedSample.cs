using UnityEngine;

namespace Hlight.ResourceBag.Samples.EnumKeyed
{
    /// <summary>
    /// 05 — Gọi bằng enum. Kéo `EnumKeyedSample.prefab` vào scene, bấm Play.
    /// MonoBehaviour này giữ 0 reference ResourceDefinition — chỉ giữ blueprint.
    /// </summary>
    public class EnumKeyedSample : MonoBehaviour
    {
        [SerializeField] private CurrencyBlueprint blueprint;

        private ResourceBag<CurrencyId> _bag;

        private void Start()
        {
            _bag = new ResourceBag<CurrencyId>("enum-demo", blueprint);
            _bag.Changed += c => Debug.Log($"[Bag] {c.Resource?.Id} {c.OldAmount}→{c.NewAmount} ({c.Reason})");

            // Add / Spend / Get — all by enum value.
            _bag.Add(CurrencyId.Coin, 250, "quest_reward");          // 0 → 250
            _bag.Add(CurrencyId.Gem, 5, "daily_login");              // 0 → 5
            _bag.Add(CurrencyId.Heart, 10, "regen_test");            // 0 → 5 (clamped at maxAmount=5)

            bool bought = _bag.TrySpend(CurrencyId.Coin, 100, "buy_skin"); // 250 → 150
            Debug.Log($"Bought skin? {bought}. Coins={_bag.GetAmount(CurrencyId.Coin)} " +
                      $"Gems={_bag.GetAmount(CurrencyId.Gem)} Hearts={_bag.GetAmount(CurrencyId.Heart)}");

            // HasAtLeast / SetMaxAmount also enum-keyed.
            Debug.Log($"Has at least 50 coin? {_bag.HasAtLeast(CurrencyId.Coin, 50)}");
            _bag.SetMaxAmount(CurrencyId.Heart, 10); // remote-config bump
            Debug.Log($"Heart max is now {_bag.GetMaxAmount(CurrencyId.Heart)}");

            // Cần def rời thì hỏi blueprint, không hỏi bag: tra cứu theo key là hàm thuần
            // của danh sách resource đã author, không liên quan state runtime của bag.
            ResourceDefinition coinDef = _bag.Blueprint.ResolveByKey(CurrencyId.Coin);
            Debug.Log($"Resolved CoinDef.Id={coinDef.Id} DisplayName={coinDef.DisplayName}");

            // TryResolveByKey để tra an toàn — trả false nếu blueprint không có key đó.
            if (!_bag.Blueprint.TryResolveByKey(CurrencyId.Heart, out _))
                Debug.LogWarning("Heart def missing from blueprint!");
        }

        private void OnDestroy() => _bag?.Dispose();
    }
}
