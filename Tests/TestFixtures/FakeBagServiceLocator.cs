using System;
using System.Collections.Generic;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>
    /// Minimal in-memory IBagServiceLocator. Stores instances per (Type, key)
    /// — null key is treated as the "default" lookup.
    /// </summary>
    internal sealed class FakeBagServiceLocator : IBagServiceLocator
    {
        private readonly Dictionary<(Type, string), object> _services
            = new Dictionary<(Type, string), object>();

        public FakeBagServiceLocator Register<T>(T instance, string key = null) where T : class
        {
            _services[(typeof(T), key ?? string.Empty)] = instance;
            return this;
        }

        public bool TryProvide<T>(out T value, string key = null) where T : class
        {
            if (_services.TryGetValue((typeof(T), key ?? string.Empty), out var raw) && raw is T cast)
            {
                value = cast;
                return true;
            }
            value = null;
            return false;
        }
    }
}
