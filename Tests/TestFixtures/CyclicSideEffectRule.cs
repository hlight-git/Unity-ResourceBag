using System.Collections.Generic;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Test-only rule: on every Add of <see cref="Trigger"/>, emits a side effect that
    /// re-adds <see cref="Trigger"/> with the same amount. Triggers the depth-limit
    /// guard in ResourceBag.Pipeline.
    /// </summary>
    internal sealed class CyclicSideEffectRule : ResourceRule
    {
        public ResourceDefinition Trigger;

        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => new Instance(this, owner, bag);

        private sealed class Instance : AttachedRule
        {
            private readonly CyclicSideEffectRule _cfg;

            public Instance(CyclicSideEffectRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
            {
                if (intent.Resource == null || intent.Resource != _cfg.Trigger) return;
                sideEffects.Add(new ResourceSideEffect(_cfg.Trigger, intent.Delta, "cycle"));
            }
        }
    }
}
