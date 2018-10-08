// replaces ConcurrentDictionary which is not available in .NET 3.5 yet.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Telepathy
{
    public class SafeDictionary<TKey,TValue>
    {
        ReaderWriterLock rwLock = new ReaderWriterLock();
        Dictionary<TKey,TValue> dict = new Dictionary<TKey,TValue>();

        // for statistics. don't call Count and assume that it's the same after the
        // call.
        public int Count
        {
            get
            {
                try
                {
                    rwLock.AcquireReaderLock(TimeSpan.MaxValue);
                    return dict.Count;
                }
                finally
                {
                    rwLock.ReleaseReaderLock();
                }
            }
        }

        public void Add(TKey key, TValue value)
        {
            try
            {
                rwLock.AcquireWriterLock(TimeSpan.MaxValue);
                dict[key] = value;
            }
            finally
            {
                rwLock.ReleaseWriterLock();
            }
        }

        public void Remove(TKey key)
        {
            try
            {
                rwLock.AcquireWriterLock(TimeSpan.MaxValue);
                dict.Remove(key);
            }
            finally
            {
                rwLock.ReleaseWriterLock();
            }
        }

        // can't check .ContainsKey before Get because it might change inbetween,
        // so we need a TryGetValue
        public bool TryGetValue(TKey key, out TValue result)
        {
            try
            {
                rwLock.AcquireReaderLock(TimeSpan.MaxValue);
                return dict.TryGetValue(key, out result);
            }
            finally
            {
                rwLock.ReleaseReaderLock();
            }
        }

        public List<TValue> GetValues()
        {
            try
            {
                rwLock.AcquireReaderLock(TimeSpan.MaxValue);
                return dict.Values.ToList();
            }
            finally
            {
                rwLock.ReleaseReaderLock();
            }
        }

        public void Clear()
        {
            try
            {
                rwLock.AcquireWriterLock(TimeSpan.MaxValue);
                dict.Clear();
            }
            finally
            {
                rwLock.ReleaseWriterLock();
            }
        }
    }
}