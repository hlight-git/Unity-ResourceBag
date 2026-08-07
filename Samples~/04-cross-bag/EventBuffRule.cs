using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.CrossBag
{
    /// <summary>
    /// Rule tự viết: nhân đôi Add của owner khi bag "event" còn vé.
    /// </summary>
    /// <remarks>
    /// Dependency được đẩy vào trong Attach — đó là factory của rule và là code của project,
    /// nên nó là chỗ đúng để inject. Nhận <c>Func</c> chứ không nhận instance: Attach chạy
    /// trong constructor của ResourceBag, mà bag "event" ở đây có thể được dựng sau. Xem README.
    /// </remarks>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/04 Event Buff Rule")]
    public class EventBuffRule : ResourceRule
    {
        [SerializeField] private ResourceDefinition eventTicket;
        [SerializeField] private int multiplier = 2;

        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
        {
            if (owner == null) return null;

            var rule = new Instance(this, owner, bag);
            bag.Injector?.Inject(rule);
            return rule;
        }

        public sealed class Instance : AttachedRule
        {
            private readonly EventBuffRule _cfg;

            public Instance(EventBuffRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            /// <summary>Bag "event", đọc lúc dùng nên dựng sau bag này vẫn được.</summary>
            public Func<ResourceBag> EventBag { get; set; }

            public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
            {
                if (intent.Resource != Owner) return;
                var eventBag = EventBag?.Invoke();
                if (eventBag == null || _cfg.eventTicket == null) return;
                if (eventBag.GetAmount(_cfg.eventTicket) < 1) return;
                intent.Delta *= _cfg.multiplier;
            }
        }
    }
}
