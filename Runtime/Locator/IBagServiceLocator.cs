namespace Hlight.ResourceBag
{
    /// <summary>
    /// Service Locator contract for ResourceBag — mirrors Hlight's IServiceLocator shape
    /// so consumer projects can bridge their existing locator/scope chain to the bag.
    /// </summary>
    /// <remarks>
    /// The signature is identical to the Hlight DI package's
    /// <c>AServiceLocator.TryProvide&lt;T&gt;</c>, so bridging costs no code at all — a
    /// project's concrete locator subclass just declares the interface and the inherited
    /// method satisfies it:
    /// <code>
    /// public sealed class GameServiceLocator : AServiceLocator, IBagServiceLocator { }
    /// </code>
    /// Declare it on the project-side subclass, not on <c>AServiceLocator</c> itself: the DI
    /// package's asmdef has no references, and adding this one would make a general-purpose
    /// DI package depend on ResourceBag.
    /// <para>
    /// <b>Resolve lazily, on first use — not in <see cref="AttachedRule.OnAttach"/>.</b>
    /// <c>OnAttach</c> runs inside the <see cref="ResourceBag"/> constructor, so resolving there
    /// requires every dependency to be registered before the bag is built. That holds for a
    /// sibling bag you construct yourself, but not for a service registered in a later
    /// bootstrap phase or a scope that does not exist yet — those resolve to <c>null</c>
    /// permanently and silently. Since the behaviours these dependencies serve run later
    /// anyway, resolve when first needed and cache on success:
    /// <code>
    /// private IHapticService _haptics;
    /// private bool _resolved;
    ///
    /// private IHapticService Haptics
    /// {
    ///     get
    ///     {
    ///         if (_resolved) return _haptics;
    ///         _resolved = Bag.Locator != null &amp;&amp; Bag.Locator.TryProvide(out _haptics);
    ///         return _haptics;   // null until registered, then picked up on a later call
    ///     }
    /// }
    /// </code>
    /// Holding the locator reference is correct here. Earlier versions of this doc told rules
    /// not to, which removed the only way to retry and made late registration unrecoverable.
    /// </para>
    /// </remarks>
    public interface IBagServiceLocator
    {
        /// <summary>
        /// Resolve <typeparamref name="T"/> from this locator (and its scope chain, if any).
        /// <paramref name="key"/> disambiguates multiple instances of the same type; pass
        /// <c>null</c> when a single instance is owned. Returns <c>false</c> on a miss anywhere
        /// in the chain, leaving <paramref name="value"/> as <c>null</c>.
        /// </summary>
        bool TryProvide<T>(out T value, string key = null) where T : class;
    }
}
