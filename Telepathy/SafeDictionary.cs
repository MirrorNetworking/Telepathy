// replaces ConcurrentDictionary which is not available in .NET 3.5 yet.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Telepathy
{
    public class SafeDictionary<TKey,TValue>
    {
        ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();
        Dictionary<TKey,TValue> dict = new Dictionary<TKey,TValue>();

        // for statistics. don't call Count and assume that it's the same after the
        // call.
        public int Count
        {
            get
            {
                rwLock.EnterReadLock();
                try
                {
                    return dict.Count;
                }
                finally
                {
                    rwLock.ExitReadLock();
                }
            }
        }

        public void Add(TKey key, TValue value)
        {
            rwLock.EnterWriteLock();
            try
            {
                dict[key] = value;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public void Remove(TKey key)
        {
            rwLock.EnterWriteLock();
            try
            {
                dict.Remove(key);
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        // can't check .ContainsKey before Get because it might change inbetween,
        // so we need a TryGetValue
        public bool TryGetValue(TKey key, out TValue result)
        {
            rwLock.EnterReadLock();
            try
            {
                return dict.TryGetValue(key, out result);
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        public List<TValue> GetValues()
        {
            rwLock.EnterReadLock();
            try
            {
                return dict.Values.ToList();
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        public void Clear()
        {
            rwLock.EnterWriteLock();
            try
            {
                dict.Clear();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }
    }
}