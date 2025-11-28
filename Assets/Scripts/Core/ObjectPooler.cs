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

        private Dictionary<string, Queue<GameObject>> poolDictionary;

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
                Queue<GameObject> objectPool = new Queue<GameObject>();

                for (int i = 0; i < pool.size; i++)
                {
                    GameObject obj = Instantiate(pool.prefab);
                    obj.SetActive(false);
                    obj.transform.SetParent(transform);
                    objectPool.Enqueue(obj);
                }

                poolDictionary.Add(pool.tag, objectPool);
                Debug.Log($"Initialized pool '{pool.tag}' with {pool.size} objects");
            }
        }

        public GameObject SpawnFromPool(string tag, Vector3 position, Quaternion rotation)
        {
            if (poolDictionary == null)
            {
                Debug.LogError("Pool dictionary is not initialized!");
                return null;
            }

            if (!poolDictionary.ContainsKey(tag))
            {
                Debug.LogWarning($"Pool with tag '{tag}' doesn't exist. Available pools: {string.Join(", ", poolDictionary.Keys)}");
                return null;
            }

            Queue<GameObject> pool = poolDictionary[tag];

            // Find an inactive object in the pool
            GameObject objectToSpawn = null;
            int checkedCount = 0;
            int poolSize = pool.Count;

            while (checkedCount < poolSize)
            {
                GameObject obj = pool.Dequeue();
                pool.Enqueue(obj);

                if (!obj.activeInHierarchy)
                {
                    objectToSpawn = obj;
                    break;
                }

                checkedCount++;
            }

            // If no inactive object found, pool is exhausted
            if (objectToSpawn == null)
            {
                Debug.LogWarning($"Pool '{tag}' exhausted! All {poolSize} objects are active. Consider increasing pool size.");
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
        }

        public void ClearAllPools()
        {
            foreach (var pool in poolDictionary.Values)
            {
                foreach (var obj in pool)
                {
                    if (obj.activeInHierarchy)
                    {
                        obj.SetActive(false);
                    }
                }
            }
        }
    }
}
