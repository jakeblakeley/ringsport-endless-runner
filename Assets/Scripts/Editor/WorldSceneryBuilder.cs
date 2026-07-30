using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RingSport.Level;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Builds per-location world scenery from the Toon Desert / Toon Enchanted
    /// Meadow / Toon Series packs:
    /// - Arc-shader materials (Custom/Mobile/ArcEffect) for each pack atlas so
    ///   scenery curves with the world like the floors do
    /// - Scenery prefabs (ScrollableObject + normalized scale, colliders and
    ///   LODGroups stripped, shadows off) in Assets/Prefabs/World/<Location>
    /// - U-shaped StartScene prefabs per location in Assets/Prefabs/StartScenes
    /// - France/Oregon floor prefab copies + themed ground materials for all
    ///   four locations (textures from the Toon Series terrain set)
    /// - LocationConfig wiring (scenery lists, start scenes, floors, spawn tuning)
    ///
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Build World Scenery. Writes a report to
    /// Logs/WorldSceneryBuild.txt.
    /// </summary>
    public static class WorldSceneryBuilder
    {
        // Bump to force the auto-run to re-apply the build
        private const int BuildVersion = 5;
        private const string VersionPrefKey = "RingSport.WorldSceneryBuilder.Version";

        private const string ArcShaderName = "Custom/Mobile/ArcEffect";
        private const string WorldMatFolder = "Assets/Materials/World";
        private const string FloorMatFolder = "Assets/Materials/Floors";
        private const string WorldPrefabFolder = "Assets/Prefabs/World";
        private const string StartSceneFolder = "Assets/Prefabs/StartScenes";
        private const string FloorPrefabFolder = "Assets/Prefabs/Floors";

        private const string TS = "Assets/Toon Series/Toon Nature Assets/Prefabs";
        private const string TEM = "Assets/Toon Enchanted Meadow/Prefabs";
        private const string DS = "Assets/Toon Desert/Prefabs";
        private const string TSTex = "Assets/Toon Series/Toon Nature Assets/Textures";

        private const float BottomSink = 0.02f;

        private static readonly StringBuilder Report = new StringBuilder();
        private static readonly List<string> Errors = new List<string>();

        private enum SizeMode { Height, Footprint }

        private class SceneryDef
        {
            public string SourcePath;
            public string Name;
            public float TargetSize;
            public SizeMode Mode = SizeMode.Height;
            public bool TwoSided;
            public Color Tint = Color.white;
            public string TintSuffix = "";
            // true = ships in LocationConfig.sceneryPrefabs; false = StartScene-only prop
            public bool InSpawnList = true;
        }

        private class StartSceneItem
        {
            public string Key;
            public float X, Z, RotY, Scale;

            public StartSceneItem(string key, float x, float z, float rotY, float scale)
            {
                Key = key; X = x; Z = z; RotY = rotY; Scale = scale;
            }
        }

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            if (EditorPrefs.GetInt(VersionPrefKey, 0) >= BuildVersion)
                return;

            Run();
        }

        [MenuItem("Tools/RingSport/Build World Scenery")]
        private static void RunFromMenu()
        {
            Run();
        }

        private static void Run()
        {
            Report.Clear();
            Errors.Clear();
            Log($"World scenery build v{BuildVersion} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                Shader arcShader = Shader.Find(ArcShaderName);
                if (arcShader == null)
                    throw new Exception($"Shader '{ArcShaderName}' not found");

                EnsureFolder(WorldMatFolder);

                // ---- 1. Ground materials (rename Test* in place + create France/Oregon) ----
                BuildGroundMaterials(arcShader);

                // ---- 2. France/Oregon floor prefab copies ----
                BuildFloorPrefabCopies();

                // ---- 3. Scenery prefabs per world ----
                var worldPrefabs = new Dictionary<string, Dictionary<string, GameObject>>();
                foreach (var world in SceneryDefs())
                {
                    worldPrefabs[world.Key] = new Dictionary<string, GameObject>();
                    string folder = $"{WorldPrefabFolder}/{world.Key}";
                    EnsureFolder(folder);
                    foreach (SceneryDef def in world.Value)
                    {
                        GameObject prefab = BuildSceneryPrefab(def, folder, arcShader);
                        if (prefab != null)
                            worldPrefabs[world.Key][def.Name] = prefab;
                    }
                }

                // ---- 4. StartScene prefabs ----
                var startScenes = new Dictionary<string, GameObject>();
                foreach (var kvp in StartSceneLayouts())
                    startScenes[kvp.Key] = BuildStartScene(kvp.Key, kvp.Value, worldPrefabs[kvp.Key]);

                // ---- 5. LocationConfig wiring ----
                WireLocationConfigs(worldPrefabs, startScenes);

                // ---- 6. Remove stale materials from earlier builds ----
                foreach (string stale in new[]
                         {
                             $"{WorldMatFolder}/Arc_TEM_TEM_Atlas_1A_Source.mat",
                             $"{WorldMatFolder}/Arc_TEM_TEM_Atlas_1A_Source_2S.mat",
                             $"{WorldMatFolder}/Arc_TEM_TEM_Atlas_1A_Source_2S_Gold.mat",
                             $"{WorldMatFolder}/Arc_TEM_NoTex.mat",
                         })
                {
                    if (AssetDatabase.LoadAssetAtPath<Material>(stale) != null &&
                        AssetDatabase.DeleteAsset(stale))
                        Log($"Deleted stale material: {stale}");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorPrefs.SetInt(VersionPrefKey, BuildVersion);
                Log(Errors.Count == 0
                    ? "Build finished with no errors."
                    : $"Build finished with {Errors.Count} error(s) - see above.");
            }
            catch (Exception e)
            {
                LogError($"Build aborted: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                WriteReport();
            }
        }

        // ------------------------------------------------------------------
        // Asset selections
        // ------------------------------------------------------------------

        private static Dictionary<string, List<SceneryDef>> SceneryDefs()
        {
            return new Dictionary<string, List<SceneryDef>>
            {
                ["Seattle"] = new List<SceneryDef>
                {
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Pine_Tree_1A.prefab", Name = "Pine_Tree_1A", TargetSize = 7.0f },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Pine_Tree_2A.prefab", Name = "Pine_Tree_2A", TargetSize = 5.8f },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Pine_Tree_3B.prefab", Name = "Pine_Tree_3B", TargetSize = 7.5f },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Plants/Fern_1A.prefab", Name = "Fern_1A", TargetSize = 0.55f, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Tree_Log_1A.prefab", Name = "Tree_Log_1A", TargetSize = 0.5f },
                    new SceneryDef { SourcePath = $"{TS}/Rocks/Rock_Boulder_1B.prefab", Name = "Rock_Boulder_1B", TargetSize = 1.0f },
                    new SceneryDef { SourcePath = $"{TS}/Props/Signpost_1A.prefab", Name = "Signpost_1A", TargetSize = 1.6f, InSpawnList = false },
                },
                ["France"] = new List<SceneryDef>
                {
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Flowers_Patch_01A.prefab", Name = "TEM_Flowers_Patch_01A", TargetSize = 1.4f, Mode = SizeMode.Footprint, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Flowers_Patch_02A.prefab", Name = "TEM_Flowers_Patch_02A", TargetSize = 1.4f, Mode = SizeMode.Footprint, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_01A.prefab", Name = "TEM_Grass_Patch_01A", TargetSize = 1.2f, Mode = SizeMode.Footprint, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Flower_Bush_01A.prefab", Name = "TEM_Flower_Bush_01A", TargetSize = 0.9f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Tree_01A.prefab", Name = "TEM_Tree_01A", TargetSize = 4.5f },
                    new SceneryDef { SourcePath = $"{TEM}/Rocks/TEM_Rock_Small_01A.prefab", Name = "TEM_Rock_Small_01A", TargetSize = 0.5f },
                },
                ["Arizona"] = new List<SceneryDef>
                {
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Tall_1A.prefab", Name = "DS_Cactus_Tall_1A", TargetSize = 3.2f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Tall_3A.prefab", Name = "DS_Cactus_Tall_3A", TargetSize = 2.6f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Small_02A.prefab", Name = "DS_Cactus_Small_02A", TargetSize = 0.8f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Dry_Bush_01A.prefab", Name = "DS_Dry_Bush_01A", TargetSize = 0.7f, TwoSided = true },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Plant_Dry_01A.prefab", Name = "DS_Plant_Dry_01A", TargetSize = 0.55f },
                    new SceneryDef { SourcePath = $"{DS}/Rocks/Rocks/DS_Rock_Large_01A.prefab", Name = "DS_Rock_Large_01A", TargetSize = 1.5f },
                },
                ["Oregon"] = new List<SceneryDef>
                {
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Forest_Tree_2A.prefab", Name = "Forest_Tree_2A", TargetSize = 5.5f },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Forest_Tree_5A.prefab", Name = "Forest_Tree_5A", TargetSize = 4.2f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Bush_01A.prefab", Name = "TEM_Bush_01A", TargetSize = 1.0f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Bush_02A.prefab", Name = "TEM_Bush_02A", TargetSize = 1.1f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_04A.prefab", Name = "TEM_Grass_Patch_04A", TargetSize = 1.2f, Mode = SizeMode.Footprint, TwoSided = true, Tint = new Color(1f, 0.88f, 0.6f), TintSuffix = "_Gold" },
                    new SceneryDef { SourcePath = $"{TS}/Rocks/Rock_Medium_1A.prefab", Name = "Rock_Medium_1A", TargetSize = 0.8f },
                    new SceneryDef { SourcePath = $"{TS}/Props/Fence_2A.prefab", Name = "Fence_2A", TargetSize = 1.0f, InSpawnList = false },
                    new SceneryDef { SourcePath = $"{TS}/Props/Wood_Stack_1A.prefab", Name = "Wood_Stack_1A", TargetSize = 0.8f, InSpawnList = false },
                },
            };
        }

        // U-shaped layouts: heavy behind the start (negative Z), arms down both
        // sides, centre/front left open for the dog and the camera.
        private static Dictionary<string, List<StartSceneItem>> StartSceneLayouts()
        {
            return new Dictionary<string, List<StartSceneItem>>
            {
                ["Seattle"] = new List<StartSceneItem>
                {
                    new StartSceneItem("Pine_Tree_1A", 0f, -9f, 15f, 1.05f),
                    new StartSceneItem("Pine_Tree_3B", -4.8f, -8.2f, 140f, 0.95f),
                    new StartSceneItem("Pine_Tree_2A", 5.0f, -8.6f, 220f, 1.0f),
                    new StartSceneItem("Pine_Tree_1A", -9.0f, -9.5f, 60f, 0.9f),
                    new StartSceneItem("Pine_Tree_3B", 9.5f, -9.0f, 300f, 1.0f),
                    new StartSceneItem("Pine_Tree_2A", -8.0f, -2.0f, 90f, 0.85f),
                    new StartSceneItem("Pine_Tree_1A", 8.5f, -1.5f, 180f, 0.9f),
                    new StartSceneItem("Pine_Tree_3B", -9.2f, 4.0f, 20f, 0.8f),
                    new StartSceneItem("Pine_Tree_1A", 9.5f, 5.0f, 270f, 0.85f),
                    new StartSceneItem("Pine_Tree_2A", -8.5f, 9.0f, 200f, 0.9f),
                    new StartSceneItem("Pine_Tree_3B", 9.0f, 9.5f, 80f, 0.95f),
                    new StartSceneItem("Tree_Log_1A", -4.6f, -5.5f, 25f, 1.0f),
                    new StartSceneItem("Rock_Boulder_1B", 4.2f, -5.2f, 290f, 1.0f),
                    new StartSceneItem("Rock_Boulder_1B", -6.5f, 1.5f, 150f, 0.7f),
                    new StartSceneItem("Fern_1A", -3.2f, -6.8f, 0f, 1.1f),
                    new StartSceneItem("Fern_1A", 3.5f, -6.4f, 80f, 1.0f),
                    new StartSceneItem("Fern_1A", 6.2f, -3.4f, 200f, 0.9f),
                    new StartSceneItem("Fern_1A", -6.0f, -3.8f, 120f, 1.0f),
                    new StartSceneItem("Fern_1A", 7.0f, 2.5f, 40f, 1.1f),
                    new StartSceneItem("Fern_1A", -7.2f, 6.5f, 310f, 0.95f),
                    new StartSceneItem("Signpost_1A", 5.6f, 2.2f, 205f, 1.0f),
                },
                ["France"] = new List<StartSceneItem>
                {
                    new StartSceneItem("TEM_Tree_01A", 0f, -8.5f, 0f, 1.0f),
                    new StartSceneItem("TEM_Tree_01A", -5.5f, -7.5f, 120f, 0.85f),
                    new StartSceneItem("TEM_Tree_01A", 6.0f, -8.0f, 240f, 0.9f),
                    new StartSceneItem("TEM_Tree_01A", -9.0f, -3.5f, 60f, 0.75f),
                    new StartSceneItem("TEM_Tree_01A", 9.0f, -3.0f, 180f, 0.8f),
                    new StartSceneItem("TEM_Tree_01A", -8.8f, 7.0f, 300f, 0.7f),
                    new StartSceneItem("TEM_Tree_01A", 9.0f, 8.0f, 90f, 0.75f),
                    new StartSceneItem("TEM_Flower_Bush_01A", -4.0f, -5.5f, 30f, 1.1f),
                    new StartSceneItem("TEM_Flower_Bush_01A", 4.5f, -5.2f, 210f, 1.0f),
                    new StartSceneItem("TEM_Flower_Bush_01A", -6.5f, 0f, 140f, 0.9f),
                    new StartSceneItem("TEM_Flower_Bush_01A", 7.0f, 0.5f, 320f, 1.0f),
                    new StartSceneItem("TEM_Flower_Bush_01A", -6.2f, 5.0f, 80f, 1.0f),
                    new StartSceneItem("TEM_Flower_Bush_01A", 6.5f, 5.5f, 170f, 0.9f),
                    new StartSceneItem("TEM_Flowers_Patch_01A", -3.0f, -6.5f, 0f, 1.2f),
                    new StartSceneItem("TEM_Flowers_Patch_01A", 3.2f, -6.2f, 90f, 1.1f),
                    new StartSceneItem("TEM_Flowers_Patch_01A", -5.0f, 2.5f, 45f, 1.0f),
                    new StartSceneItem("TEM_Flowers_Patch_01A", 5.2f, 3.0f, 270f, 1.0f),
                    new StartSceneItem("TEM_Flowers_Patch_02A", 3.9f, -7.6f, 130f, 1.0f),
                    new StartSceneItem("TEM_Flowers_Patch_02A", -6.8f, -2.5f, 200f, 1.1f),
                    new StartSceneItem("TEM_Flowers_Patch_02A", 7.2f, 8.8f, 60f, 1.0f),
                    new StartSceneItem("TEM_Grass_Patch_01A", -4.5f, 1.0f, 15f, 1.0f),
                    new StartSceneItem("TEM_Grass_Patch_01A", 5.0f, 1.2f, 190f, 1.1f),
                    new StartSceneItem("TEM_Grass_Patch_01A", -3.6f, -4.6f, 275f, 0.9f),
                    new StartSceneItem("TEM_Rock_Small_01A", 4.0f, -6.9f, 50f, 1.0f),
                    new StartSceneItem("TEM_Rock_Small_01A", -5.2f, -6.3f, 220f, 0.8f),
                },
                ["Arizona"] = new List<StartSceneItem>
                {
                    new StartSceneItem("DS_Cactus_Tall_1A", 0f, -9.0f, 0f, 1.05f),
                    new StartSceneItem("DS_Cactus_Tall_1A", -6.0f, -7.5f, 140f, 0.9f),
                    new StartSceneItem("DS_Cactus_Tall_1A", 6.5f, -8.0f, 250f, 1.0f),
                    new StartSceneItem("DS_Cactus_Tall_1A", -9.0f, 0f, 60f, 0.85f),
                    new StartSceneItem("DS_Cactus_Tall_1A", 9.5f, 1.0f, 180f, 0.9f),
                    new StartSceneItem("DS_Cactus_Tall_1A", -8.5f, 8.0f, 20f, 0.8f),
                    new StartSceneItem("DS_Cactus_Tall_1A", 9.0f, 9.0f, 300f, 0.85f),
                    new StartSceneItem("DS_Cactus_Tall_3A", -4.0f, -6.0f, 90f, 0.9f),
                    new StartSceneItem("DS_Cactus_Tall_3A", 4.5f, -5.8f, 270f, 0.85f),
                    new StartSceneItem("DS_Rock_Large_01A", 3.8f, -7.8f, 30f, 1.0f),
                    new StartSceneItem("DS_Rock_Large_01A", -7.8f, -3.5f, 210f, 0.8f),
                    new StartSceneItem("DS_Rock_Large_01A", 7.5f, 4.5f, 120f, 0.9f),
                    new StartSceneItem("DS_Dry_Bush_01A", -3.4f, -5.0f, 0f, 1.0f),
                    new StartSceneItem("DS_Dry_Bush_01A", 6.8f, -2.5f, 160f, 1.1f),
                    new StartSceneItem("DS_Dry_Bush_01A", -6.2f, 3.5f, 80f, 0.9f),
                    new StartSceneItem("DS_Dry_Bush_01A", 7.9f, 7.5f, 240f, 1.0f),
                    new StartSceneItem("DS_Plant_Dry_01A", -5.5f, -4.4f, 120f, 1.1f),
                    new StartSceneItem("DS_Plant_Dry_01A", 5.8f, 0.5f, 300f, 1.0f),
                    new StartSceneItem("DS_Plant_Dry_01A", -7.0f, 6.5f, 40f, 0.9f),
                    new StartSceneItem("DS_Cactus_Small_02A", 4.2f, -4.6f, 200f, 1.0f),
                    new StartSceneItem("DS_Cactus_Small_02A", -4.8f, 1.5f, 20f, 1.1f),
                    new StartSceneItem("DS_Cactus_Small_02A", 6.0f, 8.5f, 140f, 0.9f),
                },
                ["Oregon"] = new List<StartSceneItem>
                {
                    new StartSceneItem("Forest_Tree_2A", 0f, -8.8f, 0f, 1.0f),
                    new StartSceneItem("Forest_Tree_2A", -7.0f, -7.5f, 130f, 0.85f),
                    new StartSceneItem("Forest_Tree_5A", 7.5f, -8.0f, 220f, 0.9f),
                    new StartSceneItem("Forest_Tree_5A", -9.5f, 5.0f, 40f, 0.8f),
                    new StartSceneItem("Forest_Tree_2A", 9.5f, 6.0f, 310f, 0.75f),
                    // Fence lines running parallel to the track = vineyard rows
                    new StartSceneItem("Fence_2A", -6.5f, -4.0f, 90f, 1.0f),
                    new StartSceneItem("Fence_2A", -6.5f, -1.0f, 90f, 1.0f),
                    new StartSceneItem("Fence_2A", -6.5f, 2.0f, 90f, 1.0f),
                    new StartSceneItem("Fence_2A", 6.8f, -3.0f, 90f, 1.0f),
                    new StartSceneItem("Fence_2A", 6.8f, 0f, 90f, 1.0f),
                    new StartSceneItem("Fence_2A", 6.8f, 3.0f, 90f, 1.0f),
                    new StartSceneItem("TEM_Bush_01A", -8.2f, -4.5f, 10f, 1.0f),
                    new StartSceneItem("TEM_Bush_02A", -8.2f, -1.5f, 100f, 0.95f),
                    new StartSceneItem("TEM_Bush_01A", -8.2f, 1.5f, 190f, 1.05f),
                    new StartSceneItem("TEM_Bush_02A", 8.5f, -3.5f, 45f, 1.0f),
                    new StartSceneItem("TEM_Bush_01A", 8.5f, -0.5f, 135f, 1.1f),
                    new StartSceneItem("TEM_Bush_02A", 8.5f, 2.5f, 225f, 0.95f),
                    new StartSceneItem("TEM_Bush_01A", -3.8f, -6.5f, 60f, 1.0f),
                    new StartSceneItem("TEM_Bush_02A", 4.0f, -6.2f, 150f, 1.05f),
                    new StartSceneItem("Wood_Stack_1A", 4.8f, -4.8f, 205f, 1.0f),
                    new StartSceneItem("TEM_Grass_Patch_04A", -4.6f, -5.6f, 0f, 1.1f),
                    new StartSceneItem("TEM_Grass_Patch_04A", 3.4f, -5.0f, 80f, 1.0f),
                    new StartSceneItem("TEM_Grass_Patch_04A", -5.4f, 4.0f, 160f, 1.0f),
                    new StartSceneItem("TEM_Grass_Patch_04A", 5.8f, 5.0f, 240f, 1.1f),
                    new StartSceneItem("Rock_Medium_1A", -4.4f, -7.4f, 20f, 0.9f),
                    new StartSceneItem("Rock_Medium_1A", 5.5f, 8.0f, 290f, 0.8f),
                },
            };
        }

        // ------------------------------------------------------------------
        // Ground materials
        // ------------------------------------------------------------------

        private static void BuildGroundMaterials(Shader arcShader)
        {
            string grass1A = $"{TSTex}/TNA_Grass_1A_D.psd";
            string grass1C = $"{TSTex}/TNA_Grass_1C_D.psd";
            string dirt1A = $"{TSTex}/TNA_Dirt_1A_D.psd";
            string dirt1B = $"{TSTex}/TNA_Dirt_1B_D.psd";
            string sand1A = $"{TSTex}/TNA_Sand_1A_D.psd";
            string dust1A = $"{TSTex}/TNA_Dust_1A_D.psd";
            string dust1B = $"{TSTex}/TNA_Dust_1B_D.psd";

            // Existing materials keep their GUIDs (floor prefabs stay wired) but
            // get renamed and re-textured.
            // Tints sit below full white: the scene runs an intensity-2 sun plus
            // skybox ambient, which pushes up-facing surfaces towards clipping.
            RetextureGroundMat("TestGround", "Ground_Seattle", dirt1B, new Color(0.68f, 0.66f, 0.62f), 5f);
            RetextureGroundMat("TestGroundSides", "Ground_Seattle_Sides", grass1C, new Color(0.64f, 0.7f, 0.64f), 5f);
            RetextureGroundMat("TestFinishLine", "Ground_Seattle_Finish", dust1A, new Color(0.6f, 0.66f, 0.6f), 4f);
            RetextureGroundMat("TestGroundArizona", "Ground_Arizona", sand1A, new Color(0.74f, 0.64f, 0.5f), 3f);
            RetextureGroundMat("TestGroundArizonaSides", "Ground_Arizona_Sides", dust1B, new Color(0.72f, 0.54f, 0.4f), 3f);
            RetextureGroundMat("TestGroundArizonaFinishLine", "Ground_Arizona_Finish", dust1A, new Color(0.7f, 0.6f, 0.48f), 4f);

            CreateGroundMat(arcShader, "Ground_France", dirt1A, new Color(0.7f, 0.64f, 0.5f), 5f);
            CreateGroundMat(arcShader, "Ground_France_Sides", grass1A, new Color(0.64f, 0.68f, 0.52f), 5f);
            CreateGroundMat(arcShader, "Ground_France_Finish", dust1A, new Color(0.62f, 0.66f, 0.55f), 4f);
            CreateGroundMat(arcShader, "Ground_Oregon", dirt1A, new Color(0.64f, 0.55f, 0.44f), 5f);
            CreateGroundMat(arcShader, "Ground_Oregon_Sides", grass1A, new Color(0.72f, 0.6f, 0.4f), 5f);
            CreateGroundMat(arcShader, "Ground_Oregon_Finish", dust1A, new Color(0.68f, 0.62f, 0.5f), 4f);
        }

        private static void RetextureGroundMat(string oldName, string newName, string texPath, Color tint, float tiling)
        {
            string oldPath = $"{FloorMatFolder}/{oldName}.mat";
            string newPath = $"{FloorMatFolder}/{newName}.mat";
            string path = AssetDatabase.LoadAssetAtPath<Material>(newPath) != null ? newPath : oldPath;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                LogError($"Ground material not found: {path}");
                return;
            }

            if (path == oldPath)
            {
                string renameResult = AssetDatabase.RenameAsset(oldPath, newName);
                if (!string.IsNullOrEmpty(renameResult))
                    LogError($"Rename {oldName} -> {newName} failed: {renameResult}");
            }

            ApplyGroundProps(mat, texPath, tint, tiling);
            Log($"Ground material updated: {newName} ({Path.GetFileName(texPath)})");
        }

        private static void CreateGroundMat(Shader arcShader, string name, string texPath, Color tint, float tiling)
        {
            string path = $"{FloorMatFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(arcShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            ApplyGroundProps(mat, texPath, tint, tiling);
            Log($"Ground material created: {name} ({Path.GetFileName(texPath)})");
        }

        private static void ApplyGroundProps(Material mat, string texPath, Color tint, float tiling)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                LogError($"Ground texture not found: {texPath}");
                return;
            }

            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            // Legacy materials carry a stale _Cutoff from their URP Lit days and
            // some pack textures have junk alpha channels - floors must never clip
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0f);
            EditorUtility.SetDirty(mat);
        }

        // ------------------------------------------------------------------
        // Floor prefab copies for France / Oregon
        // ------------------------------------------------------------------

        private static void BuildFloorPrefabCopies()
        {
            CopyFloorPrefab("Seattle Floor", "France Floor", "Ground_Seattle", "Ground_France");
            CopyFloorPrefab("Seattle Floor Sides 1", "France Floor Sides", "Ground_Seattle_Sides", "Ground_France_Sides");
            CopyFloorPrefab("Seattle Finish Line Floor 1", "France Finish Line Floor", "Ground_Seattle_Finish", "Ground_France_Finish");
            CopyFloorPrefab("Seattle Floor", "Oregon Floor", "Ground_Seattle", "Ground_Oregon");
            CopyFloorPrefab("Seattle Floor Sides 1", "Oregon Floor Sides", "Ground_Seattle_Sides", "Ground_Oregon_Sides");
            CopyFloorPrefab("Seattle Finish Line Floor 1", "Oregon Finish Line Floor", "Ground_Seattle_Finish", "Ground_Oregon_Finish");
        }

        private static void CopyFloorPrefab(string srcName, string dstName, string oldMatName, string newMatName)
        {
            string srcPath = $"{FloorPrefabFolder}/{srcName}.prefab";
            string dstPath = $"{FloorPrefabFolder}/{dstName}.prefab";

            if (AssetDatabase.LoadAssetAtPath<GameObject>(dstPath) == null)
            {
                if (!AssetDatabase.CopyAsset(srcPath, dstPath))
                {
                    LogError($"Failed to copy {srcPath} -> {dstPath}");
                    return;
                }
            }

            Material oldMat = AssetDatabase.LoadAssetAtPath<Material>($"{FloorMatFolder}/{oldMatName}.mat");
            Material newMat = AssetDatabase.LoadAssetAtPath<Material>($"{FloorMatFolder}/{newMatName}.mat");
            if (oldMat == null || newMat == null)
            {
                LogError($"Missing materials for floor copy {dstName} ({oldMatName} -> {newMatName})");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(dstPath);
            try
            {
                int swapped = 0;
                foreach (Renderer r in contents.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == oldMat)
                        {
                            mats[i] = newMat;
                            swapped++;
                        }
                    }
                    r.sharedMaterials = mats;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, dstPath);
                Log($"Floor prefab: {dstName} ({swapped} material slot(s) -> {newMatName})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ------------------------------------------------------------------
        // Scenery prefab build
        // ------------------------------------------------------------------

        private static GameObject BuildSceneryPrefab(SceneryDef def, string folder, Shader arcShader)
        {
            string outPath = $"{folder}/{def.Name}.prefab";

            GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(def.SourcePath);
            if (src == null)
            {
                LogError($"Source prefab not found: {def.SourcePath}");
                return null;
            }

            GameObject model = UnityEngine.Object.Instantiate(src);
            model.name = def.Name + "_Model";
            GameObject root = null;

            try
            {
                model.transform.position = Vector3.zero;
                model.transform.rotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                // Keep only the highest-detail LOD (these packs are already low poly)
                foreach (LODGroup group in model.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = group.GetLODs();
                    if (lods.Length > 1)
                    {
                        var keep = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
                        for (int i = 1; i < lods.Length; i++)
                        {
                            foreach (Renderer r in lods[i].renderers)
                            {
                                if (r != null && !keep.Contains(r))
                                    UnityEngine.Object.DestroyImmediate(r.gameObject);
                            }
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(group);
                }

                foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(c);

                // Static scenery: no particle systems or animators from the packs
                foreach (ParticleSystem ps in model.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps != null && ps.gameObject != model)
                        UnityEngine.Object.DestroyImmediate(ps.gameObject);
                }
                foreach (Animator a in model.GetComponentsInChildren<Animator>(true))
                    UnityEngine.Object.DestroyImmediate(a);
                foreach (Animation a in model.GetComponentsInChildren<Animation>(true))
                    UnityEngine.Object.DestroyImmediate(a);

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    LogError($"No renderers in {def.SourcePath}");
                    return null;
                }

                foreach (Renderer r in renderers)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = GetOrCreateArcMaterial(mats[i], def, arcShader);
                    r.sharedMaterials = mats;
                }

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers.Skip(1))
                    bounds.Encapsulate(r.bounds);

                float sourceSize = def.Mode == SizeMode.Height
                    ? bounds.size.y
                    : Mathf.Max(bounds.size.x, bounds.size.z);
                if (sourceSize < 0.001f)
                {
                    LogError($"Degenerate bounds for {def.SourcePath}");
                    return null;
                }

                float scale = def.TargetSize / sourceSize;

                root = new GameObject(def.Name);
                root.AddComponent<ScrollableObject>();
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * scale;
                // Centre the footprint on the root and rest the bottom on y=0
                // (slightly sunk so nothing floats on the curved world)
                model.transform.localPosition = new Vector3(
                    -bounds.center.x * scale,
                    -bounds.min.y * scale - BottomSink,
                    -bounds.center.z * scale);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, outPath);
                Log($"Scenery prefab: {outPath} (scale {scale:F3}, {def.Mode.ToString().ToLower()} {def.TargetSize})");
                return prefab;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                else if (model != null) UnityEngine.Object.DestroyImmediate(model);
            }
        }

        private static readonly Dictionary<string, Material> ArcMatCache = new Dictionary<string, Material>();

        private static Material GetOrCreateArcMaterial(Material srcMat, SceneryDef def, Shader arcShader)
        {
            if (srcMat == null)
                return null;

            Texture tex = ResolveAlbedoTexture(srcMat);

            string packTag = PackTag(def.SourcePath);
            string texName = tex != null ? tex.name : "NoTex";
            string matName = $"Arc_{packTag}_{texName}{(def.TwoSided ? "_2S" : "")}{def.TintSuffix}";
            string path = $"{WorldMatFolder}/{matName}.mat";

            if (ArcMatCache.TryGetValue(matName, out Material cached) && cached != null)
                return cached;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(arcShader);
                AssetDatabase.CreateAsset(mat, path);
                Log($"Arc material: {path} (from {srcMat.name})");
            }

            // Foliage detection: leaf/flower/grass cards carry meaningful alpha
            // in their atlas (TEM vegetation). Those need cutout + double-sided;
            // solid-geometry atlases (TS/DS) have no alpha and stay opaque.
            bool texHasAlpha = false;
            if (tex != null)
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
                texHasAlpha = importer != null && importer.DoesSourceTextureHaveAlpha();
            }
            bool foliage = def.TwoSided || texHasAlpha;

            Color tint = def.Tint;
            if (tint == Color.white && tex != null && tex.name.StartsWith("TEM_Atlas_Vegetation"))
                tint = new Color(0.7f, 0.78f, 0.64f);

            mat.shader = arcShader;
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", foliage ? 0f : 2f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", foliage ? 0.5f : 0f);
            EditorUtility.SetDirty(mat);

            ArcMatCache[matName] = mat;
            return mat;
        }

        /// <summary>
        /// The packs disagree on which property holds the colour atlas: the TEM
        /// shaders sample _MainTexture (their _MainTex slot holds a stale
        /// black-ish "source" atlas), while Toon Series / Toon Desert use
        /// _MainTex. Only trust properties the material's shader declares.
        /// </summary>
        private static Texture ResolveAlbedoTexture(Material srcMat)
        {
            string[] candidates = { "_MainTexture", "_MainTex", "_BaseMap", "_Albedo" };
            foreach (string prop in candidates)
            {
                if (srcMat.HasProperty(prop))
                {
                    Texture tex = srcMat.GetTexture(prop);
                    if (tex != null)
                        return tex;
                }
            }
            return srcMat.mainTexture;
        }

        private static string PackTag(string assetPath)
        {
            if (assetPath.StartsWith("Assets/Toon Desert")) return "DS";
            if (assetPath.StartsWith("Assets/Toon Enchanted Meadow")) return "TEM";
            if (assetPath.StartsWith("Assets/Toon Series")) return "TS";
            return "Misc";
        }

        // ------------------------------------------------------------------
        // StartScene build
        // ------------------------------------------------------------------

        private static readonly Vector3[] FloorPadPositions =
        {
            new Vector3(0f, 0f, -7f), new Vector3(0f, 0f, 5f),
            new Vector3(12f, 0f, -7f), new Vector3(12f, 0f, 5f),
            new Vector3(-12f, 0f, -7f), new Vector3(-12f, 0f, 5f),
        };

        private static GameObject BuildStartScene(string world, List<StartSceneItem> items, Dictionary<string, GameObject> prefabs)
        {
            string outPath = $"{StartSceneFolder}/StartScene_{world}.prefab";

            string floorName = world == "Seattle" ? "Seattle Floor"
                : world == "Arizona" ? "ArizonaFloor"
                : $"{world} Floor";
            GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FloorPrefabFolder}/{floorName}.prefab");
            if (floorPrefab == null)
                LogError($"StartScene {world}: floor prefab '{floorName}' not found");

            GameObject root = new GameObject($"StartScene_{world}");
            try
            {
                root.tag = "Floor";
                root.AddComponent<ScrollableObject>();

                if (floorPrefab != null)
                {
                    // Same 3x2 pad as the original StartScene1
                    foreach (Vector3 pos in FloorPadPositions)
                    {
                        GameObject tile = (GameObject)PrefabUtility.InstantiatePrefab(floorPrefab, root.transform);
                        tile.transform.localPosition = pos;
                        tile.transform.localRotation = Quaternion.identity;
                        tile.transform.localScale = Vector3.one * 1.2f;
                        // The pad must not scroll on its own - the root moves it
                        foreach (ScrollableObject s in tile.GetComponentsInChildren<ScrollableObject>())
                            UnityEngine.Object.DestroyImmediate(s, false);
                    }
                }

                foreach (StartSceneItem item in items)
                {
                    if (!prefabs.TryGetValue(item.Key, out GameObject prefab) || prefab == null)
                    {
                        LogError($"StartScene {world}: missing scenery prefab '{item.Key}'");
                        continue;
                    }
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    inst.transform.localPosition = new Vector3(item.X, 0f, item.Z);
                    inst.transform.localRotation = Quaternion.Euler(0f, item.RotY, 0f);
                    inst.transform.localScale = Vector3.one * item.Scale;
                    foreach (ScrollableObject s in inst.GetComponentsInChildren<ScrollableObject>())
                        UnityEngine.Object.DestroyImmediate(s, false);
                }

                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, outPath);
                Log($"StartScene prefab: {outPath} ({items.Count} scenery items + floor pad)");
                return prefabAsset;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------
        // LocationConfig wiring
        // ------------------------------------------------------------------

        private static void WireLocationConfigs(
            Dictionary<string, Dictionary<string, GameObject>> worldPrefabs,
            Dictionary<string, GameObject> startScenes)
        {
            var spawnTuning = new Dictionary<string, (int min, int max, float dist, int pool)>
            {
                ["Seattle"] = (1, 4, 2.5f, 20),
                ["France"] = (2, 5, 1.5f, 20),
                ["Arizona"] = (1, 3, 2.0f, 20),
                ["Oregon"] = (1, 4, 2.0f, 20),
            };

            foreach (string world in worldPrefabs.Keys)
            {
                string configPath = $"Assets/LevelsData/Level Locations/{world}.asset";
                var config = AssetDatabase.LoadAssetAtPath<LocationConfig>(configPath);
                if (config == null)
                {
                    LogError($"LocationConfig not found: {configPath}");
                    continue;
                }

                var so = new SerializedObject(config);

                List<GameObject> spawnList = SceneryDefs()[world]
                    .Where(d => d.InSpawnList)
                    .Select(d => worldPrefabs[world].TryGetValue(d.Name, out GameObject p) ? p : null)
                    .Where(p => p != null)
                    .ToList();

                SerializedProperty arr = so.FindProperty("sceneryPrefabs");
                arr.arraySize = spawnList.Count;
                for (int i = 0; i < spawnList.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = spawnList[i];

                if (startScenes.TryGetValue(world, out GameObject startScene) && startScene != null)
                    so.FindProperty("startScenePrefab").objectReferenceValue = startScene;

                (int min, int max, float dist, int pool) = spawnTuning[world];
                so.FindProperty("minSceneryPerFloor").intValue = min;
                so.FindProperty("maxSceneryPerFloor").intValue = max;
                so.FindProperty("sceneryMinDistance").floatValue = dist;
                so.FindProperty("sceneryPoolSize").intValue = pool;

                if (world == "France" || world == "Oregon")
                {
                    so.FindProperty("mainFloorPrefab").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>($"{FloorPrefabFolder}/{world} Floor.prefab");
                    so.FindProperty("sideFloorPrefab").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>($"{FloorPrefabFolder}/{world} Floor Sides.prefab");
                    so.FindProperty("finishLineFloorPrefab").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<GameObject>($"{FloorPrefabFolder}/{world} Finish Line Floor.prefab");
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
                Log($"LocationConfig wired: {world} ({spawnList.Count} scenery prefabs, scenery {min}-{max}/floor, minDist {dist})");
            }
        }

        // ------------------------------------------------------------------
        // Logging
        // ------------------------------------------------------------------

        private static void Log(string message)
        {
            Debug.Log($"[WorldSceneryBuilder] {message}");
            Report.AppendLine(message);
        }

        private static void LogError(string message)
        {
            Debug.LogError($"[WorldSceneryBuilder] {message}");
            Report.AppendLine($"ERROR: {message}");
            Errors.Add(message);
        }

        private static void WriteReport()
        {
            try
            {
                string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "WorldSceneryBuild.txt"), Report.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"[WorldSceneryBuilder] Failed to write report: {e.Message}");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
