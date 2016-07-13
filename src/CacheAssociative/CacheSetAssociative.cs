using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CacheAssociative
{
    public class CacheSetAssociative<TKey, TResult>
    {
        /// <summary>
        /// Thw order of the cache-sets is not important as long the Indexer can reach quickest to the set block. 
        /// Therefore I chose outer container as Dictionary whose ContainsKey operation is O(1). 
        /// 
        /// The inner container is a List<TResult> because the order of the cache blocks within a specific set in important. 
        /// The replacement or eviction Algorithm depends on MRU or LRU etc which in turn depends on ordering 
        ///
        /// If there is a cacheMiss then the dictionary provides a syntactic sugar in Item [] to create a key and initialize and
        /// set a cache-set block.
        ///
        /// 
        /// Side Notes:
        /// 1. Also any argument of efficiency of TryGetValue in my modest opinion will not hold here because efficient if TryGetValue holds
        /// when we often have to try keys that turn out not to be in the dictionary when TryGetValue can be a more efficient 
        /// way to retrieve values. But in this scenerio we are designing for a cache where more often than not that the keys are available
        /// 2. When searching for a Key in ContainsKey , the Dictionary will use EqualityComparer.Default for the default IEqualityComparer 
        /// used by the Dictionary class. If you do not want to generally override GetHashCode and Equals on the class, or if you are unable to. 
        /// There is an overload of the Dictionary constructor in which you can provide the specific IEqualityComparer to use.
        /// </summary>
        private readonly ConcurrentDictionary<TKey, List<TResult>> cache;
        private int set_size;
        private readonly ReaderWriterLockSlim mutateLock = new ReaderWriterLockSlim();
        private Func<int> replaceAlgo;

        public CacheSetAssociative(int set_size, Func<int> replaceAlgo ) 
        {
            cache = new ConcurrentDictionary<TKey, List<TResult>>();
            this.set_size = set_size;
            this.replaceAlgo = replaceAlgo;
        }

        ~CacheSetAssociative()
        {
            if (mutateLock != null) mutateLock.Dispose();
        }

        /// <summary>
        /// The GetItem is designed as a promise to retrieve the item from the cache.
        /// The promise can have three states
        /// 1. pending, 
        /// 2. fulfilled 
        /// 3. rejected.
        /// 
        /// 1. The promise to get the Item from the cache can be pending and while it is 
        ///    pending the requester can do other jobs without getting blocked. 
        ///    It is for that reason that it is implemented as an async Task.
        ///
        /// 2. The promise may be fulfilled either of following 
        ///   a) There is a cacheHit and also the tag resolves to a valid cache-block in the set
        ///   b) There is a cacheHit but the tag could not resolve to a valid cache-block.
        ///      In that case we fallback and obtain value from memory for example a database dip. 
        ///   c) After fallback returns 
        ///         (i)  if the cache-set is full then we apply a replacementAlgo 
        ///         (ii) else we add a new slot to the cache-set
        ///   d) There is a cacheMiss. The fallback returns and we allocate a new cacheset initialized with
        ///      a new cache-block which has the first block as returned from the fallback
        ///      
        /// 3. The promise can be rejected because 
        ///          (i)  there was a cacheMiss and the fallback did not work say dataabase dip returned nothing or
        ///               the database dip returned an exception
        ///          (ii) there was a cacheHit but tag did not resolve a valid value and the fallback did not work 
        ///               say dataabase dip returned nothing or the database dip returned an exception
        /// 
        /// Side Notes:
        /// This is based on the .then(success, fail) anti-pattern
        /// The promise A+ spec which is adopted by Google Angular Team is avaiable at https://promisesaplus.com/
        /// If you find that difficult to understand then there is a link here to understand the promise spec 
        /// in terms of a simplified Cartoon
        /// http://andyshora.com/promises-angularjs-explained-as-cartoon.html
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public async Task<TResult> GetItem(TKey key, Func<TResult,bool> ValidTag, Func<TResult> fallback)
        {
            if (key == null) throw new ArgumentNullException("key", "key should be a non null value");
            if (fallback == null) throw new ArgumentNullException("fallback", "fallback should be a non null func");

            //cache hit conditons
            List<TResult> cacheset;
            if( cache.TryGetValue(key, out cacheset))
            {
                try
                {
                    //See fullfilled condition 2 a)
                    mutateLock.EnterReadLock();
                    try
                    {
                        foreach (TResult cacheblock in cacheset)
                            if (ValidTag(cacheblock)) return cacheblock;
                    }
                    finally
                    {
                        mutateLock.ExitReadLock();
                    }

                    //See fullfilled condition 2 b). 
                    //Note spawning a new thread because fallback may have a higher payload since it makes expensive database calls for eg.
                    TResult block = await Task.Factory.StartNew<TResult>(() => fallback());
                    // Condition 2 c (i)
                    var replace = false; //local variable is thread-safe
                    var replaceIndex = -1; //local variable is thread safe
                    mutateLock.EnterReadLock();
                    try
                    {
                        replaceIndex = cacheset.Count();
                        if (replaceIndex == set_size) replace = true;
                        if (replace) replaceIndex = replaceAlgo();
                        else replaceIndex = -1;
                    }
                    finally
                    {
                        mutateLock.ExitReadLock();
                    }

                    mutateLock.TryEnterWriteLock(-1);
                    try
                    {
                        if (replaceIndex == -1)
                            cacheset.Add(block);//Condition 2 c (ii)
                        else
                            cacheset[replaceIndex] = block;//Condition 2 c (i)
                    }
                    finally
                    {
                        mutateLock.ExitWriteLock();
                    }

                    return block;
                }
                catch(Exception e)
                {
                    //Also to be noted that the await may throw exception, cacheHit Condition 3 (ii) Promise Reject
                    //We do not propagate exception (strictly .Net exceptions only) but errFunc will be invoked as per Promise A+ spec
                    throw new Exception("There is a cacheHit. But promise rejected. Investigate Exception",e);
                }
            }
            //cache miss conditions
            else
            {
                //Get the item from memory and then add it to outer container 
                try
                {
                    TResult block = await Task.Factory.StartNew(() => fallback());

                    mutateLock.EnterWriteLock();
                    try
                    {
                        cacheset = new List<TResult>(set_size);
                        cacheset.Add(block);
                    }
                    finally
                    {
                        mutateLock.ExitWriteLock();
                    }

                    if (!cache.TryAdd(key, cacheset))
                        throw new Exception("Unable to add to the cache dictionary for unknown reason. Promise rejected");

                    return block;

                }
                catch (Exception e)
                {
                    //Also to be noted that the await may throw exception, cacheHit Condition 3 (ii) Promise Reject
                    //We do not propagate exception (strictly .Net exceptions only) but errFunc will be invoked as per Promise A+ spec
                    throw new Exception("An exception occured in the cache miss condition. Investigate the exception",e);
                }
            }
        }
    }
}
