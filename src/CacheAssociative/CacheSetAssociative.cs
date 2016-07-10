using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CacheAssociative
{

    public class CacheSetAssociative<TKey, TResult>
    {
        private Action<TKey> replacementAlgo;
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
        private Dictionary<TKey, List<TResult>> cache;

        public CacheSetAssociative(int set_size, Action<TKey> replacementAlgo ) 
        {
            this.replacementAlgo = replacementAlgo;
        }

        /// <summary>
        /// The GetItem is designed as a promise to retrieve the item from the cache.
        /// The promise can have three states
        /// 1. pending, 
        /// 2. fulfilled 
        /// 3. rejected.
        /// 
        /// 1. The promise to get the Iten from the cache can be pending and while it is 
        ///    pending the requester can do other jobs without getting blocked. 
        ///    It is for that reason that it is implemented as an async Task.
        ///
        /// 2. The promise may be fulfilled either as 
        ///   a) There is a cacheHit and also tag resolves to a valid cache-block in the set
        ///   b) There is a cacheHit but the tag could not resolve to a valid cache-block.
        ///      In that case we fallback and obtain value from memory say a database dip. 
        ///      Next if the case-set is full then we apply a replacementAlgo 
        ///      else we add a new slot to the cache-set
        ///   c) The promise can be rejected because 
        ///          (i)  there was a cacheMiss and the fallback did not work say dataabase dip returned nothing or
        ///               the database dip returned an exception
        ///          (ii) there was a cacheHit but tag did not resolve a valid value and the fallback did not work 
        ///               say dataabase dip returned nothing or
        ///               the database dip returned an exception
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
        async Task<TResult> GetItem(TKey key, Func<TKey,TResult> fallback, Action err)
        {
            throw new NotImplementedException();
        }
    }
}
