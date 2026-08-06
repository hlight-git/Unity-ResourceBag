namespace Hlight.ResourceBag
{
    /// <summary>
    /// Time source for time-dependent rules.
    /// </summary>
    /// <remarks>
    /// Obligations on every implementation — an outside infrastructure package may
    /// supply one (e.g. server time), so these are contract, not convention:
    /// <list type="bullet">
    ///   <item><b>Never decreases.</b> Rules compare a persisted fire time against
    ///   <see cref="Now"/>; one step backwards stalls a rule permanently.</item>
    ///   <item><b>Domain is seconds since the Unix epoch (UTC).</b> Rule state is
    ///   persisted, so an implementation swapped in on a later session must use the
    ///   same epoch. Lagging real UTC is allowed (a capped offline grant does exactly
    ///   that); a since-boot counter is not.</item>
    ///   <item><b>Read on the main thread.</b> <see cref="ResourceBag"/> is not thread-safe.</item>
    ///   <item><b>Forward jumps are allowed</b> — offline credit, or an authoritative
    ///   time arriving late. Rules absorb these with a clamped catch-up.</item>
    /// </list>
    /// Deliberately one member: no "time changed" event, because
    /// <see cref="ResourceBag.Tick"/> polls <see cref="Now"/> and observes any jump on
    /// the next tick.
    /// </remarks>
    public interface IBagClock
    {
        /// <summary>Seconds since the Unix epoch (UTC). Never decreases between reads.</summary>
        double Now { get; }
    }
}
