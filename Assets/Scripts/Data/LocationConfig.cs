using UnityEngine;

namespace RingSport.Level
{
    [CreateAssetMenu(fileName = "LocationConfig", menuName = "RingSport/Location Config", order = 2)]
    public class LocationConfig : ScriptableObject
    {
        [Header("Location Info")]
        [SerializeField] private Location location;

        [Header("Floor Prefabs")]
        [Tooltip("Floor prefab used in the main gameplay area")]
        [SerializeField] private GameObject mainFloorPrefab;

        [Tooltip("Floor prefab used for visual side floors (left and right)")]
        [SerializeField] private GameObject sideFloorPrefab;

        [Tooltip("Optional: Override finish line floor prefab for this location")]
        [SerializeField] private GameObject finishLineFloorPrefab;

        [Header("Scenery")]
        [Tooltip("Prefabs to scatter on side floors for visual decoration")]
        [SerializeField] private GameObject[] sceneryPrefabs;

        [Tooltip("Minimum number of scenery objects per side floor")]
        [SerializeField] [Range(0, 10)] private int minSceneryPerFloor = 1;

        [Tooltip("Maximum number of scenery objects per side floor")]
        [SerializeField] [Range(1, 10)] private int maxSceneryPerFloor = 5;

        [Tooltip("Minimum distance between scenery objects (for Poisson Disk Sampling)")]
        [SerializeField] [Range(0.5f, 5f)] private float sceneryMinDistance = 1.5f;

        [Tooltip("Pool size per scenery prefab type")]
        [SerializeField] [Range(10, 100)] private int sceneryPoolSize = 30;

        public Location Location => location;
        public GameObject MainFloorPrefab => mainFloorPrefab;
        public GameObject SideFloorPrefab => sideFloorPrefab;
        public GameObject FinishLineFloorPrefab => finishLineFloorPrefab;
        public GameObject[] SceneryPrefabs => sceneryPrefabs;
        public int MinSceneryPerFloor => minSceneryPerFloor;
        public int MaxSceneryPerFloor => maxSceneryPerFloor;
        public float SceneryMinDistance => sceneryMinDistance;
        public int SceneryPoolSize => sceneryPoolSize;
    }
}
