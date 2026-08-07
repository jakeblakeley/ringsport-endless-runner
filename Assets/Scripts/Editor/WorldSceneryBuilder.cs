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
        private const int BuildVersion = 27;
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
        private const string PNB = "Assets/PolygonNatureBiomes/PNB_Tropical_Jungle/Prefabs";
        private const string PNBTerrain = "Assets/PolygonNatureBiomes/PNB_Tropical_Jungle/Terrain";

        private const string GrassShaderName = "Custom/Mobile/GrassBlades";
        private const string SkyShaderName = "Ringsport/Gradient Skybox";
        private const string LocationConfigFolder = "Assets/LevelsData/Level Locations";

        private const float BottomSink = 0.02f;

        private static readonly Color LavaRock = new Color(0.3f, 0.29f, 0.28f);

        private static readonly StringBuilder Report = new StringBuilder();
        private static readonly List<string> Errors = new List<string>();

        // Stand-in ambience for a world that ships without any. Tracked in
        // SOUND_EFFECTS.md - tropical birds are a placeholder for surf.
        private static readonly Dictionary<string, string> LocationAmbience = new Dictionary<string, string>
        {
            ["Hawaii"] = "Assets/Sounds/Ambient/ambient birds1.wav",
        };

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
            // Which LOD of a multi-LOD source to keep. The toon packs are single
            // -LOD art so 0 is right there; the Synty packs ship LOD0-LOD3 where
            // LOD0 carries detail no scrolling prop ever shows through the fog.
            public int LodIndex;
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
            ArcTexturePaths.Clear();
            Log($"World scenery build v{BuildVersion} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                Shader arcShader = Shader.Find(ArcShaderName);
                if (arcShader == null)
                    throw new Exception($"Shader '{ArcShaderName}' not found");

                EnsureFolder(WorldMatFolder);

                // ---- 1. Ground materials (rename Test* in place + create France/Oregon/Hawaii) ----
                BuildGroundMaterials(arcShader);

                // ---- 1b. Skyboxes this builder owns ----
                BuildSkyboxes();

                // ---- 2. France/Oregon/Hawaii floor prefab copies ----
                BuildFloorPrefabCopies();

                // ---- 2b. Side-floor grass patches (material + density per world) ----
                BuildGrassPatches();

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

                // ---- 5b. Level -> location assignments ----
                RemapLevelLocation("Assets/LevelsData/Level Data/Level2 - Ring 1 Leg 1.asset",
                    Location.Oregon, $"{LocationConfigFolder}/Oregon.asset");
                RemapLevelLocation("Assets/LevelsData/Level Data/Level4 - Ring 2 Leg 1.asset",
                    Location.Hawaii, $"{LocationConfigFolder}/Hawaii.asset");

                // ---- 5c. Performance: GPU instancing + capped atlas sizes ----
                ApplyPerformanceSettings();

                // ---- 5d. Arc curvature is global-only now; keep the look the
                //          legacy floor material defined (strength 20/100) ----
                ApplyArcControllerValues(20f, 100f);

                // ---- 5e. Despawn margin: tiles despawned when their CENTER passed
                //          player-10, leaving the far edge visible at the bottom of
                //          the frame as it popped. -18 hides a 12-unit tile fully. ----
                ApplyDespawnDistance(-18f);

                // ---- 5f. Love note attention beacon ----
                BuildLoveNoteBeacon();

                // ---- 5g. Collectibles never cast shadows (mobile fill-rate) ----
                DisableCollectibleShadows();

                // ---- 5h. Re-bake the skybox ambient probes. Driven from here
                //          rather than left to LocationAmbientBake's own auto-run:
                //          delayCall order across [InitializeOnLoad] classes is
                //          effectively alphabetical, so the bake would otherwise
                //          run before a new location's config and sky exist. ----
                EditorTools.LocationAmbientBake.Bake();

                // ---- 6. Remove stale materials from earlier builds ----
                foreach (string stale in new[]
                         {
                             $"{WorldMatFolder}/Arc_TEM_TEM_Atlas_1A_Source.mat",
                             $"{WorldMatFolder}/Arc_TEM_TEM_Atlas_1A_Source_2S.mat",
                             $"{WorldMatFolder}/Arc_TEM_TEM_Atlas_1A_Source_2S_Gold.mat",
                             $"{WorldMatFolder}/Arc_TEM_NoTex.mat",
                             // v14 built this white before the tropical rocks
                             // got their lava tint
                             $"{WorldMatFolder}/Arc_PNB_NoTex.mat",
                             // Hawaii's shader grass was cut - the mesh clumps
                             // are the dune vegetation now
                             $"{FloorMatFolder}/Grass_Hawaii.mat",
                         })
                {
                    // Never delete something this run just built. Arc_PNB_NoTex
                    // went stale when the tropical rocks got their lava tint and
                    // came straight back when a prop turned up whose material
                    // points at a texture guid the pack never shipped - deleting
                    // it left a live prefab pointing at nothing, which renders as
                    // magenta and looks like a broken shader.
                    string staleName = Path.GetFileNameWithoutExtension(stale);
                    if (ArcMatCache.ContainsKey(staleName))
                    {
                        Log($"Kept {staleName}: in use by this build, no longer stale");
                        continue;
                    }
                    if (AssetDatabase.LoadAssetAtPath<Material>(stale) != null &&
                        AssetDatabase.DeleteAsset(stale))
                        Log($"Deleted stale material: {stale}");
                }

                // ---- 6b. Remove scenery prefabs dropped from the defs ----
                foreach (string stale in new[]
                         {
                             // Oregon's warm autumn trees, replaced by the cool D variants
                             $"{WorldPrefabFolder}/Oregon/Forest_Tree_2A.prefab",
                             $"{WorldPrefabFolder}/Oregon/Forest_Tree_5A.prefab",
                         })
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(stale) != null &&
                        AssetDatabase.DeleteAsset(stale))
                        Log($"Deleted stale scenery prefab: {stale}");
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
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_03A.prefab", Name = "TEM_Grass_Patch_03A", TargetSize = 1.1f, Mode = SizeMode.Footprint, TwoSided = true, Tint = new Color(0.48f, 0.62f, 0.46f), TintSuffix = "_Forest" },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Mushrooms/Mushroom_1A.prefab", Name = "Mushroom_1A", TargetSize = 0.3f },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Plants/Fern_2A.prefab", Name = "Fern_2A", TargetSize = 0.5f, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Mushrooms/Mushroom_1C.prefab", Name = "Mushroom_1C", TargetSize = 0.28f },
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
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_05A.prefab", Name = "TEM_Grass_Patch_05A", TargetSize = 1.1f, Mode = SizeMode.Footprint, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Flowers_Patch_03A.prefab", Name = "TEM_Flowers_Patch_03A", TargetSize = 1.3f, Mode = SizeMode.Footprint, TwoSided = true },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_02A.prefab", Name = "TEM_Grass_Patch_02A", TargetSize = 1.1f, Mode = SizeMode.Footprint, TwoSided = true },
                },
                ["Arizona"] = new List<SceneryDef>
                {
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Tall_1A.prefab", Name = "DS_Cactus_Tall_1A", TargetSize = 3.2f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Tall_3A.prefab", Name = "DS_Cactus_Tall_3A", TargetSize = 2.6f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Small_02A.prefab", Name = "DS_Cactus_Small_02A", TargetSize = 0.8f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Dry_Bush_01A.prefab", Name = "DS_Dry_Bush_01A", TargetSize = 0.7f, TwoSided = true },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Plant_Dry_01A.prefab", Name = "DS_Plant_Dry_01A", TargetSize = 0.55f },
                    new SceneryDef { SourcePath = $"{DS}/Rocks/Rocks/DS_Rock_Large_01A.prefab", Name = "DS_Rock_Large_01A", TargetSize = 1.5f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Plant_Dry_03A.prefab", Name = "DS_Plant_Dry_03A", TargetSize = 0.5f },
                    new SceneryDef { SourcePath = $"{DS}/Rocks/Rocks/DS_Rock_Small_02A.prefab", Name = "DS_Rock_Small_02A", TargetSize = 0.35f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Cactus_Small_05A.prefab", Name = "DS_Cactus_Small_05A", TargetSize = 0.7f },
                    new SceneryDef { SourcePath = $"{DS}/Vegetation/DS_Plant_Dry_02A.prefab", Name = "DS_Plant_Dry_02A", TargetSize = 0.5f },
                },
                ["Oregon"] = new List<SceneryDef>
                {
                    // The pack's trees are UV variants on one colour-swatch atlas:
                    // the A/B/C letters land on warm olive/gold/rust swatches, D on
                    // the cool teal-greens. Same mesh, same material - a free swap.
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Forest_Tree_8D.prefab", Name = "Forest_Tree_8D", TargetSize = 5.5f },
                    new SceneryDef { SourcePath = $"{TS}/Vegetation/Trees/Forest_Tree_5D.prefab", Name = "Forest_Tree_5D", TargetSize = 4.2f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Bush_01A.prefab", Name = "TEM_Bush_01A", TargetSize = 1.0f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Bush_02A.prefab", Name = "TEM_Bush_02A", TargetSize = 1.1f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_04A.prefab", Name = "TEM_Grass_Patch_04A", TargetSize = 1.2f, Mode = SizeMode.Footprint, TwoSided = true, Tint = new Color(1f, 0.88f, 0.6f), TintSuffix = "_Gold" },
                    new SceneryDef { SourcePath = $"{TS}/Rocks/Rock_Medium_1A.prefab", Name = "Rock_Medium_1A", TargetSize = 0.8f },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_02A.prefab", Name = "TEM_Grass_Patch_02A", TargetSize = 1.1f, Mode = SizeMode.Footprint, TwoSided = true, Tint = new Color(1f, 0.88f, 0.6f), TintSuffix = "_Gold" },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_05A.prefab", Name = "TEM_Grass_Patch_05A", TargetSize = 1.0f, Mode = SizeMode.Footprint, TwoSided = true, Tint = new Color(1f, 0.88f, 0.6f), TintSuffix = "_Gold" },
                    new SceneryDef { SourcePath = $"{TEM}/Vegetation/TEM_Grass_Patch_03A.prefab", Name = "TEM_Grass_Patch_03A", TargetSize = 1.0f, Mode = SizeMode.Footprint, TwoSided = true, Tint = new Color(1f, 0.88f, 0.6f), TintSuffix = "_Gold" },
                    new SceneryDef { SourcePath = $"{TS}/Rocks/Rock_Small_1A.prefab", Name = "Rock_Small_1A", TargetSize = 0.45f },
                    new SceneryDef { SourcePath = $"{TS}/Props/Fence_2A.prefab", Name = "Fence_2A", TargetSize = 1.0f, InSpawnList = false },
                    new SceneryDef { SourcePath = $"{TS}/Props/Wood_Stack_1A.prefab", Name = "Wood_Stack_1A", TargetSize = 0.8f, InSpawnList = false },
                },
                // Beach, not jungle: trees for the silhouette, driftwood and rock
                // at ankle height, and nothing dense enough to wall the track in.
                // LOD1 on every tree - they are the props repeated dozens of
                // times and their LOD0 leaf detail never survives the fog.
                //
                // The spawner picks uniformly from this list, so a type's share
                // of the list IS its share of the beach. Six tree shapes out of
                // eighteen entries keeps trees near a third of what spawns while
                // making a back-to-back repeat rare - two palms of the same kind
                // in shot at once was the whole problem with the first pass.
                //
                // Skipped on purpose: the pack's flowers, flowering bushes and
                // red palm take their colour from the Synty shader's flat-colour
                // switch (red over a green atlas), so on the arc shader they come
                // through as plain green leaves. Palm_01_Coconuts_01 is a coconut
                // cluster meant to be attached to a trunk, not a tree.
                ["Hawaii"] = new List<SceneryDef>
                {
                    // Sized down from the 7.2 a pine gets: a palm's fronds spread
                    // nearly as wide as the trunk is tall, so height-matching one
                    // to a conifer hangs leaves over the racing line.
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Palm_01.prefab", Name = "SM_Env_Tree_Palm_01", TargetSize = 6.2f, LodIndex = 1 },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Palm_02.prefab", Name = "SM_Env_Tree_Palm_02", TargetSize = 5.8f, LodIndex = 1 },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Palm_03.prefab", Name = "SM_Env_Tree_Palm_03", TargetSize = 5.2f, LodIndex = 1 },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Palm_04.prefab", Name = "SM_Env_Tree_Palm_04", TargetSize = 4.8f, LodIndex = 1 },
                    // Two non-palm silhouettes. A row of palms reads as one tree
                    // however many variants it has - the broad-canopy pohutukawa
                    // and the squat banana are what actually break the skyline up.
                    //
                    // The 03/04 pohutukawas, not 01/02: those two hang their
                    // canopy on a second `Branches_01` material whose texture guid
                    // was never shipped with the pack, so the branch half renders
                    // flat white - and 02's LOD1 is 6312 tris across 2 renderers
                    // against 04's 2100 across 1.
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Pohutukawa_04.prefab", Name = "SM_Env_Tree_Pohutukawa_04", TargetSize = 4.6f, LodIndex = 1 },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Banana_02.prefab", Name = "SM_Env_Tree_Banana_02", TargetSize = 3.6f, LodIndex = 1 },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Bush_Palm_01.prefab", Name = "SM_Env_Bush_Palm_01", TargetSize = 1.5f },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Bush_Palm_02.prefab", Name = "SM_Env_Bush_Palm_02", TargetSize = 1.6f },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Bush_Palm_04.prefab", Name = "SM_Env_Bush_Palm_04", TargetSize = 1.2f },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Bush_Tropical_01.prefab", Name = "SM_Env_Bush_Tropical_01", TargetSize = 1.3f },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Grass_Med_Clump_02.prefab", Name = "SM_Env_Grass_Med_Clump_02", TargetSize = 0.9f },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Grass_Short_Clump_01.prefab", Name = "SM_Env_Grass_Short_Clump_01", TargetSize = 0.5f },
                    // Driftwood is a long low log: matched on height it scales up
                    // until the footprint swallows half a floor tile, so these
                    // are sized across the ground instead.
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_DriftWood_02.prefab", Name = "SM_Env_DriftWood_02", TargetSize = 1.6f, Mode = SizeMode.Footprint },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_DriftWood_03.prefab", Name = "SM_Env_DriftWood_03", TargetSize = 1.7f, Mode = SizeMode.Footprint },
                    // Seaweed_Beach_01 held a slot here and was cut: it is a flat
                    // dark decal that disappears into sand at any distance.
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_DriftWood_05.prefab", Name = "SM_Env_DriftWood_05", TargetSize = 1.9f, Mode = SizeMode.Footprint },
                    // Every rock in the tropical pack is triplanar-projected, so
                    // it carries no UV-mapped albedo for the arc shader to sample.
                    // No loss: the texture it projects is flat to within a 0.013
                    // standard deviation, so a solid colour is the same picture
                    // for one fewer 2048 atlas. Volcanic dark rather than the
                    // pack's grey-brown - it is what a Hawaiian beach actually
                    // has, and it reads against pale sand where grey-brown would
                    // disappear into it.
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Rock_Round_01.prefab", Name = "SM_Env_Rock_Round_01", TargetSize = 0.8f, Tint = LavaRock, TintSuffix = "_Lava" },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Rock_Small_01.prefab", Name = "SM_Env_Rock_Small_01", TargetSize = 0.35f, Tint = LavaRock, TintSuffix = "_Lava" },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Rock_Flat_01.prefab", Name = "SM_Env_Rock_Flat_01", TargetSize = 1.2f, Mode = SizeMode.Footprint, Tint = LavaRock, TintSuffix = "_Lava" },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_Tree_Pohutukawa_03.prefab", Name = "SM_Env_Tree_Pohutukawa_03", TargetSize = 5.0f, LodIndex = 1, InSpawnList = false },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Env_DriftWood_04.prefab", Name = "SM_Env_DriftWood_04", TargetSize = 2.2f, Mode = SizeMode.Footprint, InSpawnList = false },
                    new SceneryDef { SourcePath = $"{PNB}/SM_Prop_Starfish_01.prefab", Name = "SM_Prop_Starfish_01", TargetSize = 0.5f, Mode = SizeMode.Footprint, InSpawnList = false },
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
                    new StartSceneItem("Forest_Tree_8D", 0f, -8.8f, 0f, 1.0f),
                    new StartSceneItem("Forest_Tree_8D", -7.0f, -7.5f, 130f, 0.85f),
                    new StartSceneItem("Forest_Tree_5D", 7.5f, -8.0f, 220f, 0.9f),
                    new StartSceneItem("Forest_Tree_5D", -9.5f, 5.0f, 40f, 0.8f),
                    new StartSceneItem("Forest_Tree_8D", 9.5f, 6.0f, 310f, 0.75f),
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
                // A palm grove behind the line opening onto bare sand: fewer,
                // bigger pieces than the other worlds so the beach reads empty.
                // The camera sits at (0, 4, -5) looking forward, and a palm's
                // fronds reach ~4 units out from its trunk - so nothing taller
                // than a bush goes inside |x| = 6, and the trunks that frame the
                // shot sit behind z = -8 where the fronds clear the lens.
                ["Hawaii"] = new List<StartSceneItem>
                {
                    new StartSceneItem("SM_Env_Tree_Palm_02", -6.4f, -9.4f, 20f, 1.0f),
                    new StartSceneItem("SM_Env_Tree_Palm_01", 6.8f, -9.0f, 210f, 0.95f),
                    new StartSceneItem("SM_Env_Tree_Pohutukawa_03", -9.6f, -8.0f, 130f, 0.9f),
                    new StartSceneItem("SM_Env_Tree_Palm_03", 9.8f, -8.4f, 300f, 1.0f),
                    new StartSceneItem("SM_Env_Tree_Palm_04", -9.2f, -1.5f, 60f, 0.85f),
                    new StartSceneItem("SM_Env_Tree_Pohutukawa_04", 9.4f, -0.5f, 240f, 0.8f),
                    new StartSceneItem("SM_Env_Tree_Banana_02", -8.8f, 6.5f, 15f, 0.9f),
                    new StartSceneItem("SM_Env_Tree_Palm_01", 9.0f, 7.5f, 190f, 0.8f),
                    new StartSceneItem("SM_Env_Bush_Palm_02", -4.2f, -8.2f, 40f, 1.0f),
                    new StartSceneItem("SM_Env_Bush_Palm_04", 4.0f, -8.0f, 220f, 1.1f),
                    new StartSceneItem("SM_Env_Bush_Palm_01", -7.0f, 0.5f, 150f, 0.9f),
                    new StartSceneItem("SM_Env_Bush_Tropical_01", 7.2f, 1.5f, 320f, 1.0f),
                    new StartSceneItem("SM_Env_Bush_Palm_02", 6.8f, 8.5f, 80f, 0.85f),
                    new StartSceneItem("SM_Env_DriftWood_04", -3.4f, -6.9f, 105f, 1.0f),
                    new StartSceneItem("SM_Env_DriftWood_02", 5.2f, -3.2f, 25f, 1.0f),
                    new StartSceneItem("SM_Env_DriftWood_02", -6.4f, 4.5f, 200f, 0.9f),
                    new StartSceneItem("SM_Env_Rock_Round_01", 3.6f, -7.4f, 55f, 1.0f),
                    new StartSceneItem("SM_Env_Rock_Round_01", -7.8f, -1.8f, 235f, 0.8f),
                    new StartSceneItem("SM_Env_Rock_Small_01", -4.8f, -4.2f, 0f, 1.1f),
                    new StartSceneItem("SM_Env_Rock_Small_01", 6.0f, 4.8f, 170f, 0.9f),
                    new StartSceneItem("SM_Env_Grass_Med_Clump_02", -5.2f, -6.4f, 30f, 1.1f),
                    new StartSceneItem("SM_Env_Grass_Med_Clump_02", 4.6f, -6.6f, 250f, 1.0f),
                    new StartSceneItem("SM_Env_Grass_Short_Clump_01", -6.6f, 2.8f, 90f, 1.0f),
                    new StartSceneItem("SM_Env_Grass_Short_Clump_01", 7.6f, -5.0f, 300f, 1.1f),
                    new StartSceneItem("SM_Env_DriftWood_05", 5.4f, 2.0f, 65f, 1.0f),
                    new StartSceneItem("SM_Prop_Starfish_01", -4.6f, 5.0f, 140f, 1.0f),
                },
            };
        }

        // ------------------------------------------------------------------
        // Ground materials
        // ------------------------------------------------------------------

        private static void BuildGroundMaterials(Shader arcShader)
        {
            string grass1A = $"{TSTex}/TNA_Grass_1A_D.png";
            string grass1C = $"{TSTex}/TNA_Grass_1C_D.png";
            string dirt1A = $"{TSTex}/TNA_Dirt_1A_D.png";
            string dirt1B = $"{TSTex}/TNA_Dirt_1B_D.png";
            string sand1A = $"{TSTex}/TNA_Sand_1A_D.png";
            string dust1A = $"{TSTex}/TNA_Dust_1A_D.png";
            string dust1B = $"{TSTex}/TNA_Dust_1B_D.png";

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

            // Hawaii runs on the tropical pack's own sand so the ground and the
            // props share a palette: flat sand down the racing line, wind ripples
            // on the dunes either side, wet pebbled sand at the finish. Tints pull
            // the blue channel UP rather than down - the raw texture is a warm
            // desert tan, and lifting blue bleaches it toward beach sand instead
            // of leaving Hawaii looking like a second Arizona. These land at
            // roughly 0.55 effective albedo, the brightest floor in the game and
            // still short of clipping under the scene's intensity-2 sun.
            string sandFlat = $"{PNBTerrain}/Sand_Texture_01.png";
            string sandWavy = $"{PNBTerrain}/Sand_Wavy_Texture_01.png";
            string sandPebbles = $"{PNBTerrain}/Sand_Pebbles_Texture_01.png";
            CreateGroundMat(arcShader, "Ground_Hawaii", sandFlat, new Color(0.74f, 0.8f, 0.9f), 4f);
            CreateGroundMat(arcShader, "Ground_Hawaii_Sides", sandWavy, new Color(0.74f, 0.8f, 0.89f), 3f);
            CreateGroundMat(arcShader, "Ground_Hawaii_Finish", sandPebbles, new Color(0.7f, 0.76f, 0.86f), 4f);
        }

        // ------------------------------------------------------------------
        // Skyboxes
        // ------------------------------------------------------------------

        private static void BuildSkyboxes()
        {
            // Arizona's and Oregon's gradient skies already exist; only the new
            // world needs one. Deep tropical blue overhead falling to a bright
            // haze at the horizon, so the sand reads as sunlit rather than lit
            // by a flat blue dome.
            CreateGradientSky("Sky_Hawaii_Gradient",
                top: new Color(0.06f, 0.38f, 0.72f),
                horizon: new Color(0.7f, 0.89f, 0.94f),
                bottom: new Color(0.52f, 0.78f, 0.82f),
                upExponent: 0.8f,
                downExponent: 1.6f);
        }

        private static void CreateGradientSky(string name, Color top, Color horizon, Color bottom,
            float upExponent, float downExponent)
        {
            Shader skyShader = Shader.Find(SkyShaderName);
            if (skyShader == null)
            {
                LogError($"Shader '{SkyShaderName}' not found - skipping {name}");
                return;
            }

            string path = $"{WorldMatFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(skyShader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = skyShader;
            mat.SetColor("_TopColor", top);
            mat.SetColor("_HorizonColor", horizon);
            mat.SetColor("_BottomColor", bottom);
            mat.SetFloat("_UpExponent", upExponent);
            mat.SetFloat("_DownExponent", downExponent);
            mat.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(mat);
            Log($"Gradient sky: {path}");
        }

        // ------------------------------------------------------------------
        // Side-floor grass patches
        // ------------------------------------------------------------------

        /// <summary>
        /// The blade patch that hides the floor seam is a GrassPatch child on
        /// each side-floor prefab, inherited by any prefab copied from Seattle's.
        /// Hawaii does not want it: sand meets sand at that seam, so there is
        /// nothing to hide, and the pack's own mesh grass clumps carry what
        /// vegetation the dunes need. The other four worlds' patches were tuned
        /// by hand and are deliberately left alone.
        /// </summary>
        private static void BuildGrassPatches()
        {
            RemoveGrassChild("Hawaii Floor Sides");
        }

        private static void RemoveGrassChild(string sideFloorName)
        {
            string path = $"{FloorPrefabFolder}/{sideFloorName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                LogError($"Grass removal: side floor prefab not found: {path}");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                GrassPatch[] patches = contents.GetComponentsInChildren<GrassPatch>(true);
                if (patches.Length == 0)
                    return;

                foreach (GrassPatch patch in patches)
                    UnityEngine.Object.DestroyImmediate(patch.gameObject);

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                Log($"Grass patch removed from {sideFloorName} ({patches.Length} child object(s))");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
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
            mat.DisableKeyword("_ALPHATEST_ON"); // opaque: keep the clip()-free variant (early-Z)
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
            CopyFloorPrefab("Seattle Floor", "Hawaii Floor", "Ground_Seattle", "Ground_Hawaii");
            CopyFloorPrefab("Seattle Floor Sides 1", "Hawaii Floor Sides", "Ground_Seattle_Sides", "Ground_Hawaii_Sides");
            CopyFloorPrefab("Seattle Finish Line Floor 1", "Hawaii Finish Line Floor", "Ground_Seattle_Finish", "Ground_Hawaii_Finish");
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

                // Collapse the LOD chain to the one level this def wants. Scenery
                // is pooled and scrolls past inside heavy fog, so a runtime
                // LODGroup would only ever pay for its own bookkeeping.
                foreach (LODGroup group in model.GetComponentsInChildren<LODGroup>(true))
                {
                    LOD[] lods = group.GetLODs();
                    if (lods.Length > 1)
                    {
                        int keepLevel = Mathf.Clamp(def.LodIndex, 0, lods.Length - 1);
                        var keep = new HashSet<Renderer>(lods[keepLevel].renderers.Where(r => r != null));
                        // A billboard-card LOD alone would face the wrong way once
                        // the arc bends the world - fall back to LOD0 if the
                        // requested level turns out to be empty.
                        if (keep.Count == 0 && keepLevel != 0)
                        {
                            keepLevel = 0;
                            keep = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
                        }
                        for (int i = 0; i < lods.Length; i++)
                        {
                            if (i == keepLevel)
                                continue;
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
                Log($"Scenery prefab: {outPath} (scale {scale:F3}, {def.Mode.ToString().ToLower()} {def.TargetSize}, " +
                    $"LOD{def.LodIndex}, {TriangleCount(root)} tris, {renderers.Length} renderer(s))");
                return prefab;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                else if (model != null) UnityEngine.Object.DestroyImmediate(model);
            }
        }

        /// <summary>Triangles a scenery prefab costs per spawned instance - the report's budget line.</summary>
        private static int TriangleCount(GameObject root)
        {
            int tris = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    tris += (int)(mesh.GetIndexCount(i) / 3);
            }
            return tris;
        }

        private static readonly Dictionary<string, Material> ArcMatCache = new Dictionary<string, Material>();

        /// <summary>Every atlas an arc material ended up sampling, for the texture-size cap.</summary>
        private static readonly HashSet<string> ArcTexturePaths = new HashSet<string>();

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

            // These packs ship materials pointing at texture guids that were
            // never in the package, so a null here is a silently flat-white prop
            // rather than an obvious failure. Say so in the report.
            if (tex == null && def.Tint == Color.white)
                Log($"  NOTE: {srcMat.name} resolved no albedo (dangling texture guid?) - " +
                    $"{def.Name} renders flat white. Give the def a Tint or pick another source.");

            // Foliage detection: leaf/flower/grass cards carry meaningful alpha
            // in their atlas (TEM vegetation). Those need cutout + double-sided;
            // solid-geometry atlases (TS/DS) have no alpha and stay opaque.
            bool texHasAlpha = false;
            if (tex != null)
            {
                string texPath = AssetDatabase.GetAssetPath(tex);
                ArcTexturePaths.Add(texPath);
                var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                texHasAlpha = importer != null && importer.DoesSourceTextureHaveAlpha();
            }
            bool foliage = def.TwoSided || texHasAlpha;

            // Packs disagree on where their foliage sits in the alpha ramp - the
            // Synty vegetation shader clips at 0.3, the toon packs at 0.5. Using
            // one number for both eats the thin edges off half the leaf cards.
            float cutoff = 0.5f;
            if (srcMat.HasProperty("_Cutoff"))
            {
                float srcCutoff = srcMat.GetFloat("_Cutoff");
                if (srcCutoff > 0f)
                    cutoff = srcCutoff;
            }

            Color tint = def.Tint;
            if (tint == Color.white && tex != null && tex.name.StartsWith("TEM_Atlas_Vegetation"))
                tint = new Color(0.7f, 0.78f, 0.64f);

            mat.shader = arcShader;
            // Assigned even when null: a source material whose albedo the arc
            // shader cannot reach (triplanar projection, for one) is meant to
            // come through as its flat tint, not as whatever texture an earlier
            // build left in the slot.
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", foliage ? 0f : 2f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", foliage ? cutoff : 0f);
            // clip() only compiles into cutout materials; opaque ones keep early-Z
            if (foliage) mat.EnableKeyword("_ALPHATEST_ON");
            else mat.DisableKeyword("_ALPHATEST_ON");
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
            if (assetPath.StartsWith("Assets/PolygonNatureBiomes")) return "PNB";
            return "Misc";
        }

        // ------------------------------------------------------------------
        // StartScene build
        // ------------------------------------------------------------------

        // Centre column uses the main floor, side columns the side floor - the
        // runtime spawner overlaps this pad from z=0 and coplanar tiles only
        // stay invisible when prefab AND material match per column.
        // Back row only: runtime floors cover z>=0 from the first frame (home
        // screen included), and a forward pad row sits coplanar with them with
        // offset UVs - that overlap shimmers now that the ground is textured.
        // Centre z=-6: a 12-unit tile then spans -12..0 and meets the first
        // runtime tile edge-to-edge (z=-7 left a 1-unit void strip at the start).
        private static readonly Vector3[] MainPadPositions =
        {
            new Vector3(0f, 0f, -6f),
        };

        private static readonly Vector3[] SidePadPositions =
        {
            new Vector3(12f, 0f, -6f), new Vector3(-12f, 0f, -6f),
        };

        private static GameObject BuildStartScene(string world, List<StartSceneItem> items, Dictionary<string, GameObject> prefabs)
        {
            string outPath = $"{StartSceneFolder}/StartScene_{world}.prefab";

            string floorName = world == "Seattle" ? "Seattle Floor"
                : world == "Arizona" ? "ArizonaFloor"
                : $"{world} Floor";
            string sideFloorName = world == "Seattle" ? "Seattle Floor Sides 1"
                : world == "Arizona" ? "Arizona Floor Sides"
                : $"{world} Floor Sides";
            GameObject floorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FloorPrefabFolder}/{floorName}.prefab");
            GameObject sideFloorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FloorPrefabFolder}/{sideFloorName}.prefab");
            if (floorPrefab == null)
                LogError($"StartScene {world}: floor prefab '{floorName}' not found");
            if (sideFloorPrefab == null)
                LogError($"StartScene {world}: side floor prefab '{sideFloorName}' not found");

            GameObject root = new GameObject($"StartScene_{world}");
            try
            {
                root.tag = "Floor";
                root.AddComponent<ScrollableObject>();

                foreach ((GameObject prefab, Vector3[] positions) in new[]
                         {
                             (floorPrefab, MainPadPositions),
                             (sideFloorPrefab, SidePadPositions),
                         })
                {
                    if (prefab == null)
                        continue;
                    foreach (Vector3 pos in positions)
                    {
                        GameObject tile = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                        tile.transform.localPosition = pos;
                        // Runtime right-side floors spawn rotated 180 on Y; match
                        // so the texture orientation is continuous at the z=0 seam
                        tile.transform.localRotation = pos.x > 0.1f
                            ? Quaternion.Euler(0f, 180f, 0f)
                            : Quaternion.identity;
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
                ["Seattle"] = (3, 6, 1.8f, 30),
                // France was (4,7, 1.3) - the game's densest spawn AND tightest
                // spacing, stacked on the game's heaviest tree (TEM_Tree_01A,
                // 6,678 tris) and six two-sided cutout patch types. Fine on
                // desktop; on the phone it was the one world that lagged
                // (2026-08-06). Overlapping alpha-tested quads are the killer on
                // Apple GPUs - the wider min-distance matters as much as the
                // count. Densest-in-game is now the verge grass, which Seattle
                // proves the phone can afford.
                ["France"] = (3, 5, 1.7f, 30),
                ["Arizona"] = (2, 5, 1.7f, 30),
                ["Oregon"] = (3, 6, 1.7f, 30),
                // Still spaced widest of the five - open sand is the point of a
                // beach - but 3-5 rather than 2-4 now that the shader grass is
                // gone from the dunes: props are the only thing left standing on
                // them, and at 2-4 the sides read as empty desert. The smaller
                // pool is per TYPE, and Hawaii has 18 of them against everyone
                // else's 9-10, so no type puts more than a handful on screen.
                ["Hawaii"] = (3, 5, 2.2f, 16),
            };

            // Per-location light mood: fog color/density + skybox. Seattle keeps
            // the scene's current values; the rest warm up or brighten. Arizona
            // and Oregon use flat gradient skies (Ringsport/Gradient Skybox) so
            // they read as clean as Seattle's; fog matches each horizon color.
            var atmosphere = new Dictionary<string, (Color fog, float density, string skybox)>
            {
                ["Seattle"] = (new Color(0.5396f, 0.7997f, 0.8113f), 0.04f, "Assets/Materials/Test Skybox.mat"),
                ["Arizona"] = (new Color(0.85f, 0.7f, 0.52f), 0.04f, "Assets/Materials/World/Sky_Arizona_Gradient.mat"),
                ["Oregon"] = (new Color(0.62f, 0.81f, 0.87f), 0.028f, "Assets/Materials/World/Sky_Oregon_Gradient.mat"),
                ["France"] = (new Color(0.72f, 0.8f, 0.7f), 0.035f, "Assets/Toon Enchanted Meadow/Skybox/TEM_Skybox_01A/TEM_Skybox_01A.mat"),
                // Thinnest fog in the game (0.024 against 0.028-0.04): a beach
                // wants a long, hazy sightline, and there is no dense canopy
                // here for the fog to hide pop-in behind.
                ["Hawaii"] = (new Color(0.74f, 0.88f, 0.9f), 0.024f, $"{WorldMatFolder}/Sky_Hawaii_Gradient.mat"),
            };

            foreach (string world in worldPrefabs.Keys)
            {
                string configPath = $"{LocationConfigFolder}/{world}.asset";
                var config = EnsureLocationConfig(world, configPath);
                if (config == null)
                    continue;

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

                // Ambience is filled only when the slot is empty, the same rule
                // JuicePolishSetup uses - swapping the stand-in in the Inspector
                // has to stick through the next build.
                SerializedProperty ambientProp = so.FindProperty("ambientSound");
                if (ambientProp.objectReferenceValue == null &&
                    LocationAmbience.TryGetValue(world, out string ambientPath))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ambientPath);
                    if (clip != null)
                    {
                        ambientProp.objectReferenceValue = clip;
                        Log($"Ambient sound seeded for {world}: {Path.GetFileName(ambientPath)}");
                    }
                }

                (Color fog, float density, string skyboxPath) = atmosphere[world];
                so.FindProperty("overrideAtmosphere").boolValue = true;
                so.FindProperty("fogColor").colorValue = fog;
                so.FindProperty("fogDensity").floatValue = density;
                var skybox = AssetDatabase.LoadAssetAtPath<Material>(skyboxPath);
                if (skybox == null)
                    LogError($"Skybox not found for {world}: {skyboxPath}");
                so.FindProperty("skyboxMaterial").objectReferenceValue = skybox;

                // Worlds whose floor prefabs this builder generates (Seattle and
                // Arizona keep their own hand-made ones, under other names)
                if (world == "France" || world == "Oregon" || world == "Hawaii")
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

        /// <summary>
        /// Loads a world's LocationConfig, creating it on first run for a world
        /// that has no asset yet. Only the fields the builder does not own get
        /// seeded here (ambient grading), so a later hand-tweak in the inspector
        /// survives the next build.
        /// </summary>
        private static LocationConfig EnsureLocationConfig(string world, string configPath)
        {
            var config = AssetDatabase.LoadAssetAtPath<LocationConfig>(configPath);
            if (config != null)
                return config;

            if (!Enum.TryParse(world, out Location location))
            {
                LogError($"No Location enum member named '{world}' - add it to Location.cs before building this world");
                return null;
            }

            EnsureFolder(LocationConfigFolder);
            config = ScriptableObject.CreateInstance<LocationConfig>();
            AssetDatabase.CreateAsset(config, configPath);

            var so = new SerializedObject(config);
            so.FindProperty("location").enumValueIndex = (int)location;
            // A saturated sky is the only fill light in the scene, so its raw
            // probe re-tints everything it touches - the dog included. Pulling
            // saturation back keeps a blue sky from turning the sand grey-blue.
            so.FindProperty("ambientSaturation").floatValue = 0.6f;
            so.FindProperty("ambientIntensity").floatValue = 0.95f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            Log($"LocationConfig created: {configPath}");
            return config;
        }

        // ------------------------------------------------------------------
        // Level remap / performance / scene tweaks
        // ------------------------------------------------------------------

        private static void RemapLevelLocation(string levelAssetPath, Location location, string locationConfigPath)
        {
            var level = AssetDatabase.LoadAssetAtPath<ScriptableObject>(levelAssetPath);
            var config = AssetDatabase.LoadAssetAtPath<LocationConfig>(locationConfigPath);
            if (level == null || config == null)
            {
                LogError($"Level remap failed: {levelAssetPath} -> {locationConfigPath}");
                return;
            }

            var so = new SerializedObject(level);
            so.FindProperty("location").enumValueIndex = (int)location;
            so.FindProperty("locationConfig").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
            Log($"Level remapped: {Path.GetFileNameWithoutExtension(levelAssetPath)} -> {location}");
        }

        private static void ApplyPerformanceSettings()
        {
            // GPU instancing on every arc material: repeated scenery (pines,
            // cacti, grass) batches on WebGL2 where the SRP batcher is
            // unavailable; the SRP batcher covers WebGPU/Metal.
            int instanced = 0;
            foreach (string folder in new[] { WorldMatFolder, FloorMatFolder })
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (mat == null || mat.enableInstancing)
                        continue;
                    // GrassBlades deliberately compiles no instancing variant -
                    // one shared patch mesh across every live tile is already a
                    // single SRP-batched draw, and flagging it here would only
                    // claim a path the shader does not have.
                    if (mat.shader != null && mat.shader.name == GrassShaderName)
                        continue;
                    mat.enableInstancing = true;
                    EditorUtility.SetDirty(mat);
                    instanced++;
                }
            }
            Log($"GPU instancing enabled on {instanced} material(s)");

            // Pack atlases are flat-colour art - 1024 is indistinguishable in
            // game and much cheaper for mobile WebGL memory + download. Every
            // texture an arc material actually sampled this run is covered, so a
            // new pack (the tropical set ships 2048 leaf sheets) is capped the
            // moment it is used rather than needing a hand-written path here.
            var atlasPaths = new HashSet<string>(ArcTexturePaths)
            {
                "Assets/Toon Enchanted Meadow/Textures/TEM_Atlas_Vegetation_1A.tga",
                "Assets/Toon Enchanted Meadow/Textures/TEM_Atlas_Vegetation_1B.tga",
                "Assets/Toon Enchanted Meadow/Textures/TEM_Atlas_Vegetation_1A.png",
                "Assets/Toon Enchanted Meadow/Textures/TEM_Atlas_Vegetation_1B.png",
                "Assets/Toon Enchanted Meadow/Textures/TEM_Atlas_1A.png",
                "Assets/Toon Desert/Textures/Atlas_1A_D.png",
                "Assets/Toon Series/Toon Nature Assets/Textures/Atlas_1A_D.png",
            };
            // Ground textures are sampled at tiling 3-5 across a 12-unit tile,
            // so they need the cap as much as the prop atlases do.
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { FloorMatFolder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                Texture groundTex = mat != null && mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                if (groundTex != null)
                    atlasPaths.Add(AssetDatabase.GetAssetPath(groundTex));
            }

            int capped = 0;
            foreach (string path in atlasPaths.OrderBy(p => p))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                int cap = SmallVegetationCaps.TryGetValue(Path.GetFileName(path), out int small) ? small : 1024;
                if (importer.maxTextureSize <= cap)
                    continue;
                importer.maxTextureSize = cap;
                importer.SaveAndReimport();
                capped++;
                Log($"Texture capped to {cap}: {Path.GetFileName(path)}");
            }
            Log($"Texture size cap: {capped} of {atlasPaths.Count} texture(s) reduced");
        }

        private static void ApplyArcControllerValues(float strength, float distance)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log("Arc controller update skipped (play mode) - rerun the builder from the Tools menu");
                return;
            }

            var controller = UnityEngine.Object.FindFirstObjectByType<ArcEffectController>();
            if (controller == null)
            {
                Log("Arc controller not found in the open scene - open the game scene and rerun Tools/RingSport/Build World Scenery");
                return;
            }

            var so = new SerializedObject(controller);
            SerializedProperty strengthProp = so.FindProperty("arcStrength");
            SerializedProperty distanceProp = so.FindProperty("arcDistance");
            if (Mathf.Approximately(strengthProp.floatValue, strength) &&
                Mathf.Approximately(distanceProp.floatValue, distance))
                return;

            strengthProp.floatValue = strength;
            distanceProp.floatValue = distance;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(controller.gameObject.scene);
            Log($"ArcEffectController set to strength {strength}, distance {distance} (scene saved)");
        }

        private static void ApplyDespawnDistance(float distance)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Log("Despawn distance update skipped (play mode) - rerun the builder from the Tools menu");
                return;
            }

            var generator = UnityEngine.Object.FindFirstObjectByType<LevelGenerator>();
            if (generator == null)
            {
                Log("LevelGenerator not found in the open scene - open the game scene and rerun Tools/RingSport/Build World Scenery");
                return;
            }

            var so = new SerializedObject(generator);
            SerializedProperty prop = so.FindProperty("despawnDistance");
            if (prop == null || Mathf.Approximately(prop.floatValue, distance))
                return;

            prop.floatValue = distance;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(generator);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(generator.gameObject.scene);
            Log($"LevelGenerator despawnDistance set to {distance} (scene saved)");
        }

        /// <summary>
        /// A hard-to-miss radial beacon behind the love note: spark rays that
        /// shoot outward from the centre plus a soft pulsing glow. Uses the
        /// Generic_ParticlesUnlit_Arc graph so the effect curves with the world
        /// and stays glued to the note at spawn distance.
        /// </summary>
        private static void BuildLoveNoteBeacon()
        {
            const string prefabPath = "Assets/Prefabs/Collectibles/LoveNote.prefab";
            var particleShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/ShaderGraphs/Generic_ParticlesUnlit_Arc.shadergraph");
            var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/VFX/SparkStar.png");
            var glowTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/VFX/SoftGlow.png");
            if (particleShader == null || sparkTex == null || glowTex == null)
            {
                LogError("Love note beacon: missing particle shader graph or VFX textures");
                return;
            }

            Material sparkMat = CreateParticleMat("Arc_VFX_NoteBurst", particleShader, sparkTex, Color.white);
            Material glowMat = CreateParticleMat("Arc_VFX_NoteGlow", particleShader, glowTex, new Color(1f, 1f, 1f, 0.6f));

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                // Bigger, calmer, slightly lifted note (+25% scale, +0.25 up so
                // it clears coin height and reads from distance; hover kept, no
                // spin). Applied before the beacon is centred on it. The pickup
                // capsule stays put - the lift is visual only.
                Transform visual = contents.transform.Find("Visual");
                if (visual != null)
                {
                    visual.localScale = Vector3.one * 0.625f;
                    visual.localPosition = new Vector3(0f, 0.25f, 0f);
                }

                foreach (CapsuleCollider capsule in contents.GetComponentsInChildren<CapsuleCollider>(true))
                {
                    capsule.radius = 0.625f;
                    capsule.height = 2.5f;
                }

                var animation = contents.GetComponentInChildren<CollectibleAnimation>(true);
                if (animation != null)
                {
                    var animSo = new SerializedObject(animation);
                    SerializedProperty rotProp = animSo.FindProperty("rotationSpeed");
                    if (rotProp != null)
                    {
                        rotProp.floatValue = 0f;
                        animSo.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                // Centre the beacon on the note visual
                Bounds bounds = new Bounds(contents.transform.position, Vector3.zero);
                Renderer[] renderers = contents.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    foreach (Renderer r in renderers.Skip(1))
                        bounds.Encapsulate(r.bounds);
                }
                Vector3 centreLocal = contents.transform.InverseTransformPoint(bounds.center);

                Transform old = contents.transform.Find("AttentionBeacon");
                if (old != null)
                    UnityEngine.Object.DestroyImmediate(old.gameObject);

                var beacon = new GameObject("AttentionBeacon");
                beacon.transform.SetParent(contents.transform, false);
                // +Z is away from the camera - the note occludes the burst centre
                beacon.transform.localPosition = centreLocal + new Vector3(0f, 0f, 0.2f);

                ConfigureBurst(CreateChildPs(beacon, "SparkBurst"), sparkMat);
                ConfigurePulse(CreateChildPs(beacon, "GlowPulse"), glowMat);

                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                Log("Love note beacon added (white burst, 0.625 scale, +0.25 lift, spin off)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Material CreateParticleMat(string name, Shader shader, Texture2D tex, Color tint)
        {
            string path = $"{WorldMatFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            if (mat.HasProperty("_Albedo_Map")) mat.SetTexture("_Albedo_Map", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Alpha_Clip_Threshold")) mat.SetFloat("_Alpha_Clip_Threshold", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static ParticleSystem CreateChildPs(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go.AddComponent<ParticleSystem>();
        }

        private static void ConfigureBurst(ParticleSystem ps, Material mat)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.65f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.4f, 4.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 48;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 18f;

            // Circle facing the camera: particles fly radially outward from the centre
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.03f;
            shape.radiusThickness = 0f;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void ConfigurePulse(ParticleSystem ps, Material mat)
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.75f;
            main.startSpeed = 0f;
            main.startSize = 1.1f;
            main.startColor = new Color(1f, 1f, 1f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 6;
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 2.5f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            // Rings that swell outward and fade - reads as a beacon from far away
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 3.0f));

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void DisableCollectibleShadows()
        {
            int changedPrefabs = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Collectibles" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (Renderer r in contents.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                        {
                            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        changedPrefabs++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            Log($"Collectible shadows disabled on {changedPrefabs} prefab(s)");
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
