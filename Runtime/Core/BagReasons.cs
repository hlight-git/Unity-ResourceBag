namespace Hlight.ResourceBag
{
    /// <summary>
    /// Reason strings emitted by <see cref="ResourceBag"/> itself. System reasons are
    /// prefixed with <c>_</c>; project reasons should not be. Reasons produced by the
    /// built-in rules live in <c>Hlight.ResourceBag.Rules.RuleReasons</c>.
    /// </summary>
    public static class BagReasons
    {
        /// <summary>Amount restored by a TrySpendAll rollback.</summary>
        public const string Restored = "_restored";

        /// <summary>A credit hit the cap; the surplus was clamped.</summary>
        public const string Overflow = "_overflow";

        /// <summary>
        /// Default when a call site passes no reason. Deliberately a real string rather than
        /// <c>null</c>: an event stream full of nulls cannot be told apart from "nobody set
        /// one", while this is greppable when it is time to add the missing breadcrumbs.
        /// </summary>
        public const string Unspecified = "_unspecified";
    }
}
