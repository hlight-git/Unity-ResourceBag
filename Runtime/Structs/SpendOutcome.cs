namespace Hlight.ResourceBag
{
    /// <summary>
    /// Rule verdict on a debit. <see cref="Reject"/> is sticky: the pipeline restores it
    /// after every rule call, so a later rule cannot resurrect a rejected debit.
    /// </summary>
    /// <remarks>
    /// <see cref="Substitute"/> is not restored, and deliberately so — a rule that
    /// overwrites it with <see cref="Continue"/> sends the debit back to the owner resource,
    /// which is the safe direction. Only the direction that could charge a player who
    /// should not have been charged is worth enforcing.
    /// <para>
    /// There is no <c>Skip</c>: it duplicated <see cref="ResourceIntent.SkipPrimary"/>. Set
    /// <c>SkipPrimary</c> on a debit for a free pass.
    /// </para>
    /// </remarks>
    public enum SpendOutcome
    {
        /// <summary>Proceed with the debit (default).</summary>
        Continue = 0,

        /// <summary>Fail the debit regardless of balance, and discard accumulated side effects.</summary>
        Reject = 1,

        /// <summary>Debit <see cref="ResourceIntent.SubstituteWith"/> instead.</summary>
        Substitute = 2
    }
}
