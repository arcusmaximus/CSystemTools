using System;
using System.Collections.Generic;

namespace CSystemArc
{
    internal static class CollectionExtensions
    {
        public static TValue FetchValue<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> getValue)
        {
            if (!dict.TryGetValue(key, out TValue value))
            {
                value = getValue();
                dict.Add(key, value);
            }
            return value;
        }
    }
}
