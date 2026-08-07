using System;
using System.Collections.Generic;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Minimal in-memory <see cref="IBagInjector"/>. Holds one push action per target type
    /// and dispatches on the target's exact runtime type, which is enough to stand in for the
    /// project's real injector without this package referencing the DI one.
    /// </summary>
    internal sealed class FakeBagInjector : IBagInjector
    {
        private readonly Dictionary<Type, Action<object>> _resolvers
            = new Dictionary<Type, Action<object>>();

        public FakeBagInjector Resolve<T>(Action<T> push) where T : class
        {
            _resolvers[typeof(T)] = target => push((T)target);
            return this;
        }

        public void Inject(object target)
        {
            if (target != null && _resolvers.TryGetValue(target.GetType(), out var push))
                push(target);
        }
    }
}
