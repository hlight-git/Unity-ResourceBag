using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag.Rules
{
    /// <summary>
    /// Stateless Add-time overflow handler: when an Add of the owner resource would exceed
    /// the bag's cap, the surplus is converted to <c>convertTo</c> at
    /// <c>conversionRatio</c> (rounded per <c>rounding</c>) and emitted as a side
    /// effect with reason <see cref="BagReasons.Overflow"/>. The primary intent's
    /// amount is shrunk so it lands exactly at cap. Only the incoming amount can ever
    /// convert — even when the balance already sits above the cap (e.g. seeded via
    /// <c>initialAmount</c> or after the cap was lowered below the current balance),
    /// the overflow is bounded by <c>intent.Delta</c> and the rest of the balance is
    /// left untouched. If cap is 0 (unlimited) or the Add would not exceed cap, no-op.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Rules/Overflow Convert", fileName = "OverflowConvertRule", order = 11)]
    public class OverflowConvertRule : ResourceRule
    {
        /// <summary>Resource that absorbs the converted overflow.</summary>
        [SerializeField] private ResourceDefinition convertTo;

        /// <summary>Ratio applied to the overflow units before rounding.</summary>
        [SerializeField] private float conversionRatio = 1f;

        /// <summary>Rounding strategy applied to <c>overflow * conversionRatio</c>.</summary>
        [SerializeField] private RoundingMode rounding = RoundingMode.Ceil;

        /// <inheritdoc />
        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => owner != null ? new Instance(this, owner, bag) : null;

        private sealed class Instance : AttachedRule
        {
            private readonly OverflowConvertRule _cfg;

            public Instance(OverflowConvertRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
            {
                if (intent.Resource != Owner || _cfg.convertTo == null) return;
                var cap = Bag.GetMaxAmount(Owner);
                if (cap <= 0) return;
                var current = Bag.GetAmount(Owner);
                var wouldHave = current + intent.Delta;
                if (wouldHave <= cap) return;
                var overflow = wouldHave - cap;
                if (overflow > intent.Delta) overflow = intent.Delta;   // never convert more than came in
                var kept = intent.Delta - overflow;
                var convertedInt = RoundingHelper.Round(overflow * _cfg.conversionRatio, _cfg.rounding);
                if (convertedInt > 0)
                    sideEffects.Add(new ResourceSideEffect(_cfg.convertTo, convertedInt, BagReasons.Overflow));
                intent.Delta = kept;
            }
        }
    }
}
