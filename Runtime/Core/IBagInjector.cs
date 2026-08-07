namespace Hlight.ResourceBag
{
    /// <summary>
    /// Push-DI seam for ResourceBag — mirrors the shape of Hlight's push injector so a
    /// consumer project can hand its own one to the bag.
    /// </summary>
    /// <remarks>
    /// Declared here rather than taken from the DI package for the same reason the locator
    /// interface used to be: a general-purpose DI package should not gain a dependency on
    /// ResourceBag, and this package should not gain one on it. Bridging costs one forwarding
    /// method on the project side:
    /// <code>
    /// sealed class BagInjector : IBagInjector
    /// {
    ///     readonly DependencyInjector _injector;
    ///     public BagInjector(DependencyInjector injector) => _injector = injector;
    ///     public void Inject(object target) => _injector.Inject(target);
    /// }
    /// </code>
    /// <para>
    /// <b>Where a rule gets its dependencies.</b> <see cref="ResourceRule.Attach"/> is the
    /// project's own code and is the factory for every <see cref="AttachedRule"/>, so that is
    /// the place to inject: build the instance, push into it, and fail loudly if a required
    /// dependency did not arrive.
    /// <code>
    /// public override AttachedRule Attach(ResourceBag bag, ResourceDefinition owner)
    /// {
    ///     var rule = new Instance(this, owner, bag);
    ///     bag.Injector?.Inject(rule);
    ///     if (rule.Policy == null) throw new InvalidOperationException($"{name}: no resolver declared.");
    ///     return rule;
    /// }
    /// </code>
    /// The instance receives what it needs before it runs, so a rule needs no resolve-retry
    /// state of its own — no cached locator, no "did I resolve yet" flag.
    /// </para>
    /// <para>
    /// <b>Push is eager.</b> <c>Attach</c> runs inside the <see cref="ResourceBag"/>
    /// constructor, so a service registered in a later bootstrap phase does not exist yet and
    /// will arrive as <c>null</c> — permanently. Either build the bag after those services
    /// exist, or have the resolver push a <c>Func&lt;T&gt;</c> so the rule reads it on first
    /// use. Deferring is then visible in the rule's own field type instead of hidden in
    /// resolve-and-cache logic.
    /// </para>
    /// <para>
    /// <b>No key.</b> A locator disambiguated instances of one service type with a key; push
    /// resolvers are per target type and have no equivalent. Two rules needing different
    /// instances of the same service must be different target types, or take the difference
    /// from their own authored config.
    /// </para>
    /// </remarks>
    public interface IBagInjector
    {
        /// <summary>
        /// Pushes dependencies into <paramref name="target"/>. A target no resolver matches is
        /// a no-op: the injector cannot know whether that target wanted anything, so a rule
        /// that requires a dependency checks for it itself.
        /// </summary>
        void Inject(object target);
    }
}
