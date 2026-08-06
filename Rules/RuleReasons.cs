namespace Hlight.ResourceBag.Rules
{
    /// <summary>
    /// Reason strings emitted by the built-in rules. System reasons are prefixed with
    /// <c>_</c>; project reasons should not be. Core-level reasons live in
    /// <see cref="BagReasons"/>.
    /// </summary>
    public static class RuleReasons
    {
        /// <summary>Periodic delta applied by <see cref="PeriodicDeltaRule"/>. The sign of the change says which direction.</summary>
        public const string Periodic = "_periodic";

        /// <summary>A bundle definition expanded into its component credits.</summary>
        public const string ResolveBundle = "_resolve_bundle";
    }
}
