// C#'s ConcurrentHashSet is not available in Unity :(
// Let's create a simple thread safe hash set
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SafeDictionary<TKey,TValue>
{
    Dictionary<TKey,TValue> dict = new Dictionary<TKey,TValue>();

    public void Add(TKey key, TValue value)
    {
        lock(dict)
        {
            dict[key] = value;
        }
    }

    public void Remove(TKey key)
    {
        lock(dict)
        {
            dict.Remove(key);
        }
    }

    // can't check .ContainsKey before Get because it might change inbetween,
    // so we need a TryGetValue
    public bool TryGetValue(TKey key, out TValue result)
    {
        lock(dict)
        {
            return dict.TryGetValue(key, out result);
        }
    }

    public List<TValue> GetValues()
    {
        lock(dict)
        {
            return dict.Values.ToList();
        }
    }

    public void Clear()
    {
        lock(dict)
        {
            dict.Clear();
        }
    }
}
