using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using RingSport.Level;
using RingSport.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// One-shot setup for the human decoy used by the flee attack (and later
    /// the face attack / decoy battle): builds a decoy-specific
    /// AnimatorController from the Malbers human clips (jog/sprint locomotion,
    /// strafe-lean, fall-forward), assembles Assets/Prefabs/Decoy.prefab
    /// hosting the placeholder Steve model + DecoyController, and wires the
    /// prefab into the scene's MiniLevelFleeAttack.
    ///
    /// The fall-forward clip is chosen by SAMPLING the package death clips and
    /// measuring which way the body lands (the pack has no clip literally named
    /// "fall forward"). Drop a better clip (e.g. Mixamo "Falling Forward
    /// Death", imported as Humanoid) anywhere under
    /// Assets/Animations/Decoy/Overrides and re-run to use it instead.
    ///
    /// Runs automatically after script compilation when missing/stale; can also
    /// be run from the menu. Idempotent.
    /// </summary>
    public static class DecoySetup
    {
        // Bump to make the auto-run rebuild after changing this script
        private const int SetupVersion = 10;
        private const string VersionPrefKey = "RingSport.DecoySetup.Version";

        private const string ControllerPath = "Assets/Animations/Decoy/DecoyHuman.controller";
        private const string PrefabPath = "Assets/Prefabs/Decoy.prefab";
        private const string OverrideFolder = "Assets/Animations/Decoy/Overrides";
        private const string FallbackMaterialPath = "Assets/Materials/DecoyHuman.mat";
        private const string ModelName = "Human Model";
        // Uniform scale on the model (user-tuned). Everything downstream reads
        // the live transform scale instead of this constant: DecoyController
        // slows the locomotion cycle and scales the ragdoll to match.
        private const float ModelScale = 1.5f;

        // Malbers Animal Controller / Human asset GUIDs
        private const string ModelGuid = "d9a8fb1864c033e4b990da223ac23ead";        // Steve_v2.fbx
        private const string RagdollPrefabGuid = "820e370bc2f37df44adfbdd3b1536ed1"; // Steve Ragdoll.prefab
        private const string IdleFbxGuid = "adba5936348c1e1459e4a28103f7b697";      // S_Idle.fbx
        private const string JogFbxGuid = "c15724f994cb8bf459b9488ae4b263a1";       // S_Jog_F.fbx
        private const string SprintFbxGuid = "ab163dec8ad003e4d805b833ca7c204e";    // S_Sprint.fbx
        private const string RunLeftSharpGuid = "c42ec8841f1c5724e86174423eb8db6c"; // RunLeftSharp.anim
        private const string RunRightSharpGuid = "68efa89381c47054fb3e1bca123d1ebe"; // RunRightSharp.anim
        private const string Death1FbxGuid = "999623ad073529442bb47e8a89aa6144";    // H_Death1.fbx
        private const string Death2AnimGuid = "a832e322c5cac594cb7c1db28b77e43c";   // H_Death2.anim
        private const string Death3FbxGuid = "29208826ca7505c468328e97add03b59";    // H_Death3.fbx
        private const string BarlowBoldFontGuid = "099dce98fb9fd47cb8ff1abc60bfba4c"; // Barlow-Bold SDF.asset

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

            bool prefabMissing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null;

            var controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var versionParam = controllerAsset == null
                ? null
                : controllerAsset.parameters.FirstOrDefault(p => p.name == "SetupVersion");
            bool controllerStale =
                controllerAsset == null ||
                EditorPrefs.GetInt(VersionPrefKey, 0) < SetupVersion ||
                versionParam == null ||
                versionParam.defaultInt < SetupVersion;

            // Also re-run when the open scene's flee attack lost a wire
            var fleeAttack = Object.FindAnyObjectByType<MiniLevelFleeAttack>(FindObjectsInactive.Include);
            bool sceneUnwired = false;
            if (fleeAttack != null)
            {
                var fleeSO = new SerializedObject(fleeAttack);
                sceneUnwired = fleeSO.FindProperty("decoyPrefab")?.objectReferenceValue == null ||
                               fleeSO.FindProperty("bannerFont")?.objectReferenceValue == null;
            }

            if (!prefabMissing && !controllerStale && !sceneUnwired)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[DecoySetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Decoy")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DecoySetup] Cannot run during play mode - exit play mode first (the auto-run will then apply it).");
                return;
            }

            var controller = BuildAnimatorController();
            if (controller == null)
                return;

            var prefab = BuildDecoyPrefab(controller);
            if (prefab == null)
                return;

            AssetDatabase.SaveAssets();
            WireFleeAttack(prefab);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[DecoySetup] Done (v{SetupVersion}). Controller at {ControllerPath}, prefab at {PrefabPath}.");
        }

        // ------------------------------------------------------------------
        // Animator controller
        // ------------------------------------------------------------------

        private static AnimatorController BuildAnimatorController()
        {
            var clips = new Dictionary<string, AnimationClip>
            {
                ["idle"] = LoadClip(IdleFbxGuid, "S_Idle"),
                ["jog"] = LoadClip(JogFbxGuid, "S_Jog_F"),
                ["sprint"] = LoadClip(SprintFbxGuid, "S_Sprint"),
                ["runLeft"] = LoadClip(RunLeftSharpGuid),
                ["runRight"] = LoadClip(RunRightSharpGuid),
            };

            var missing = clips.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError($"[DecoySetup] Missing Malbers human animation clips: {string.Join(", ", missing)}. Is the Malbers Animations package intact?");
                return null;
            }

            var fallClip = PickFallForwardClip();
            if (fallClip == null)
            {
                Debug.LogError("[DecoySetup] No usable fall/death clip found at all - cannot build the decoy controller.");
                return null;
            }

            EnsureFolder("Assets/Animations");
            EnsureFolder("Assets/Animations/Decoy");

            // Rebuild in place (never delete the asset) so the controller keeps
            // its GUID and the Decoy prefab's Animator reference stays valid.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            foreach (var parameter in controller.parameters.ToArray())
                controller.RemoveParameter(parameter);

            foreach (var subAsset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            {
                if (subAsset != null && subAsset != controller)
                    AssetDatabase.RemoveObjectFromAsset(subAsset);
            }

            var baseStateMachine = new AnimatorStateMachine
            {
                name = "Base Layer",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(baseStateMachine, controller);

            var layers = controller.layers;
            if (layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                layers = controller.layers;
            }
            layers[0].stateMachine = baseStateMachine;
            controller.layers = layers;

            controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Strafe", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter("Fall", AnimatorControllerParameterType.Trigger);
            // Version stamp read by TryAutoRun's staleness check; not used by gameplay
            controller.AddParameter(new AnimatorControllerParameter { name = "SetupVersion", type = AnimatorControllerParameterType.Int, defaultInt = SetupVersion });

            var sm = baseStateMachine;

            // --- Locomotion: 2D blend of strafe lean (x) vs idle->jog->sprint (y),
            // mirroring the dog's parameter conventions ---
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "Strafe",
                blendParameterY = "MoveSpeed",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            tree.AddChild(clips["idle"], new Vector2(0f, 0f));
            tree.AddChild(clips["jog"], new Vector2(0f, 1f));
            tree.AddChild(clips["runLeft"], new Vector2(-1f, 1f));
            tree.AddChild(clips["runRight"], new Vector2(1f, 1f));
            tree.AddChild(clips["sprint"], new Vector2(0f, 2f));
            tree.AddChild(clips["runLeft"], new Vector2(-1f, 2f));
            tree.AddChild(clips["runRight"], new Vector2(1f, 2f));

            // The lean clips are run-speed; play them a touch faster at the
            // sprint tier so the feet keep up
            var children = tree.children;
            children[5].timeScale = 1.15f;
            children[6].timeScale = 1.15f;
            tree.children = children;

            var locomotion = sm.AddState("Locomotion", new Vector3(280f, 120f));
            locomotion.motion = tree;
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = "AnimSpeed";
            sm.defaultState = locomotion;

            // --- Fall forward: entered from anywhere when the dog pounces; no
            // way out (the ragdoll swap or the chase cleanup ends it) ---
            var fall = sm.AddState("Fall Forward", new Vector3(560f, 120f));
            fall.motion = fallClip;
            // Snappier topple: the pounce-to-catch window is short, so the fall
            // plays faster than authored to get visibly underway before the
            // ragdoll takes over
            fall.speed = 1.5f;

            var anyToFall = sm.AddAnyStateTransition(fall);
            anyToFall.hasExitTime = false;
            anyToFall.hasFixedDuration = true;
            anyToFall.duration = 0.12f;
            anyToFall.canTransitionToSelf = false;
            anyToFall.AddCondition(AnimatorConditionMode.If, 0f, "Fall");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------
        // Fall-forward clip selection
        // ------------------------------------------------------------------

        private class FallCandidate
        {
            public AnimationClip clip;
            public float endHipsHeight;
            public float headAheadOfHips; // along the character's starting facing; + = forward
            public bool Fell => endHipsHeight < 0.6f;
            public bool FallsForward => Fell && headAheadOfHips > 0.25f;
        }

        private static AnimationClip PickFallForwardClip()
        {
            // A user-supplied clip (e.g. Mixamo) always wins
            if (AssetDatabase.IsValidFolder(OverrideFolder))
            {
                var overrideClip = AssetDatabase.FindAssets("t:AnimationClip", new[] { OverrideFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (overrideClip != null)
                {
                    Debug.Log($"[DecoySetup] Fall forward: using override clip '{overrideClip.name}' from {OverrideFolder}.");
                    return overrideClip;
                }
            }

            var candidates = new[]
            {
                LoadClip(Death1FbxGuid, "H_Death1"),
                LoadClip(Death2AnimGuid),
                LoadClip(Death3FbxGuid, "H_Death3"),
            }.Where(c => c != null).Select(MeasureFall).Where(m => m != null).ToList();

            foreach (var candidate in candidates)
            {
                string verdict = !candidate.Fell
                    ? "does not end on the ground"
                    : candidate.FallsForward
                        ? "falls FORWARD"
                        : candidate.headAheadOfHips < -0.25f ? "falls backward" : "falls sideways/on the spot";
                Debug.Log($"[DecoySetup] Measured '{candidate.clip.name}': end hips height {candidate.endHipsHeight:F2}m, " +
                          $"head {candidate.headAheadOfHips:+0.00;-0.00}m ahead of hips -> {verdict}.");
            }

            // Prefer the SHORTEST forward fall - only ~0.45s plays between the
            // pounce and the ragdoll handover, so a long slow collapse barely
            // gets started while a short one reads as a real topple
            var forward = candidates.Where(c => c.FallsForward)
                .OrderBy(c => c.clip.length)
                .ThenByDescending(c => c.headAheadOfHips)
                .FirstOrDefault();
            if (forward != null)
            {
                Debug.Log($"[DecoySetup] Fall forward: using package clip '{forward.clip.name}' ({forward.clip.length:F1}s).");
                return forward.clip;
            }

            // No forward fall in the package - use the least-bad death as a
            // stand-in and tell the user how to supply a real one
            var fallback = candidates.Where(c => c.Fell)
                .OrderByDescending(c => c.headAheadOfHips)
                .FirstOrDefault() ?? candidates.FirstOrDefault();
            if (fallback != null)
            {
                Debug.LogWarning("[DecoySetup] NO FORWARD-FALL CLIP IN THE PACKAGE. Using " +
                    $"'{fallback.clip.name}' as a stand-in. To fix: grab a Mixamo clip (e.g. 'Falling Forward Death'), " +
                    $"import the FBX as Humanoid, drop it under {OverrideFolder}, and run Tools > RingSport > Setup Decoy.");
                return fallback.clip;
            }
            return null;
        }

        /// <summary>
        /// Plays the humanoid clip through on a temp Steve instance (via a
        /// stepped PlayableGraph - humanoid muscle clips can't use
        /// SampleAnimation, and these clips carry their vertical drop in ROOT
        /// MOTION, so a static one-shot evaluate never leaves standing height)
        /// and measures where the body ends up relative to its starting facing.
        /// </summary>
        private static FallCandidate MeasureFall(AnimationClip clip)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null)
                return null;

            var temp = Object.Instantiate(modelPrefab, Vector3.zero, Quaternion.identity);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = temp.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning($"[DecoySetup] Steve model has no Animator - cannot measure '{clip.name}'.");
                    return null;
                }

                Transform hips = FindBone(temp.transform, "Pelvis") ?? FindBone(temp.transform, "Hips");
                Transform head = FindBone(temp.transform, "Head");
                if (hips == null || head == null)
                {
                    Debug.LogWarning($"[DecoySetup] Could not find Pelvis/Head bones on the Steve model - cannot measure '{clip.name}'.");
                    return null;
                }

                Vector3 forward = temp.transform.forward;

                // The pose must be read while the sampling controller is still
                // assigned - reassigning runtimeAnimatorController rebinds the
                // Animator and resets every bone to the default pose
                var samplerController = PlayClipThrough(animator, clip);
                Vector3 hipsEnd = hips.position;
                Vector3 headEnd = head.position;
                Object.DestroyImmediate(samplerController);

                return new FallCandidate
                {
                    clip = clip,
                    endHipsHeight = hipsEnd.y,
                    headAheadOfHips = Vector3.Dot(headEnd - hipsEnd, forward),
                };
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static AnimatorController PlayClipThrough(Animator animator, AnimationClip clip)
        {
            // A raw PlayableGraph output in edit mode applies root motion but
            // never writes the humanoid muscle pose to the bones; manually
            // stepping the Animator with a throwaway controller applies both.
            var tempController = new AnimatorController { name = "DecoySetupSampler" };
            tempController.AddLayer("Base");
            var state = tempController.layers[0].stateMachine.AddState("Clip");
            state.motion = clip;

            animator.runtimeAnimatorController = tempController;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = true;

            animator.Play("Clip", 0, 0f);
            animator.Update(0f);

            const int steps = 90;
            float dt = clip.length / steps;
            for (int i = 0; i < steps; i++)
                animator.Update(dt);

            return tempController;
        }

        private static Transform FindBone(Transform root, string normalizedName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name.StartsWith("R_") ? t.name.Substring(2) : t.name;
                if (name == normalizedName)
                    return t;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Prefab
        // ------------------------------------------------------------------

        private static GameObject BuildDecoyPrefab(AnimatorController controller)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null)
            {
                Debug.LogError("[DecoySetup] Steve_v2.fbx not found. Is the Malbers Animations package intact?");
                return null;
            }

            var ragdollPath = AssetDatabase.GUIDToAssetPath(RagdollPrefabGuid);
            var ragdollPrefab = string.IsNullOrEmpty(ragdollPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(ragdollPath);
            if (ragdollPrefab == null)
                Debug.LogWarning("[DecoySetup] Steve Ragdoll.prefab not found - the catch will carry the animated model instead of ragdolling.");
            else
                ValidateRagdollLimbs(ragdollPrefab);

            // Assemble fresh each run; SaveAsPrefabAsset over the same path keeps
            // the prefab GUID so scene references survive rebuilds.
            var root = new GameObject("Decoy");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                model.name = ModelName;
                model.transform.SetParent(root.transform, false);
                // Model origin is at its feet, facing +Z - matches the decoy
                // root convention (ground level, fleeing along +Z)
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * ModelScale;

                var animator = model.GetComponent<Animator>();
                if (animator == null)
                    animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                FixMaterialsIfBroken(model);

                var decoy = root.AddComponent<DecoyController>();
                var so = new SerializedObject(decoy);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("ragdollPrefab").objectReferenceValue = ragdollPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();

                EnsureFolder("Assets/Prefabs");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError("[DecoySetup] Could not save the Decoy prefab.");
                    return null;
                }
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Ensures every DecoyLimb the pounce can target resolves to a
        /// rigidbody on the actual ragdoll prefab (the face attack, decoy
        /// battle etc. will pass limbs other than the flee attack's right
        /// forearm). Logs the full resolution table; warns if a limb only
        /// resolves through its fallback chain.
        /// </summary>
        private static void ValidateRagdollLimbs(GameObject ragdollPrefab)
        {
            var temp = Object.Instantiate(ragdollPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var lines = new List<string>();
                var fallbacks = new List<string>();
                var missing = new List<string>();

                foreach (DecoyLimb limb in System.Enum.GetValues(typeof(DecoyLimb)))
                {
                    var body = DecoyController.ResolveLimbBody(temp.transform, limb);
                    if (body == null)
                    {
                        missing.Add(limb.ToString());
                        lines.Add($"{limb} -> MISSING");
                        continue;
                    }

                    string resolved = body.name.StartsWith("R_") ? body.name.Substring(2) : body.name;
                    bool primary = resolved == DecoyController.PrimaryBoneName(limb);
                    if (!primary)
                        fallbacks.Add($"{limb} -> {body.name}");
                    lines.Add($"{limb} -> {body.name}{(primary ? "" : " (fallback)")}");
                }

                if (missing.Count > 0)
                    Debug.LogError($"[DecoySetup] Grab limbs with NO rigidbody on the ragdoll: {string.Join(", ", missing)}. Those pounce targets would fall back to a whole-model carry.");
                else if (fallbacks.Count > 0)
                    Debug.LogWarning($"[DecoySetup] Grab limbs resolving through fallbacks (no dedicated rigidbody): {string.Join(", ", fallbacks)}.");
                else
                    Debug.Log($"[DecoySetup] All {lines.Count} grab limbs resolve to dedicated ragdoll rigidbodies:\n{string.Join("\n", lines)}");
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static void FixMaterialsIfBroken(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool broken = materials.Any(m =>
                    m == null ||
                    m.shader == null ||
                    m.shader.name == "Hidden/InternalErrorShader" ||
                    !m.shader.isSupported);

                if (!broken)
                    continue;

                var fallback = GetOrCreateFallbackMaterial();
                if (fallback == null)
                    return;

                renderer.sharedMaterials = materials.Select(_ => fallback).ToArray();
                Debug.Log($"[DecoySetup] Replaced unsupported material(s) on '{renderer.name}' with {FallbackMaterialPath}.");
            }
        }

        private static Material GetOrCreateFallbackMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(FallbackMaterialPath);
            if (existing != null)
                return existing;

            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[DecoySetup] No URP shader found for the fallback decoy material.");
                return null;
            }

            var material = new Material(shader) { color = new Color(0.85f, 0.62f, 0.48f) };
            AssetDatabase.CreateAsset(material, FallbackMaterialPath);
            return material;
        }

        // ------------------------------------------------------------------
        // Scene wiring
        // ------------------------------------------------------------------

        private static void WireFleeAttack(GameObject prefab)
        {
            var fleeAttack = Object.FindAnyObjectByType<MiniLevelFleeAttack>(FindObjectsInactive.Include);
            if (fleeAttack == null)
            {
                Debug.Log("[DecoySetup] No MiniLevelFleeAttack in the open scene - prefab built but not wired.");
                return;
            }

            var fontPath = AssetDatabase.GUIDToAssetPath(BarlowBoldFontGuid);
            var bannerFont = string.IsNullOrEmpty(fontPath) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (bannerFont == null)
                Debug.LogWarning("[DecoySetup] Barlow-Bold SDF font asset not found - the chase banners will use the TMP default font.");

            var so = new SerializedObject(fleeAttack);
            var prefabProp = so.FindProperty("decoyPrefab");
            var fontProp = so.FindProperty("bannerFont");
            if (prefabProp == null)
            {
                Debug.LogWarning("[DecoySetup] MiniLevelFleeAttack has no decoyPrefab field - is the script up to date?");
                return;
            }

            bool prefabChanged = prefabProp.objectReferenceValue != prefab;
            bool fontChanged = fontProp != null && bannerFont != null && fontProp.objectReferenceValue != bannerFont;
            if (!prefabChanged && !fontChanged)
                return;

            bool sceneWasDirty = fleeAttack.gameObject.scene.isDirty;
            prefabProp.objectReferenceValue = prefab;
            if (fontProp != null && bannerFont != null)
                fontProp.objectReferenceValue = bannerFont;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorSceneManager.MarkSceneDirty(fleeAttack.gameObject.scene);
                if (!sceneWasDirty)
                    EditorSceneManager.SaveScene(fleeAttack.gameObject.scene);
                else
                    Debug.Log("[DecoySetup] Scene had unsaved changes - decoy prefab wired but scene NOT auto-saved. Save it when ready.");
            }
            Debug.Log("[DecoySetup] Wired Decoy prefab into MiniLevelFleeAttack.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static AnimationClip LoadClip(string guid, string clipName = null)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;

            if (clipName == null)
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == clipName);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = System.IO.Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
