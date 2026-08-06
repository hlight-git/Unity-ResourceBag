using UnityEngine;

namespace Hlight.ResourceBag.Samples.WithRegen
{
    /// <summary>
    /// 02 — Hồi phục theo thời gian. Kéo `WithRegenSample.prefab` vào scene, bấm Play.
    /// </summary>
    public class WithRegenSample : MonoBehaviour
    {
        [SerializeField] private RegenBlueprint blueprint;

        // Thay cho save thật. Static nên chỉ sống trong một lần Play — đổi sang PlayerPrefs,
        // file, hoặc DataEntry<BagSnapshot> để sống qua lần tắt app.
        private static BagSnapshot s_saved;

        private ResourceBag<RegenResourceId> _bag;

        private void Start()
        {
            _bag = new ResourceBag<RegenResourceId>("regen-demo", blueprint, s_saved);
            _bag.Changed += c => Debug.Log($"[Regen] {c.Resource?.Id} {c.OldAmount}→{c.NewAmount} ({c.Reason})");
        }

        private void Update() => _bag?.Tick();

        private void OnDestroy()
        {
            s_saved ??= new BagSnapshot();
            _bag?.SaveTo(s_saved);
            _bag?.Dispose();
        }
    }
}
