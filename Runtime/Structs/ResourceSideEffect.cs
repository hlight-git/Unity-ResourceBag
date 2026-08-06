namespace Hlight.ResourceBag
{
    /// <summary>
    /// Secondary change emitted by a rule. Re-enters the pipeline at <c>depth + 1</c>,
    /// bounded by <see cref="ResourceBag.MaxSideEffectDepth"/>.
    /// </summary>
    public readonly struct ResourceSideEffect
    {
        public readonly ResourceDefinition Resource;

        /// <summary>Signed change — the sign decides credit or debit, so no separate flag.</summary>
        public readonly int Delta;

        public readonly string Reason;

        public ResourceSideEffect(ResourceDefinition resource, int delta, string reason)
        {
            Resource = resource;
            Delta = delta;
            Reason = reason;
        }
    }
}
