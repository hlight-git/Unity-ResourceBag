using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hlight.ResourceBag.Rules
{
    /// <summary>
    /// Stateless Add-time bundle expander: when an Add of the owner resource (i.e., the
    /// "pack" def whose <see cref="ResourceDefinition.Rules"/> contains this rule) enters
    /// the pipeline, each <c>entries[i]</c> is emitted as a side effect (amount
    /// multiplied by the primary intent amount) with reason
    /// <see cref="RuleReasons.ResolveBundle"/>. The primary intent is then skipped so the
    /// bundle resource itself is not added.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Resource Bag/Rules/Bundle Resolve", fileName = "BundleResolveRule", order = 12)]
    public class BundleResolveRule : ResourceRule
    {
        /// <summary>Component entries the bundle expands into.</summary>
        [SerializeField] private BundleEntry[] entries = Array.Empty<BundleEntry>();

        /// <summary>Serializable (resource, amount) tuple used by <see cref="BundleResolveRule"/>.</summary>
        [Serializable]
        public class BundleEntry
        {
            /// <summary>Component resource to be added.</summary>
            public ResourceDefinition resource;

            /// <summary>Base amount per single bundle unit.</summary>
            public int amount = 1;
        }

        /// <inheritdoc />
        public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
            => owner != null ? new Instance(this, owner, bag) : null;

        private sealed class Instance : AttachedRule
        {
            private readonly BundleResolveRule _cfg;

            public Instance(BundleResolveRule cfg, ResourceDefinition owner, ResourceBag bag) : base(cfg, owner, bag)
            {
                _cfg = cfg;
            }

            public override void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects)
            {
                if (intent.Resource != Owner || _cfg.entries == null) return;
                for (int i = 0; i < _cfg.entries.Length; i++)
                {
                    var e = _cfg.entries[i];
                    if (e == null || e.resource == null || e.amount == 0) continue;

                    // Compute in long and clamp back into int range: an unguarded int*int
                    // can overflow into a negative product, and ChangeInternal routes a
                    // negative side-effect Delta as a debit — turning a bundle Add into a
                    // spend. Clamping preserves sign (a same-sign overflow clamps to the
                    // matching MaxValue/MinValue) rather than flipping it.
                    var product = (long)e.amount * intent.Delta;
                    if (product > int.MaxValue) product = int.MaxValue;
                    else if (product < int.MinValue) product = int.MinValue;

                    sideEffects.Add(new ResourceSideEffect(e.resource, (int)product,
                        RuleReasons.ResolveBundle));
                }
                intent.SkipPrimary = true;
            }
        }
    }
}
