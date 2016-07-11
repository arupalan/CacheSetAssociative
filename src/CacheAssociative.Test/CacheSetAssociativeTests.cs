using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace CacheAssociative.Test
{
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
            var expectedUserData = new UserData { FirstName = "Aaravind", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Aaravind",ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                                    Thread.Sleep(200);
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
            var expectedUserData = new UserData { FirstName = "Aaravind", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Aaravind",ValidTag: u => u.FirstName == "Ramesh",fallback: () => { Thread.Sleep(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Aaravind", 
                fallback: () => {
                                    Thread.Sleep(200);
                                    return new UserData()
                                    {
                                        Comment = "Some Vague Data. For clarity only that the fallback won't be invoked because data from the cache would be picked up!!"
                                    };
                                });
            Assert.AreEqual(expectedUserData, actualUserData);
        }
        /*
        /// <summary>
        /// 2 On CacheHit , ValidTag may be invalid. The promise may be still fullfilled using the fallback returned block 
        ///         (ii) if the cache slot is not full we add a new slot to the cache-set
        /// </summary>
        /// <returns>block returned by previous fallback fitted into the end of the current slot in the cache-set </returns>
        [Test]
        public async Task OnCacheHitAndTagInvalidShouldfullfillPromiseWithBlockFromFallbackAppendedToBackOfCacheSet()
        {
            var testCache = new CacheSetAssociative<string, UserData>(set_size: 3, replaceAlgo: () => { return 0; });
            var expectedUserData = new UserData { FirstName = "Aaravind", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh", fallback: () => { Thread.Sleep(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            //Fill second slot
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () =>
                {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 1"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);

            //Now check that both the first slot and the second slot exists
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Aaravind", fallback: () => { Thread.Sleep(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);


            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => string.IsNullOrEmpty(u.Comment) && u.Comment.Contains("Slot"),
                fallback: () =>
                {
                    Task.Delay(200);
                    return new UserData()
                    {
                        Comment = "Some invalid data"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);
        }
        */
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
            var expectedUserData = new UserData { FirstName = "Aaravind", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh", fallback: () => { Thread.Sleep(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            //Fill second slot
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Thread.Sleep(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 1"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);

            //Fill third slot
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Thread.Sleep(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 2"
                    };
                });
            Assert.AreEqual("Data to fit slot 2", actualUserData.Comment);

            //Replace zeroth slot using LRU (replacement aka eviction Algo to be applied) 
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Thread.Sleep(200);
                    return new UserData()
                    {
                        FirstName = "Aaravind",
                        Comment = "Data to replace slot 0"
                    };
                });
            Assert.AreEqual("Data to replace slot 0", actualUserData.Comment);

            //Check that LRU (replacement aka eviction Algo to been applied in the previous step) in this current 3-way associative cache 
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Aaravind",
                fallback: () => {
                    Thread.Sleep(200);
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
            var expectedUserData = new UserData { FirstName = "Aaravind", LastName = "Something" };
            var actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh", fallback: () => { Thread.Sleep(200); return expectedUserData; });
            Assert.AreEqual(expectedUserData, actualUserData);

            //Fill second slot
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Thread.Sleep(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 1"
                    };
                });
            Assert.AreEqual("Data to fit slot 1", actualUserData.Comment);

            //Fill third slot
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Thread.Sleep(200);
                    return new UserData()
                    {
                        Comment = "Data to fit slot 2"
                    };
                });
            Assert.AreEqual("Data to fit slot 2", actualUserData.Comment);

            //Replace third slot using MRU (replacement aka eviction Algo to be applied) 
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => u.FirstName == "Ramesh",
                fallback: () => {
                    Thread.Sleep(200);
                    return new UserData()
                    {
                        FirstName = "Aaravind",
                        Comment = "Data to replace slot 2"
                    };
                });
            Assert.AreEqual("Data to replace slot 2", actualUserData.Comment);

            //Check that MRU (replacement aka eviction Algo to been applied in the previous step) in this current 3-way associative cache 
            actualUserData = await testCache.GetItem(key: "Aaravind", ValidTag: u => !string.IsNullOrEmpty(u.Comment) && u.Comment.Contains("replace"),
                fallback: () => {
                    Thread.Sleep(200);
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
