using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.CrossBag
{
    /// <summary>
    /// 04 — Bag đọc state của bag khác. Kéo `CrossBagSample.prefab` vào scene, bấm Play.
    /// </summary>
    public class CrossBagSample : MonoBehaviour
    {
        [SerializeField] private CrossBagBlueprint playerBlueprint;
        [SerializeField] private CrossBagBlueprint eventBlueprint;

        private ResourceBag<CrossBagResourceId> _playerBag;
        private ResourceBag<CrossBagResourceId> _eventBag;

        private void Start()
        {
            // Dựng bag event trước rồi register, để rule của bag player tìm được nó.
            _eventBag = new ResourceBag<CrossBagResourceId>("event", eventBlueprint);
            _eventBag.Add(CrossBagResourceId.EventTicket, 1, "grant_ticket");

            var locator = new TinyLocator();
            // Đăng ký bằng kiểu gốc, không phải ResourceBag<CrossBagResourceId>: rule chỉ thấy
            // bag của nó là ResourceBag (nó không thể biết họ key), nên nó hỏi locator bằng
            // kiểu đó. Nếu đăng ký bằng kiểu generic thì lời hỏi trượt — và trượt im lặng.
            locator.Register<ResourceBag>(_eventBag, "event");

            _playerBag = new ResourceBag<CrossBagResourceId>("player", playerBlueprint, locator: locator);
            _playerBag.Changed += c => Debug.Log(
                $"[Player] {c.Resource?.Id} +{c.Delta} ({c.Reason})  total={c.NewAmount}");

            _playerBag.Add(CrossBagResourceId.Coin, 100, "quest_reward");        // → +200, đang có vé

            _eventBag.TrySpend(CrossBagResourceId.EventTicket, 1, "consume_ticket");
            _playerBag.Add(CrossBagResourceId.Coin, 100, "quest_reward_2");      // → +100, hết vé
        }

        private void OnDestroy()
        {
            _playerBag?.Dispose();
            _eventBag?.Dispose();
        }

        /// <summary>Locator tối giản cho sample. Project thật dùng locator của bạn — xem README.</summary>
        private sealed class TinyLocator : IBagServiceLocator
        {
            private readonly Dictionary<(Type, string), object> _byKey = new();

            public void Register<T>(T instance, string key = null) where T : class
                => _byKey[(typeof(T), key ?? string.Empty)] = instance;

            public bool TryProvide<T>(out T value, string key = null) where T : class
            {
                if (_byKey.TryGetValue((typeof(T), key ?? string.Empty), out var raw)
                    && raw is T cast) { value = cast; return true; }
                value = null; return false;
            }
        }
    }
}
