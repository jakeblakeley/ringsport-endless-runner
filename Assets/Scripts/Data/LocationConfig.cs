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

        [Header("Start Scene")]
        [Tooltip("Optional prefab to instantiate at (0,0,0) when the level starts. Destroyed when it passes the despawn distance.")]
        [SerializeField] private GameObject startScenePrefab;

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

        [Header("Atmosphere")]
        [Tooltip("Apply this location's fog and skybox when it loads")]
        [SerializeField] private bool overrideAtmosphere = false;

        [Tooltip("Fog color for this location (exp2 fog)")]
        [SerializeField] private Color fogColor = new Color(0.54f, 0.8f, 0.81f);

        [Tooltip("Fog density for this location")]
        [SerializeField] [Range(0.005f, 0.1f)] private float fogDensity = 0.04f;

        [Tooltip("Optional skybox for this location; null keeps the current skybox")]
        [SerializeField] private Material skyboxMaterial;

        [Header("Audio")]
        [Tooltip("Background music for this location")]
        [SerializeField] private AudioClip music;

        [Tooltip("Ambient sound loop for this location (e.g., wind, birds, city noise)")]
        [SerializeField] private AudioClip ambientSound;

        public Location Location => location;
        public GameObject MainFloorPrefab => mainFloorPrefab;
        public GameObject SideFloorPrefab => sideFloorPrefab;
        public GameObject FinishLineFloorPrefab => finishLineFloorPrefab;
        public GameObject StartScenePrefab => startScenePrefab;
        public GameObject[] SceneryPrefabs => sceneryPrefabs;
        public int MinSceneryPerFloor => minSceneryPerFloor;
        public int MaxSceneryPerFloor => maxSceneryPerFloor;
        public float SceneryMinDistance => sceneryMinDistance;
        public int SceneryPoolSize => sceneryPoolSize;
        public bool OverrideAtmosphere => overrideAtmosphere;
        public Color FogColor => fogColor;
        public float FogDensity => fogDensity;
        public Material SkyboxMaterial => skyboxMaterial;
        public AudioClip Music => music;
        public AudioClip AmbientSound => ambientSound;
    }
}
