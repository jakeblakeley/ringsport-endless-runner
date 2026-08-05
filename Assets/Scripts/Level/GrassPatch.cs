using System.Collections.Generic;
using UnityEngine;

namespace RingSport.Level
{
    /// <summary>
    /// Procedurally builds a dense patch of grass blades as one static mesh and
    /// assigns it to this object's MeshFilter. Sits as a child of the side
    /// floor prefabs, rendered with Custom/Mobile/GrassBlades.
    ///
    /// Meshes are generated once per unique settings hash and shared by every
    /// tile instance (the pooled side floors all point at the same mesh), so
    /// the pattern repeats tile to tile and generation cost is a one-off at
    /// load. WebGL2/WebGPU friendly by construction: plain vertex buffers, no
    /// instancing paths, no geometry shaders, no textures.
    ///
    /// Local-space convention: the patch is authored in the side floor's local
    /// space, where +X always faces the track (FloorSpawner spawns the right
    /// side floor rotated 180). Blades past +X extent/2 spill over the inner
    /// edge onto the center tile with a noisy, decaying density so the seam
    /// between side and main floors disappears under grass.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [ExecuteAlways]
    public class GrassPatch : MonoBehaviour
    {
        [Header("Patch Area (local units)")]
        [Tooltip("Patch extent along local X (across the track direction). Matches the 10-unit builtin plane of the floor tiles.")]
        [SerializeField] private float patchWidth = 10f;

        [Tooltip("Patch extent along local Z (along the track direction)")]
        [SerializeField] private float patchLength = 10f;

        [Header("Density")]
        [Tooltip("Blades per square local unit at the track-facing edge. ~18 reads as dense lawn, ~4 as sparse desert tufts.")]
        [SerializeField] [Range(0.5f, 40f)] private float bladesPerSquareUnit = 18f;

        [Tooltip("Density multiplier at the outer (away from track) edge; density ramps linearly toward 1 at the track edge. Saves vertices where the camera barely looks.")]
        [SerializeField] [Range(0.05f, 1f)] private float outerDensityScale = 0.45f;

        [Header("Inner Edge Overlap")]
        [Tooltip("How far blades spill past the track-facing edge onto the center tile (local units). Lanes sit 3 units into the 5-unit half tile, keep this under ~1.")]
        [SerializeField] [Range(0f, 1.5f)] private float innerOverhang = 0.9f;

        [Header("Blade Shape (local units)")]
        [SerializeField] private float bladeHeightMin = 0.35f;
        [SerializeField] private float bladeHeightMax = 0.75f;
        [SerializeField] private float bladeHalfWidth = 0.045f;

        [Tooltip("Max static lean of a blade tip as a fraction of its height")]
        [SerializeField] [Range(0f, 0.6f)] private float bladeLeanMax = 0.25f;

        [Header("Variation")]
        [Tooltip("Seed for the deterministic blade layout. Give each location its own seed so patches differ between locations while every tile of one location shares a mesh.")]
        [SerializeField] private int seed = 12345;

        // One mesh per unique settings hash, shared across all pooled tiles
        private static readonly Dictionary<int, Mesh> MeshCache = new Dictionary<int, Mesh>();

        private void Awake()
        {
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Defer: assigning meshes mid-validation is not allowed
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    Apply();
            };
        }
#endif

        // Domain reloads drop the static cache while play-mode options can keep
        // statics alive; make both paths start clean
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            MeshCache.Clear();
        }

        private void Apply()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                return;

            int key = SettingsHash();
            if (!MeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = BuildMesh();
                MeshCache[key] = mesh;
            }

            if (meshFilter.sharedMesh != mesh)
                meshFilter.sharedMesh = mesh;
        }

        private int SettingsHash()
        {
            return System.HashCode.Combine(
                System.HashCode.Combine(patchWidth, patchLength, bladesPerSquareUnit, outerDensityScale),
                System.HashCode.Combine(innerOverhang, bladeHeightMin, bladeHeightMax, bladeHalfWidth),
                System.HashCode.Combine(bladeLeanMax, seed));
        }

        private Mesh BuildMesh()
        {
            var rng = new System.Random(seed);
            float halfW = patchWidth * 0.5f;
            float halfL = patchLength * 0.5f;

            // Jittered grid: even coverage without Poisson cost. Cell size from
            // peak density; per-cell acceptance then shapes the density field.
            float cell = Mathf.Sqrt(1f / Mathf.Max(bladesPerSquareUnit, 0.01f));
            int cols = Mathf.CeilToInt((patchWidth + innerOverhang) / cell);
            int rows = Mathf.CeilToInt(patchLength / cell);

            var vertices = new List<Vector3>(cols * rows * 5 / 2);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(vertices.Capacity * 6 / 5);

            // Noise offset so different seeds also get different edge wobble
            float edgeNoiseOffset = (float)(rng.NextDouble() * 100.0);

            for (int cx = 0; cx < cols; cx++)
            {
                for (int cz = 0; cz < rows; cz++)
                {
                    float x = -halfW + (cx + (float)rng.NextDouble()) * cell;
                    float z = -halfL + (cz + (float)rng.NextDouble()) * cell;
                    if (z > halfL)
                        continue;

                    // Density ramp: sparse at the outer edge, full at the track
                    float acrossT = Mathf.InverseLerp(-halfW, halfW, Mathf.Min(x, halfW));
                    float acceptChance = Mathf.Lerp(outerDensityScale, 1f, acrossT);
                    float heightScale = 1f;

                    if (x > halfW)
                    {
                        // Spill zone past the inner edge: wavy boundary from
                        // low-frequency noise, quadratic falloff toward it, and
                        // slightly shorter blades so the overlap reads natural
                        if (innerOverhang <= 0f)
                            continue;
                        float wobble = Mathf.PerlinNoise(z * 0.55f + edgeNoiseOffset, edgeNoiseOffset);
                        float edgeX = halfW + innerOverhang * (0.3f + 0.7f * wobble);
                        if (x >= edgeX)
                            continue;
                        float spillT = (x - halfW) / (edgeX - halfW);
                        acceptChance = (1f - spillT) * (1f - spillT);
                        heightScale = Mathf.Lerp(1f, 0.7f, spillT);
                    }

                    if (rng.NextDouble() > acceptChance)
                        continue;

                    float bladeRand = (float)rng.NextDouble();
                    float height = Mathf.Lerp(bladeHeightMin, bladeHeightMax, (float)rng.NextDouble()) * heightScale;
                    float width = bladeHalfWidth * Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble());

                    float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));

                    float leanYaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float leanMag = (float)rng.NextDouble() * bladeLeanMax * height;
                    var lean = new Vector3(Mathf.Cos(leanYaw) * leanMag, 0f, Mathf.Sin(leanYaw) * leanMag);

                    AddBlade(vertices, uvs, triangles, new Vector3(x, 0f, z), right, lean, height, width, bladeRand);
                }
            }

            var mesh = new Mesh
            {
                name = $"GrassPatch_{seed}_{vertices.Count / 5}",
                hideFlags = HideFlags.DontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, false);

            // Manual bounds: the arc effect drops far vertices up to
            // _ArcStrength (20 in-scene) world units, and wind sways tips.
            // Unity's computed bounds would cull tiles whose displaced blades
            // are still on screen.
            float maxH = bladeHeightMax + 0.5f;
            mesh.bounds = new Bounds(
                new Vector3(innerOverhang * 0.5f, (maxH - 22f) * 0.5f, 0f),
                new Vector3(patchWidth + innerOverhang + 1f, maxH + 22f, patchLength + 1f));

            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// One blade: 5 vertices, 3 triangles. Tapered quad (root to mid) plus
        /// a tip triangle, with a static lean applied half at mid, full at tip.
        /// UV: x = normalized height (drives gradient + sway), y = per-blade
        /// random (tint + wind phase jitter).
        /// </summary>
        private static void AddBlade(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
            Vector3 root, Vector3 right, Vector3 lean, float height, float halfWidth, float bladeRand)
        {
            const float midT = 0.55f;
            int i = vertices.Count;

            Vector3 mid = root + lean * (midT * 0.5f) + Vector3.up * (height * midT);
            Vector3 widthMid = right * (halfWidth * 0.62f);
            Vector3 widthBase = right * halfWidth;

            vertices.Add(root - widthBase);
            vertices.Add(root + widthBase);
            vertices.Add(mid - widthMid);
            vertices.Add(mid + widthMid);
            vertices.Add(root + lean + Vector3.up * height);

            uvs.Add(new Vector2(0f, bladeRand));
            uvs.Add(new Vector2(0f, bladeRand));
            uvs.Add(new Vector2(midT, bladeRand));
            uvs.Add(new Vector2(midT, bladeRand));
            uvs.Add(new Vector2(1f, bladeRand));

            triangles.Add(i + 0); triangles.Add(i + 2); triangles.Add(i + 1);
            triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i + 3);
            triangles.Add(i + 2); triangles.Add(i + 4); triangles.Add(i + 3);
        }
    }
}
