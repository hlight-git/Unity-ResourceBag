using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag.Samples.CrossBag
{
    /// <summary>
    /// Rule tự viết: nhân đôi Add của owner khi bag "event" còn vé.
    /// </summary>
    /// <remarks>
    /// Resolve lần đầu dùng chứ không trong OnAttach — OnAttach chạy trong constructor của
    /// ResourceBag, nên dependency register muộn hơn sẽ thành null vĩnh viễn. Xem README.
    /// </remarks>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag Samples/04 Event Buff Rule")]
    public class EventBuffRule : ResourceRule
    {
        [SerializeField] private ResourceDefinition eventTicket;
        [SerializeField] private int multiplier = 2;

        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => owner != null ? new Instance(this, owner, bag) : null;

        private sealed class Instance : AttachedRule
        {
            private readonly EventBuffRule _cfg;
            private ResourceBag _eventBag;

            public Instance(EventBuffRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            public override void OnAttach()
            {
                var locator = Bag.Locator;
                if (locator == null) return;
                locator.TryProvide(out _eventBag, "event");
            }

            public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
            {
                if (intent.Resource != Owner) return;
                if (_eventBag == null || _cfg.eventTicket == null) return;
                if (_eventBag.GetAmount(_cfg.eventTicket) < 1) return;
                intent.Delta *= _cfg.multiplier;
            }
        }
    }
}
