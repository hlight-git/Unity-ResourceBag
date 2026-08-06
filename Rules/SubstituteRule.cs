using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag.Rules
{
    /// <summary>
    /// Stateless Spend-time substitute: a fallback, not a preference. When a TrySpend of
    /// the owner resource cannot be covered by the owner's own balance, and the shortfall can
    /// be covered by <c>substitute</c> (at <c>substituteRatio</c>, rounded per
    /// <c>rounding</c>), the intent is rewritten via <see cref="ResourceIntent.WithSubstitute"/>.
    /// If the owner resource can afford the spend on its own, the substitute is never touched.
    /// Down-stream pipeline applies the substitute deduction.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Rules/Substitute", fileName = "SubstituteRule", order = 13)]
    public class SubstituteRule : ResourceRule
    {
        /// <summary>Resource that may replace the owner resource.</summary>
        [SerializeField] private ResourceDefinition substitute;

        /// <summary>Ratio applied to spend amount to compute substitute amount.</summary>
        [SerializeField] private float substituteRatio = 1f;

        /// <summary>Rounding strategy applied to <c>amount * substituteRatio</c>.</summary>
        [SerializeField] private RoundingMode rounding = RoundingMode.Ceil;

        /// <inheritdoc />
        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => owner != null ? new Instance(this, owner, bag) : null;

        private sealed class Instance : AttachedRule
        {
            private readonly SubstituteRule _cfg;

            public Instance(SubstituteRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            public override void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
            {
                if (intent.Outcome != SpendOutcome.Continue) return;   // another rule decided
                if (intent.Resource != Owner || _cfg.substitute == null) return;

                var amount = -intent.Delta;

                // Fallback, not preference: never spend the substitute while the owner
                // resource can cover the cost itself.
                if (Bag.HasAtLeast(Owner, amount)) return;

                var subAmount = RoundingHelper.Round(amount * _cfg.substituteRatio, _cfg.rounding);
                if (subAmount <= 0) return;
                if (!Bag.HasAtLeast(_cfg.substitute, subAmount)) return;
                intent.WithSubstitute(_cfg.substitute, subAmount);
            }
        }
    }
}
