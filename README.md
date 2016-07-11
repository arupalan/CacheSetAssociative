# CacheSetAssociative
Recall that in a set-associative cache, the index field identifies a set of blocks (unlike a directmapped
cache, where the index identifies a unique block) that may contain the data. Every block
within this set is examined (in parallel) to see if the tag field matches and the valid bit is set. If
both these conditions are met, we have a cache hit. Otherwise, on a cache miss, we must load
a new block from memory into this set, replacing the least recently used (LRU) block in that set.

![Console Mode](http://www.alanaamy.net/wp-content/uploads/2016/07/CacheSetAssociativeTypes.png)
 
Here I've attempted to replicate the applicability of set-associative cache in the domain of application memory. 
I have previously worked with Gemfire and to an extent Coherence as an enterprice data fabric. But perhaps 
the applicability of those products were interesting within the space of multi-node grid computations and in as such
having a separate layer of application cache which could be managed independently from the grids' internal task
and session management.

This is therefore a very modest attempt to see how if an L2, L3 hardware specific cache optimisation logic can make its way 
into the application memory domain and bestow similar advantage. Having written the spec and the logic does not mean 
in anyway that I'm convined at all that this is in any means a holy grain of cache management; but merely a modest attempt.

I shall update this section later and when I find some more interested people in this topic but for now I've provided detailed 
albeit ragged documentation of my through while I was coding. Some I wrote into the code and some into the test code.

There are code cyclomatics which can be looked into later when I again find some time. Hope the presentation is not so fuzzy 


# Building From Source on Mac OSX or Windows (Assumed .Net Core already installed on target platform)
1. Move to your local git repository directory or any directory (with git init) in console. I assume you have .Net Core installed on Mac.
If not then follow this link https://www.microsoft.com/net/core#macos


2. Clone repository.

        git clone https://github.com/arupalan/CacheSetAssociative
        cd CacheAssociative
        dotnet restore
        cd src/CacheAssociative.Test
        dotnet run
        
        You will see test results like below which suffices me
![Console Mode](http://www.alanaamy.net/wp-content/uploads/2016/07/MacRunCacheAssociativeTests.png)
        
        Notice that when I resorted to Task.Delay in the place of Thread.Sleep (intended to increase payload in fallback) 
        there is immediate improvement in performance , for obvious reason
![Console Mode](http://www.alanaamy.net/wp-content/uploads/2016/07/Task.Delay_.png)
        
## The main code along with the rugged comments:
```csharp
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
                //See fullfilled condition 2 a)
                mutateLock.EnterReadLock();
                foreach (TResult cacheblock in cacheset)
                    if (ValidTag(cacheblock)) return cacheblock;
                mutateLock.ExitReadLock();

                //See fullfilled condition 2 b). 
                //Note spawning a new thread because fallback may have a higher payload since it makes expensive database calls for eg.
                try
                {
                    TResult block = await Task.Factory.StartNew<TResult>(() => fallback());
                    // Condition 2 c (i)
                    var replace = false; //local variable is thread-safe
                    var replaceIndex = -1; //local variable is thread safe
                    mutateLock.EnterReadLock();
                    replaceIndex = cacheset.Count();
                    if (replaceIndex == set_size) replace = true;
                    if (replace) replaceIndex = replaceAlgo();
                    else replaceIndex = -1;
                    mutateLock.ExitReadLock();

                    mutateLock.TryEnterWriteLock(-1);
                    if (replaceIndex == -1)
                        cacheset.Add(block);//Condition 2 c (ii)
                    else
                        cacheset[replaceIndex] = block;//Condition 2 c (i)
                    mutateLock.ExitWriteLock();

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
                    cacheset = new List<TResult>(set_size);
                    cacheset.Add(block);
                    mutateLock.ExitWriteLock();

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
```

## The Testsing of the logic in the above code
```c#
    [TestFixture]
    public class CacheSetAssociativeTests
    {
        /// <summary>
        /// 2. On Cache Miss, the promise may be fulfilled by
        ///    d) The fallback returns a memory block. The block is added to the cache by allocating a new cacheset initialized with
        ///      the fallback provided memory block. 
        /// </summary>
        /// <returns>block returned by fallback</returns>
        [Test]
        public async Task OnCacheMissShouldfullfillPromisebyInitializingCacheSetFromMemoryAndAddingtoCache()
        {
            var testCache = new CacheSetAssociative<string, UserData>(set_size:3, replaceAlgo: () => { return 0; });
            var expectedUserData = new UserData { FirstName = "Arup", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Arup",ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                                    Task.Delay(200);
                                    return expectedUserData;
                                });
            Assert.AreEqual(expectedUserData, actualUserData);
        }

        /// <summary>
        /// 2. On Cache Hit, ValidTag is valid The promise may be fulfilled as
        ///    a) There is a cacheHit and also the tag resolves to a valid cache-block in the set
        /// </summary>
        /// <returns>block in cache set valid by Validtag</returns>
        [Test]
        public async Task OnCacheHitAndTagValidShouldfullfillPromiseWithBlockFromCacheSet()
        {
            var testCache = new CacheSetAssociative<string, UserData>(set_size:3, replaceAlgo: () => { return 0; });
            var expectedUserData = new UserData { FirstName = "Arup", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Arup",ValidTag: u => u.FirstName == "Ramesh",fallback: () => { Task.Delay(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Arup", 
                fallback: () => {
                                    Task.Delay(200);
                                    return new UserData()
                                    {
                                        Comment = "Some Vague Data. For clarity only that the fallback won't be invoked because data from the cache would be picked up!!"
                                    };
                                });
            Assert.AreEqual(expectedUserData, actualUserData);
        }
        /// <summary>
        /// 2 On CacheHit , ValidTag may be invalid. The promise may be still fullfilled using the fallback returned block 
        ///         (ii) if the cache slot is not full we add a new slot to the cache-set
        /// </summary>
        /// <returns>block returned by previous fallback fitted into the end of the current slot in the cache-set </returns>
        [Test]
        public async Task OnCacheHitAndTagInvalidShouldfullfillPromiseWithBlockFromFallbackAppendedToBackOfCacheSet()
        {
            var testCache = new CacheSetAssociative<string, UserData>(set_size: 3, replaceAlgo: () => { return 0; });
            var expectedUserData = new UserData { FirstName = "Arup", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh", fallback: () => { Task.Delay(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            //Fill second slot
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 1"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);

            //Check the second slot
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => !string.IsNullOrEmpty(u.Comment) && u.Comment.Contains("slot"),
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data never to be used!!"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);
        }
        
        /// <summary>
        /// 2 On CacheHit , ValidTag may be invalid. The promise may be still fullfilled using the fallback returned block 
        ///         (i)  if the case-set is full then we apply a replacementAlgo also named as the evictionAlgorithm
        /// The Test is to check that the LRU Algorithm replaceAlgo: () => { return 0; }) has been applied on a 3-way associative cache
        /// evict or replace slot 0; ie the Least Recently used slot
        /// </summary>
        /// <returns>block replaced via LRU Algo </returns>
        [Test]
        public async Task OnCacheHitAndTagInvalidShouldfullfillPromiseWithBlockFromFallbackAndApplyLRU()
        {
            var testCache = new CacheSetAssociative<string, UserData>(set_size: 3, replaceAlgo: () => { return 0; });
            var expectedUserData = new UserData { FirstName = "Arup", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh", fallback: () => { Task.Delay(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            //Fill second slot
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 1"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);

            //Fill third slot
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 2"
                    };
                });
            Assert.AreEqual("Data to fit slot 2", actualUserData.Comment);

            //Replace zeroth slot using LRU (replacement aka eviction Algo to be applied) 
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        FirstName = "Arup",
                        Comment = "Data to replace slot 0"
                    };
                });
            Assert.AreEqual("Data to replace slot 0", actualUserData.Comment);

            //Check that LRU (replacement aka eviction Algo to been applied in the previous step) in this current 3-way associative cache 
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Arup",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "We do not expect this to be called anyway!!"
                    };
                });
            Assert.AreEqual("Data to replace slot 0", actualUserData.Comment);
        }

        /// <summary>
        /// 2 On CacheHit , ValidTag may be invalid. The promise may be still fullfilled using the fallback returned block 
        ///         (i)  if the case-set is full then we apply a replacementAlgo also named as the evictionAlgorithm
        /// The Test is to check that the MRU Algorithm replaceAlgo: () => { return 2; }) has been applied on this 3-way associative cache
        /// evict or replace slot 2; ie the Most Recently used slot
        /// </summary>
        /// <returns>block replaced by MRU algo</returns>
        [Test]
        public async Task OnCacheHitAndTagInvalidShouldfullfillPromiseWithBlockFromFallbackAndApplyMRU()
        {
            var testCache = new CacheSetAssociative<string, UserData>(set_size: 3, replaceAlgo: () => { return 2; });
            var expectedUserData = new UserData { FirstName = "Arup", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh", fallback: () => { Task.Delay(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            //Fill second slot
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 1"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);

            //Fill third slot
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 2"
                    };
                });
            Assert.AreEqual("Data to fit slot 2", actualUserData.Comment);

            //Replace third slot using MRU (replacement aka eviction Algo to be applied) 
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        FirstName = "Arup",
                        Comment = "Data to replace slot 2"
                    };
                });
            Assert.AreEqual("Data to replace slot 2", actualUserData.Comment);

            //Check that MRU (replacement aka eviction Algo to been applied in the previous step) in this current 3-way associative cache 
            actualUserData = await testCache.GetItem(key: "Arup", ValidTag: u => !string.IsNullOrEmpty(u.Comment) && u.Comment.Contains("replace"),
                fallback: () => {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "We do not expect this to be called anyway!!"
                    };
                });
            Assert.AreEqual("Data to replace slot 2", actualUserData.Comment);
        }
    }

    public class UserData
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string CompanyName { get; set; }
        public string Comment { get; set; }
    }
}
```
