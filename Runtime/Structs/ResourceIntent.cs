namespace Hlight.ResourceBag
{
    /// <summary>
    /// Mutable struct threaded through the change pipeline by <c>ref</c>. One intent
    /// covers both directions: <see cref="Delta"/> is signed.
    /// </summary>
    /// <remarks>
    /// Rules assign fields directly (<c>intent.Delta *= 2;</c>). The 1.x fluent
    /// <c>With*</c> helpers are gone — they mutated <c>this</c> and also returned a
    /// copy, so on a <c>ref</c> struct <c>intent.WithAmount(x)</c> and
    /// <c>intent = intent.WithAmount(x)</c> both compiled but meant different things.
    /// <see cref="WithSubstitute"/> survives because it sets three fields at once, but
    /// returns <c>void</c> so it cannot be called in the assigning form that caused the
    /// original ambiguity.
    /// </remarks>
    public struct ResourceIntent
    {
        public ResourceDefinition Resource;

        /// <summary>Signed change. Positive credits, negative debits.</summary>
        public int Delta;

        public string Reason;

        /// <summary>
        /// Skip the primary mutation; side effects still run. On a debit this reports
        /// success without deducting — the free-pass case.
        /// </summary>
        public bool SkipPrimary;

        /// <summary>Only consulted when <see cref="Delta"/> is negative.</summary>
        public SpendOutcome Outcome;

        public ResourceDefinition SubstituteWith;
        public int SubstituteAmount;

        public ResourceIntent(ResourceDefinition resource, int delta, string reason)
        {
            Resource = resource;
            Delta = delta;
            Reason = reason;
            SkipPrimary = false;
            Outcome = SpendOutcome.Continue;
            SubstituteWith = null;
            SubstituteAmount = 0;
        }

        /// <summary>Redirect this debit onto another resource. Sets outcome and both substitute fields.</summary>
        public void WithSubstitute(ResourceDefinition resource, int amount)
        {
            Outcome = SpendOutcome.Substitute;
            SubstituteWith = resource;
            SubstituteAmount = amount;
        }
    }
}
