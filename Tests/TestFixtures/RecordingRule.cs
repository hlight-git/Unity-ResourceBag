using System.Collections.Generic;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Test-only rule: records OnAttach / OnDetach / OnTick invocations and exposes
    /// counters for assertions.
    /// </summary>
    internal sealed class RecordingRule : ResourceRule
    {
        public int AttachCount;
        public int DetachCount;
        public int TickCount;
        public double LastNow;

        /// <summary>Position this instance's OnDetach landed at in the shared sequence, or -1 if never detached.</summary>
        public int DetachOrder = -1;

        // Shared across every RecordingRule instance in the process, so a test can
        // compare relative detach order across several SOs. Order is otherwise
        // unobservable — DetachCount alone cannot distinguish forward from reverse.
        private static int _detachSequence;

        /// <summary>Reset the shared sequence. Call at the start of a test that reads DetachOrder, so an earlier fixture cannot bleed in.</summary>
        public static void ResetSequence() => _detachSequence = 0;

        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => new Instance(this, owner, bag);

        private sealed class Instance : AttachedRule
        {
            private readonly RecordingRule _cfg;

            public Instance(RecordingRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            public override void OnAttach() { _cfg.AttachCount++; }

            public override void OnDetach()
            {
                _cfg.DetachCount++;
                _cfg.DetachOrder = _detachSequence++;
            }

            public override void OnTick(double now) { _cfg.TickCount++; _cfg.LastNow = now; }
        }
    }
}
