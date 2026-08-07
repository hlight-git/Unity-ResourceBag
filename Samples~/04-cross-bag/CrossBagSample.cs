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
            _eventBag = new ResourceBag<CrossBagResourceId>("event", eventBlueprint);
            _eventBag.Add(CrossBagResourceId.EventTicket, 1, "grant_ticket");

            // Nêu đích danh loại rule mình nuôi — đọc dòng này là biết ai cấp cho ai.
            // Đẩy Func chứ không đẩy instance, nên thứ tự dựng hai bag không còn quan trọng.
            var injector = new TinyInjector();
            injector.Resolve<EventBuffRule.Instance>(r => r.EventBag = () => _eventBag);

            _playerBag = new ResourceBag<CrossBagResourceId>("player", playerBlueprint, injector: injector);
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

        /// <summary>
        /// Injector tối giản cho sample. Project thật bọc DependencyInjector của mình vào
        /// IBagInjector — một method forward, xem README.
        /// </summary>
        private sealed class TinyInjector : IBagInjector
        {
            private readonly Dictionary<Type, Action<object>> _resolvers = new();

            public void Resolve<T>(Action<T> push) where T : class
                => _resolvers[typeof(T)] = target => push((T)target);

            public void Inject(object target)
            {
                if (target != null && _resolvers.TryGetValue(target.GetType(), out var push))
                    push(target);
            }
        }
    }
}
