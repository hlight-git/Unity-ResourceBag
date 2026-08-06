using UnityEngine;

namespace Hlight.ResourceBag.Tests
{
    /// <summary>Factory for in-memory ResourceRule SOs used by tests.</summary>
    internal static class TestRuleFactory
    {
        public static T Create<T>() where T : ResourceRule
        {
            var rule = ScriptableObject.CreateInstance<T>();
            rule.name = typeof(T).Name;
            return rule;
        }
    }
}
