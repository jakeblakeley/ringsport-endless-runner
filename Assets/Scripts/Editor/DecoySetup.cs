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
    /// hosting the decoy model + DecoyController, and wires the prefab into the
    /// scene's MiniLevelFleeAttack.
    ///
    /// The model is Assets/Models/decoy.glb, rigged on the Malbers human
    /// skeleton, so it keeps every one of those clips: DecoyAvatarSetup gives
    /// it a Humanoid avatar and Mecanim retargets them onto it. Its catch
    /// ragdoll comes from DecoyRagdollSetup rather than the Malbers Steve
    /// ragdoll, so the body the dog drags around is the decoy's own.
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
        private const int SetupVersion = 16;
        private const string VersionPrefKey = "RingSport.DecoySetup.Version";

        private const string ControllerPath = "Assets/Animations/Decoy/DecoyHuman.controller";
        private const string PrefabPath = "Assets/Prefabs/Decoy.prefab";
        private const string OverrideFolder = "Assets/Animations/Decoy/Overrides";
        private const string FallbackMaterialPath = "Assets/Materials/DecoyHuman.mat";
        private const string ModelName = "Human Model";
        // Uniform scale on the model (user-tuned), against the size the
        // placeholder human animated at - see modelScaleCompensation, which
        // holds the decoy to that same height. Everything downstream reads the
        // live transform scale rather than either constant: DecoyController
        // slows the locomotion cycle and scales the ragdoll to match.
        private const float ModelScale = 1.5f;

        // The decoy model itself
        private const string ModelGuid = DecoyAvatarSetup.DecoyModelGuid;          // Assets/Models/decoy.glb

        // Malbers Animal Controller / Human asset GUIDs
        private const string IdleFbxGuid = "adba5936348c1e1459e4a28103f7b697";      // S_Idle.fbx
        private const string JogFbxGuid = "c15724f994cb8bf459b9488ae4b263a1";       // S_Jog_F.fbx
        private const string SprintFbxGuid = "ab163dec8ad003e4d805b833ca7c204e";    // S_Sprint.fbx
        private const string RunLeftSharpGuid = "c42ec8841f1c5724e86174423eb8db6c"; // RunLeftSharp.anim
        private const string RunRightSharpGuid = "68efa89381c47054fb3e1bca123d1ebe"; // RunRightSharp.anim
        private const string Death1FbxGuid = "999623ad073529442bb47e8a89aa6144";    // H_Death1.fbx
        private const string Death2AnimGuid = "a832e322c5cac594cb7c1db28b77e43c";   // H_Death2.anim
        private const string Death3FbxGuid = "29208826ca7505c468328e97add03b59";    // H_Death3.fbx
        private const string PowerUpFbxGuid = "f21efd2b60c470a40bc375ba97743ea8";   // S_PowerUp.fbx
        private const string BarlowBoldFontGuid = "099dce98fb9fd47cb8ff1abc60bfba4c"; // Barlow-Bold SDF.asset

        // Real seconds between the face attack firing the PowerUp trigger (at
        // charge start) and the mid-pounce freeze: ChargeSeconds 0.6 +
        // PounceFreezeDelaySeconds 0.22 (see MiniLevelFaceAttack). The Power Up
        // state speed is solved so the clip's measured arms-up peak lands
        // right at the freeze.
        private const float PowerUpLeadRealSeconds = 0.82f;

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

            bool prefabMissing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null ||
                                 AssetDatabase.LoadAssetAtPath<GameObject>(DecoyRagdollSetup.RagdollPrefabPath) == null ||
                                 AssetDatabase.LoadAssetAtPath<Avatar>(DecoyAvatarSetup.AvatarPath) == null;

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

            var modelPrefab = LoadModelPrefab();
            if (modelPrefab == null)
                return;

            EnsureFolder("Assets/Animations");
            EnsureFolder("Assets/Animations/Decoy");

            // The Malbers human clips are HUMANOID clips, so nothing can be
            // sampled or played on the decoy until it has an avatar
            var avatar = BuildAvatar(modelPrefab);
            if (avatar == null)
                return;

            modelScaleCompensation = MeasureScaleCompensation(modelPrefab, avatar);

            var controller = BuildAnimatorController(modelPrefab, avatar);
            if (controller == null)
                return;

            var prefab = BuildDecoyPrefab(modelPrefab, avatar, controller);
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

        private static AnimatorController BuildAnimatorController(GameObject modelPrefab, Avatar avatar)
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

            var fallClip = PickFallForwardClip(modelPrefab, avatar);
            if (fallClip == null)
            {
                Debug.LogError("[DecoySetup] No usable fall/death clip found at all - cannot build the decoy controller.");
                return null;
            }

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

            // --- Power up: arms-up taunt for the face attack. Fired as the
            // decoy squares up (charge start); the frozen QTE then holds it
            // via the AnimSpeed crawl, with the raised arms spreading the limb
            // tap targets apart. The state speed is solved so the clip's
            // MEASURED arms-up peak lands right at the freeze (the trigger
            // fires PowerUpLeadRealSeconds earlier, playing at
            // AnimSpeed = 1/ModelScale). Falls out to Locomotion at clip end
            // (a crawling clip never gets there mid-QTE); the dodge escape
            // exits early via DecoyController.ResumeLocomotion.
            var powerUpClip = LoadClip(PowerUpFbxGuid, "S_PowerUp");
            if (powerUpClip != null)
            {
                var powerUp = sm.AddState("Power Up", new Vector3(280f, 280f));
                powerUp.motion = powerUpClip;
                powerUp.speedParameterActive = true;
                powerUp.speedParameter = "AnimSpeed";

                float peakTime = MeasureHandsUpPeakTime(powerUpClip, modelPrefab, avatar);
                float clipSecondsByFreeze = PowerUpLeadRealSeconds / EffectiveModelScale; // seconds of clip elapsed at state speed 1
                powerUp.speed = peakTime > 0.05f
                    ? Mathf.Clamp(peakTime / Mathf.Max(clipSecondsByFreeze, 0.01f), 0.5f, 3f)
                    : 1.2f;
                Debug.Log($"[DecoySetup] Power up: arms-up peak at {peakTime:F2}s of {powerUpClip.length:F2}s -> state speed {powerUp.speed:F2} (peak lands at the QTE freeze).");

                controller.AddParameter("PowerUp", AnimatorControllerParameterType.Trigger);
                var anyToPowerUp = sm.AddAnyStateTransition(powerUp);
                anyToPowerUp.hasExitTime = false;
                anyToPowerUp.hasFixedDuration = true;
                anyToPowerUp.duration = 0.15f;
                anyToPowerUp.canTransitionToSelf = false;
                anyToPowerUp.AddCondition(AnimatorConditionMode.If, 0f, "PowerUp");

                var powerUpToLocomotion = powerUp.AddTransition(locomotion);
                powerUpToLocomotion.hasExitTime = true;
                powerUpToLocomotion.exitTime = 0.95f;
                powerUpToLocomotion.hasFixedDuration = true;
                powerUpToLocomotion.duration = 0.3f;
            }
            else
            {
                Debug.LogWarning("[DecoySetup] S_PowerUp clip not found - the face attack QTE keeps the plain standing pose.");
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// Steps the clip through on a temp decoy (same stepped-Animator
        /// sampling as the fall measurement - humanoid muscle poses need it)
        /// and returns the clip time where the hands reach their highest
        /// combined point: the arms-up peak of the power-up taunt.
        /// </summary>
        private static float MeasureHandsUpPeakTime(AnimationClip clip, GameObject modelPrefab, Avatar avatar)
        {
            var temp = CreateSampleInstance(modelPrefab, avatar, out var animator);
            AnimatorController samplerController = null;
            try
            {
                Transform handL = FindBone(temp.transform, "L Hand");
                Transform handR = FindBone(temp.transform, "R Hand");
                if (animator == null || (handL == null && handR == null))
                {
                    Debug.LogWarning($"[DecoySetup] Could not find hand bones on the decoy model - cannot measure '{clip.name}'.");
                    return -1f;
                }

                // Pose only - hold in place
                samplerController = BeginSampling(animator, clip, applyRootMotion: false);

                const int steps = 90;
                float dt = clip.length / steps;
                float bestTime = -1f;
                float bestHeight = float.MinValue;
                for (int i = 0; i <= steps; i++)
                {
                    // Read while the sampler is still assigned (reassigning the
                    // controller rebinds and resets the pose)
                    float height = (handL != null ? handL.position.y : 0f) +
                                   (handR != null ? handR.position.y : 0f);
                    if (height > bestHeight)
                    {
                        bestHeight = height;
                        bestTime = i * dt;
                    }
                    if (i < steps)
                        animator.Update(dt);
                }
                return bestTime;
            }
            finally
            {
                if (samplerController != null)
                    Object.DestroyImmediate(samplerController);
                Object.DestroyImmediate(temp);
            }
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

        private static AnimationClip PickFallForwardClip(GameObject modelPrefab, Avatar avatar)
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
            }.Where(c => c != null).Select(c => MeasureFall(c, modelPrefab, avatar)).Where(m => m != null).ToList();

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
        /// Plays the humanoid clip through on a temp decoy instance (stepped
        /// Animator - humanoid muscle clips can't use SampleAnimation, and
        /// these clips carry their vertical drop in ROOT MOTION, so a static
        /// one-shot evaluate never leaves standing height) and measures where
        /// the body ends up relative to its starting facing.
        /// </summary>
        private static FallCandidate MeasureFall(AnimationClip clip, GameObject modelPrefab, Avatar avatar)
        {
            var temp = CreateSampleInstance(modelPrefab, avatar, out var animator);
            try
            {
                if (animator == null)
                {
                    Debug.LogWarning($"[DecoySetup] The decoy model has no Animator - cannot measure '{clip.name}'.");
                    return null;
                }

                Transform hips = FindBone(temp.transform, "Pelvis") ?? FindBone(temp.transform, "Hips");
                Transform head = FindBone(temp.transform, "Head");
                if (hips == null || head == null)
                {
                    Debug.LogWarning($"[DecoySetup] Could not find Pelvis/Head bones on the decoy model - cannot measure '{clip.name}'.");
                    return null;
                }

                Vector3 forward = temp.transform.forward;

                // The pose must be read while the sampling controller is still
                // assigned - reassigning runtimeAnimatorController rebinds the
                // Animator and resets every bone to the default pose
                var samplerController = BeginSampling(animator, clip, applyRootMotion: true);
                const int steps = 90;
                float dt = clip.length / steps;
                for (int i = 0; i < steps; i++)
                    animator.Update(dt);

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

        // ------------------------------------------------------------------
        // Model sampling
        // ------------------------------------------------------------------

        private static GameObject LoadModelPrefab()
        {
            var path = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                Debug.LogError($"[DecoySetup] decoy.glb not found (guid {ModelGuid}). Export it to Assets/Models/ and let Unity import it.");
            return prefab;
        }

        /// <summary>
        /// Yaw that turns the exported model to face +Z, measured once per run
        /// by DecoyAvatarSetup and then applied identically to the prefab and to
        /// every sampling instance - the avatar bakes the reference pose in, so
        /// the two must agree.
        /// </summary>
        private static float modelFacingYaw;

        /// <summary>
        /// Correction on ModelScale so the decoy stands the same height the
        /// placeholder did. Mecanim sizes a humanoid by its avatar's
        /// humanScale, which comes out of the REFERENCE POSE - and the decoy
        /// rig's hips sit a little lower in its export than the placeholder's
        /// did, so at the same localScale it would animate a few percent
        /// shorter. Measured against the placeholder rather than assumed, so a
        /// re-export from a different origin still lands.
        /// </summary>
        private static float modelScaleCompensation = 1f;

        private static float EffectiveModelScale => ModelScale * modelScaleCompensation;

        private static Avatar BuildAvatar(GameObject modelPrefab)
        {
            // Named and turned as it will be in the prefab: the avatar binds its
            // skeleton by transform name, root included, and bakes in the pose
            var temp = Object.Instantiate(modelPrefab, Vector3.zero, Quaternion.identity);
            temp.name = ModelName;
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                modelFacingYaw = DecoyAvatarSetup.MeasureFacingYaw(temp);
                DecoyAvatarSetup.ApplyFacingYaw(temp, modelFacingYaw);
                return DecoyAvatarSetup.Build(temp);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// Ratio between the placeholder's animated size and the decoy's, read
        /// straight off the two avatars (see modelScaleCompensation).
        /// </summary>
        private static float MeasureScaleCompensation(GameObject modelPrefab, Avatar avatar)
        {
            var clip = LoadClip(IdleFbxGuid, "S_Idle");
            if (clip == null)
                return 1f;

            var decoy = CreateSampleInstance(modelPrefab, avatar, out var decoyAnimator);
            float decoyScale = ReadHumanScale(decoy, decoyAnimator, clip);

            var placeholderPath = AssetDatabase.GUIDToAssetPath(DecoyAvatarSetup.SteveModelGuid);
            var placeholderPrefab = string.IsNullOrEmpty(placeholderPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(placeholderPath);
            if (placeholderPrefab == null)
                return 1f;

            var placeholder = Object.Instantiate(placeholderPrefab, Vector3.zero, Quaternion.identity);
            placeholder.hideFlags = HideFlags.HideAndDontSave;
            float placeholderScale = ReadHumanScale(placeholder, placeholder.GetComponent<Animator>(), clip);

            if (decoyScale <= 0.01f || placeholderScale <= 0.01f)
                return 1f;

            float compensation = placeholderScale / decoyScale;
            Debug.Log($"[DecoySetup] Size: the decoy's avatar reads humanScale {decoyScale:F3} against the placeholder's {placeholderScale:F3} " +
                      $"-> scaling the model {compensation:F3}x (localScale {ModelScale * compensation:F3}) so it stands the height the game is tuned for.");
            return compensation;
        }

        /// <summary>
        /// The avatar's own size measure, read with a clip playing so the
        /// Animator is bound. Destroys the instance.
        /// </summary>
        private static float ReadHumanScale(GameObject instance, Animator animator, AnimationClip clip)
        {
            AnimatorController sampler = null;
            try
            {
                if (animator == null)
                    return 0f;
                sampler = BeginSampling(animator, clip, applyRootMotion: false);
                return animator.humanScale;
            }
            finally
            {
                if (sampler != null)
                    Object.DestroyImmediate(sampler);
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// A throwaway decoy set up exactly like the runtime one - same root
        /// name, same avatar - so every measurement taken off it holds in game.
        /// </summary>
        private static GameObject CreateSampleInstance(GameObject modelPrefab, Avatar avatar, out Animator animator)
        {
            var temp = Object.Instantiate(modelPrefab, Vector3.zero, Quaternion.identity);
            temp.name = ModelName;
            temp.hideFlags = HideFlags.HideAndDontSave;
            DecoyAvatarSetup.ApplyFacingYaw(temp, modelFacingYaw);

            animator = temp.GetComponent<Animator>();
            if (animator == null)
                animator = temp.AddComponent<Animator>();
            animator.avatar = avatar;
            return temp;
        }

        /// <summary>
        /// Assigns a throwaway controller playing the clip and leaves the
        /// animator at t=0, ready to be stepped with animator.Update(dt). A raw
        /// PlayableGraph output in edit mode applies root motion but never
        /// writes the humanoid muscle pose to the bones; stepping the Animator
        /// by hand applies both. The caller destroys the returned controller
        /// AFTER reading the pose - reassigning runtimeAnimatorController
        /// rebinds the Animator and resets every bone to its default.
        /// </summary>
        private static AnimatorController BeginSampling(Animator animator, AnimationClip clip, bool applyRootMotion)
        {
            var controller = new AnimatorController { name = "DecoySetupSampler" };
            controller.AddLayer("Base");
            var state = controller.layers[0].stateMachine.AddState("Clip");
            state.motion = clip;

            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = applyRootMotion;

            animator.Play("Clip", 0, 0f);
            animator.Update(0f);
            return controller;
        }

        /// <summary>
        /// How far the model has to be lifted for its feet to rest on the floor,
        /// in model units at scale 1. The placeholder's origin sat exactly on
        /// its soles; the decoy rig's sits a few centimetres above them, so
        /// without this it stands buried to the ankles. Measured off the SKIN
        /// rather than the foot bone - the sole is the thing that touches - and
        /// off the animated pose rather than the rest pose, because with a
        /// humanoid avatar it is Mecanim that decides how high the hips sit.
        /// </summary>
        private static float MeasureGroundLift(GameObject modelPrefab, Avatar avatar,
            AnimationClip contactClip, IEnumerable<AnimationClip> alsoReport)
        {
            float contact = MeasureLowestSkinPoint(modelPrefab, avatar, contactClip);
            if (float.IsNaN(contact))
            {
                Debug.LogWarning("[DecoySetup] Could not measure the decoy's ground contact - leaving the model at the prefab origin.");
                return 0f;
            }

            float lift = -contact;
            var others = string.Join(", ", alsoReport
                .Where(c => c != null && c != contactClip)
                .Select(c => $"{c.name} {(MeasureLowestSkinPoint(modelPrefab, avatar, c) + lift) * EffectiveModelScale:+0.000;-0.000}m"));

            Debug.Log($"[DecoySetup] Ground fit: '{contactClip.name}' rests {contact * EffectiveModelScale:+0.000;-0.000}m off the floor at scale {EffectiveModelScale:F3} " +
                      $"-> lifting the model {lift * EffectiveModelScale:F3}m. Other clips' closest approach after the lift: {others}.");
            return lift;
        }

        /// <summary>
        /// Lowest point the skinned mesh reaches anywhere in the clip, in world
        /// units on an unscaled instance.
        /// </summary>
        private static float MeasureLowestSkinPoint(GameObject modelPrefab, Avatar avatar, AnimationClip clip)
        {
            var temp = CreateSampleInstance(modelPrefab, avatar, out var animator);
            AnimatorController samplerController = null;
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var skin = temp.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin == null || animator == null)
                    return float.NaN;

                samplerController = BeginSampling(animator, clip, applyRootMotion: false);

                const int steps = 16;
                float dt = clip.length / steps;
                float lowest = float.MaxValue;
                for (int i = 0; i <= steps; i++)
                {
                    // Baked without scale, so the renderer's own matrix (which
                    // carries the rig's centimetre-to-metre 0.01) is what puts
                    // the vertices in world space
                    skin.BakeMesh(baked, false);
                    var toWorld = skin.transform.localToWorldMatrix;
                    foreach (var vertex in baked.vertices)
                    {
                        float y = toWorld.MultiplyPoint3x4(vertex).y;
                        if (y < lowest)
                            lowest = y;
                    }
                    if (i < steps)
                        animator.Update(dt);
                }
                return lowest;
            }
            finally
            {
                Object.DestroyImmediate(baked);
                if (samplerController != null)
                    Object.DestroyImmediate(samplerController);
                Object.DestroyImmediate(temp);
            }
        }

        private static Transform FindBone(Transform root, string normalizedName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (DecoyController.NormalizeBoneName(t.name) == normalizedName)
                    return t;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Prefab
        // ------------------------------------------------------------------

        private static GameObject BuildDecoyPrefab(GameObject modelPrefab, Avatar avatar, AnimatorController controller)
        {
            var ragdollPrefab = DecoyRagdollSetup.Build(modelPrefab);
            if (ragdollPrefab == null)
                Debug.LogWarning("[DecoySetup] No decoy ragdoll was built - the catch will carry the animated model instead of ragdolling.");
            else
                ValidateRagdollLimbs(ragdollPrefab);

            // The rig's origin is not on its soles, so find the lift that puts
            // them on the floor. Idle is the contact reference (both feet
            // planted); the run cycles are reported against it.
            var idle = LoadClip(IdleFbxGuid, "S_Idle");
            float groundLift = idle == null ? 0f : MeasureGroundLift(modelPrefab, avatar, idle, new[]
            {
                LoadClip(JogFbxGuid, "S_Jog_F"),
                LoadClip(SprintFbxGuid, "S_Sprint"),
            });

            // Assemble fresh each run; SaveAsPrefabAsset over the same path keeps
            // the prefab GUID so scene references survive rebuilds.
            var root = new GameObject("Decoy");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
                model.name = ModelName;
                model.transform.SetParent(root.transform, false);
                // Lifted so its soles sit on the decoy root's origin - which is
                // ground level, fleeing along +Z. The turn that makes it face
                // +Z goes on the rig root inside, not here (see ApplyFacingYaw).
                model.transform.localPosition = new Vector3(0f, groundLift * EffectiveModelScale, 0f);
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * EffectiveModelScale;
                DecoyAvatarSetup.ApplyFacingYaw(model, modelFacingYaw);

                var animator = model.GetComponent<Animator>();
                if (animator == null)
                    animator = model.AddComponent<Animator>();
                animator.avatar = avatar;
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

                    string resolved = DecoyController.NormalizeBoneName(body.name);
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

    /// <summary>
    /// Re-exporting decoy.glb updates the mesh, materials and skeleton through
    /// the nested prefab instance on its own, but the avatar, the ragdoll and
    /// the measured ground lift are all baked against the rig - so a new export
    /// has to rebuild them.
    /// </summary>
    public class DecoyModelPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(DecoyAvatarSetup.DecoyModelGuid);
            if (string.IsNullOrEmpty(modelPath) || !importedAssets.Contains(modelPath))
                return;

            Debug.Log("[DecoySetup] decoy.glb re-imported - rebuilding the decoy avatar, ragdoll and prefab.");
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    DecoySetup.Run();
            };
        }
    }
}
