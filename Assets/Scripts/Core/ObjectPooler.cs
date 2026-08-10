using System.Collections.Generic;
using UnityEngine;

namespace RingSport.Core
{
    [System.Serializable]
    public class Pool
    {
        public string tag;
        public GameObject prefab;
        public int size;
    }

    public class ObjectPooler : MonoBehaviour
    {
        public static ObjectPooler Instance { get; private set; }

        [SerializeField] private List<Pool> pools = new List<Pool>();

        // All members per tag (never shrinks; used for reclaim + ClearAllPools)
        private Dictionary<string, Queue<GameObject>> poolDictionary;
        // Free members per tag - spawn is an O(1) pop instead of an O(size)
        // queue walk with a native activeInHierarchy check per step
        private readonly Dictionary<string, Stack<GameObject>> freeStacks = new Dictionary<string, Stack<GameObject>>();
        // Member -> owning tag, so ReturnToPool can recycle for real
        private readonly Dictionary<GameObject, string> memberTags = new Dictionary<GameObject, string>();
        // Guards double-returns (an object must be pushed free exactly once)
        private readonly HashSet<GameObject> freeSet = new HashSet<GameObject>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializePools();
        }

        private void InitializePools()
        {
            poolDictionary = new Dictionary<string, Queue<GameObject>>();

            foreach (Pool pool in pools)
            {
                CreatePool(pool.tag, pool.prefab, pool.size);
                GameLog.Info($"Initialized pool '{pool.tag}' with {pool.size} objects");
            }
        }

        private void CreatePool(string tag, GameObject prefab, int size)
        {
            var objectPool = new Queue<GameObject>();
            var free = new Stack<GameObject>(size);

            for (int i = 0; i < size; i++)
            {
                GameObject obj = Instantiate(prefab);
                obj.SetActive(false);
                obj.transform.SetParent(transform);
                objectPool.Enqueue(obj);
                memberTags[obj] = tag;
                free.Push(obj);
                freeSet.Add(obj);
            }

            poolDictionary.Add(tag, objectPool);
            freeStacks.Add(tag, free);
        }

        public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
        {
            if (poolDictionary == null)
            {
                GameLog.Error("Pool dictionary is not initialized!");
                return null;
            }

            if (!poolDictionary.TryGetValue(tag, out Queue<GameObject> pool))
            {
                GameLog.Warn($"Pool with tag '{tag}' doesn't exist. Available pools: {string.Join(", ", poolDictionary.Keys)}");
                return null;
            }

            GameObject objectToSpawn = null;

            Stack<GameObject> free = freeStacks[tag];
            while (free.Count > 0)
            {
                GameObject candidate = free.Pop();
                freeSet.Remove(candidate);
                if (candidate == null)
                    continue; // destroyed externally
                if (candidate.activeSelf)
                    continue; // stale entry (activated without a spawn)
                objectToSpawn = candidate;
                break;
            }

            if (objectToSpawn == null)
            {
                // Reclaim pass: members that were deactivated directly instead
                // of via ReturnToPool (mini-levels toggle pooled objects)
                int count = pool.Count;
                for (int i = 0; i < count; i++)
                {
                    GameObject candidate = pool.Dequeue();
                    pool.Enqueue(candidate);
                    if (candidate != null && !candidate.activeInHierarchy && !freeSet.Contains(candidate))
                    {
                        objectToSpawn = candidate;
                        break;
                    }
                }
            }

            if (objectToSpawn == null)
            {
                GameLog.Warn($"Pool '{tag}' exhausted! All {pool.Count} objects are active. Consider increasing pool size.");
                return null;
            }

            objectToSpawn.SetActive(true);
            objectToSpawn.transform.SetParent(null); // Unparent so it can move freely
            objectToSpawn.transform.position = position;
            objectToSpawn.transform.rotation = rotation;

            return objectToSpawn;
        }

        public void ReturnToPool(GameObject obj)
        {
            obj.SetActive(false);
            obj.transform.SetParent(transform);

            // Members go back on their free stack (exactly once); non-members
            // keep the legacy park-inactive behavior
            if (memberTags.TryGetValue(obj, out string tag) && freeSet.Add(obj))
                freeStacks[tag].Push(obj);
        }

        public void ClearAllPools()
        {
            foreach (var kvp in poolDictionary)
            {
                Stack<GameObject> free = freeStacks[kvp.Key];
                foreach (var obj in kvp.Value)
                {
                    if (obj == null)
                        continue;
                    if (obj.activeInHierarchy)
                        obj.SetActive(false);
                    if (freeSet.Add(obj))
                        free.Push(obj);
                }
            }
        }

        /// <summary>
        /// Creates a pool at runtime if it doesn't already exist
        /// </summary>
        public void CreatePoolIfNeeded(string tag, GameObject prefab, int size)
        {
            if (poolDictionary == null)
            {
                poolDictionary = new Dictionary<string, Queue<GameObject>>();
            }

            if (poolDictionary.ContainsKey(tag))
                return;

            CreatePool(tag, prefab, size);
            GameLog.Info($"Created runtime pool '{tag}' with {size} objects");
        }

        /// <summary>Every member of a pool, active and parked. Empty when the pool doesn't exist.</summary>
        public IEnumerable<GameObject> GetPoolMembers(string tag)
        {
            if (poolDictionary == null || !poolDictionary.TryGetValue(tag, out Queue<GameObject> pool))
                yield break;

            foreach (GameObject obj in pool)
            {
                if (obj != null)
                    yield return obj;
            }
        }

        /// <summary>
        /// Check if a pool exists
        /// </summary>
        public bool HasPool(string tag)
        {
            return poolDictionary != null && poolDictionary.ContainsKey(tag);
        }
    }
}
