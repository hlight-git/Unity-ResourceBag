using System.Collections.Generic;

namespace Hlight.ResourceBag
{
    /// <summary>
    /// Per-bag runtime instance of an <see cref="ResourceRule"/>. Holds the (rule, owner,
    /// bag) triple plus any mutable rule state as ordinary fields — no <c>object</c>
    /// state dictionaries, no casts at the call site.
    /// </summary>
    /// <remarks>
    /// Each <see cref="ResourceRule.Attach"/> call produces exactly one AttachedRule. The
    /// bag drives this instance via the virtual hooks; subclasses override the ones
    /// they care about and ignore the rest.
    /// </remarks>
    public abstract class AttachedRule
    {
        /// <summary>SO the instance was created from. Used by the bag for idempotent attach + targeted detach.</summary>
        public ResourceRule Source { get; }

        /// <summary>
        /// ResourceDefinition this attachment is bound to — i.e., the def whose
        /// <see cref="ResourceDefinition.Rules"/> list contained <see cref="Source"/>.
        /// <c>null</c> when the rule was attached via <see cref="BagBlueprint.ExtraRules"/>
        /// or directly through <see cref="ResourceBag.AttachRule"/> without an owner.
        /// </summary>
        public ResourceDefinition Owner { get; }

        /// <summary>Bag this instance lives in.</summary>
        protected ResourceBag Bag { get; }

        protected AttachedRule(ResourceRule source, ResourceDefinition owner, ResourceBag bag)
        {
            Source = source;
            Owner = owner;
            Bag = bag;
        }

        /// <summary>Invoked once after the instance is added to the bag. MUST NOT mutate bag amounts.</summary>
        public virtual void OnAttach() { }

        /// <summary>
        /// Invoked once when the instance is being removed (<see cref="ResourceBag.DetachRule"/>
        /// or <see cref="ResourceBag.Dispose"/>). MUST NOT call <c>Bag.Add</c> / <c>Bag.TrySpend</c>:
        /// during Dispose the bag is tearing down — mutations are either ignored (post-Dispose)
        /// or interleave with detach order. Use this hook for unsubscribing events /
        /// cancelling tasks only.
        /// </summary>
        public virtual void OnDetach() { }

        /// <summary>
        /// Invoked once per <see cref="ResourceBag.Tick"/>. <paramref name="now"/> is
        /// <see cref="ResourceBag.Clock"/> read once for the whole tick, so every rule in a
        /// tick sees the same instant.
        /// </summary>
        public virtual void OnTick(double now) { }

        /// <summary>
        /// Key suffix under which this attachment's state is persisted, or <c>null</c>
        /// (default) for a stateless rule. The bag scopes it by owner
        /// (<c>"{Owner.Id}:{StateKey}"</c>), so the same rule SO on two defs stores
        /// separately.
        /// </summary>
        public virtual string StateKey => null;

        /// <summary>
        /// The value to persist, or <c>null</c> to write nothing. Derive from
        /// <see cref="AttachedRule{TState}"/> to get this — and its counterpart — from a
        /// plain state class instead of writing them by hand.
        /// </summary>
        public virtual string SaveState() => null;

        /// <summary>
        /// Restore from what <see cref="SaveState"/> produced. Runs inside the bag's
        /// constructor, after amounts are restored, and only when a row for
        /// <see cref="StateKey"/> exists — so a rule that was never persisted keeps
        /// whatever its constructor set.
        /// </summary>
        public virtual void LoadState(string value) { }

        /// <summary>
        /// Invoked for a credit (<see cref="ResourceIntent.Delta"/> &gt; 0). May mutate
        /// <paramref name="intent"/> and append to <paramref name="sideEffects"/>.
        /// </summary>
        public virtual void OnBeforeAdd(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects) { }

        /// <summary>
        /// Invoked for a debit (<see cref="ResourceIntent.Delta"/> &lt; 0). May mutate
        /// <paramref name="intent"/> and append to <paramref name="sideEffects"/>.
        /// Return early when <see cref="ResourceIntent.Outcome"/> is not
        /// <see cref="SpendOutcome.Continue"/> — another rule has already decided.
        /// </summary>
        public virtual void OnBeforeSpend(ref ResourceIntent intent, List<ResourceSideEffect> sideEffects) { }
    }
}
