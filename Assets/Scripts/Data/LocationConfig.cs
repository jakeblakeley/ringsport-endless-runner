using UnityEngine;
using UnityEngine.Rendering;

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

        [Tooltip("Ambient light probe baked from the skybox by Tools/RingSport/Bake Location Ambient. " +
                 "27 SH coefficients (9 per RGB channel). Empty = not baked yet.")]
        [SerializeField] [HideInInspector] private float[] bakedAmbientProbe;

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

        public bool HasBakedAmbientProbe => bakedAmbientProbe != null && bakedAmbientProbe.Length == 27;

        /// <summary>
        /// The skybox's ambient light as spherical harmonics, baked in the editor.
        /// Computing this at runtime means DynamicGI.UpdateEnvironment(), which reads
        /// the skybox cubemap back to the CPU - an operation WebGPU refuses outright,
        /// leaving the ambient probe garbage and every lit surface mis-shaded.
        /// </summary>
        public SphericalHarmonicsL2 BakedAmbientProbe
        {
            get
            {
                var sh = new SphericalHarmonicsL2();
                if (!HasBakedAmbientProbe)
                    return sh;

                for (int rgb = 0; rgb < 3; rgb++)
                {
                    for (int coeff = 0; coeff < 9; coeff++)
                        sh[rgb, coeff] = bakedAmbientProbe[rgb * 9 + coeff];
                }
                return sh;
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor-only writer for Tools/RingSport/Bake Location Ambient.</summary>
        public void SetBakedAmbientProbe(SphericalHarmonicsL2 sh)
        {
            bakedAmbientProbe = new float[27];
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coeff = 0; coeff < 9; coeff++)
                    bakedAmbientProbe[rgb * 9 + coeff] = sh[rgb, coeff];
            }
        }
#endif
    }
}
