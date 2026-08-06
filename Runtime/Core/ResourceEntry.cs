namespace Hlight.ResourceBag
{
    /// <summary>
    /// One in-scope resource plus this scope's policy for it: where it starts and how high it
    /// may go. This is the untyped view the bag consumes; the authored form lives on
    /// <see cref="BagBlueprint{TKey}"/>, where the resource field is typed to the blueprint's
    /// key family.
    /// </summary>
    /// <remarks>
    /// <see cref="InitialAmount"/> seeds a raw amount directly in the bag's constructor —
    /// bypassing the rule pipeline and the cap — before rules first run.
    /// </remarks>
    public readonly struct ResourceEntry
    {
        /// <summary>The in-scope resource.</summary>
        public readonly ResourceDefinition Resource;

        /// <summary>Amount a fresh bag starts with. 0 = start empty. Bypasses the cap and the pipeline.</summary>
        public readonly int InitialAmount;

        /// <summary>Highest amount this bag may hold. 0 = unlimited.</summary>
        public readonly int MaxAmount;

        public ResourceEntry(ResourceDefinition resource, int initialAmount, int maxAmount)
        {
            Resource = resource;
            InitialAmount = initialAmount;
            MaxAmount = maxAmount;
        }
    }
}
