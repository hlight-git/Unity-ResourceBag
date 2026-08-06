namespace Hlight.ResourceBag
{
    /// <summary>
    /// Immutable record of one amount change, emitted via <see cref="ResourceBag.Changed"/>.
    /// </summary>
    /// <remarks>
    /// Carries no timestamp: nothing in the package read one, and minting a
    /// <see cref="System.DateTime"/> per coin gained is pure cost. Stamp it in the subscriber
    /// if the project needs it.
    /// </remarks>
    public readonly struct ResourceChange
    {
        public readonly ResourceDefinition Resource;
        public readonly int Delta;
        public readonly int OldAmount;
        public readonly int NewAmount;
        public readonly string Reason;

        public ResourceChange(ResourceDefinition resource, int delta, int oldAmount, int newAmount, string reason)
        {
            Resource = resource;
            Delta = delta;
            OldAmount = oldAmount;
            NewAmount = newAmount;
            Reason = reason;
        }
    }
}
