using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using RingSport.Player;

namespace RingSport.Editor
{
    /// <summary>
    /// One-shot setup that swaps the placeholder sphere for the Malbers Wolf Lite
    /// dog model, builds a runner-specific AnimatorController from the Wolf Lite
    /// clips (run/sprint/strafe/jump/clamber/vault/death), wires the PlayerAnimator
    /// component, and saves the Player as a prefab.
    ///
    /// Runs automatically after script compilation if the dog is missing from the
    /// open scene; can also be run manually from the menu. Idempotent.
    /// </summary>
    public static class DogPlayerSetup
    {
        // Bump to make the auto-run rebuild the controller after changing this script
        private const int SetupVersion = 19;
        private const string VersionPrefKey = "RingSport.DogPlayerSetup.Version";

        // The retarget math is part of what the generated assets depend on, so a
        // change there has to invalidate them just like a change here does
        private static int EffectiveVersion => SetupVersion * 100 + CaicosRetarget.RetargetVersion;

        private const string DogName = "Dog Model";
        private const string ControllerPath = "Assets/Animations/Player/DogPlayer.controller";
        private const string DashLoopPath = "Assets/Animations/Player/WL_Dash_Loop.anim";
        private const string DodgeHopPath = "Assets/Animations/Player/WL_DodgeHop.anim";
        private const string JumpFlatPath = "Assets/Animations/Player/WL_Jump_InPlace_Flat.anim";
        private const string VaultInPlacePath = "Assets/Animations/Player/WL_Vault_InPlace.anim";
        // Must match the arc duration in PlayerController.AnimateOverObstacle -
        // the vault clip's takeoff->landing segment is sped to span this
        private const float VaultArcDuration = 0.2f;
        private const float DodgeHopHeightScale = 0.5f;
        private const string FallbackMaterialPath = "Assets/Materials/DogPlayer.mat";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        // The player model is caicos.glb, nested as a prefab instance so that
        // re-exporting the model from Blender updates the player automatically.
        // Its rig shares the Wolf Lite bone names, but nothing else lines up -
        // CaicosRetarget bakes every Malbers clip onto it (see that file).
        private const string ModelGuid = CaicosRetarget.CaicosModelGuid;         // Assets/Models/caicos.glb
        // Only the gameplay tuning is size-sensitive (jump heights, hurdle
        // clearance, camera framing), so the model is scaled back to the old
        // dog's footprint when the new one differs by more than this.
        private const float ScaleCompensationThreshold = 0.02f;

        // Malbers Animal Controller (lite) asset GUIDs - the clips' source rig
        private const string IdlesFbxGuid = "0eb22952662ada041b5429bc1421472e";   // WL_Idles.FBX
        private const string LedgeGrabFbxGuid = "144a4f1e0fc58eb4b8e452a69b1147b2"; // WL_LedgeGrab.fbx
        private const string WalkGuid = "6fc18097a7084a94d90ae044ae70cd14";       // WL_Walk.anim
        private const string WalkLeftGuid = "65871c00692145546940485b812fb75d";   // WL_Walk Left.anim
        private const string WalkRightGuid = "018739b2f735e1846bd2261b3c3cd8f2";  // WL_Walk Right.anim
        private const string RunGuid = "8db7dc07850a97041873b34d9781756a";        // WL_Run.anim
        private const string RunLeftGuid = "26895fe3464fdbe40bb05b65fe04986c";    // WL_Run_Left.anim
        private const string RunRightGuid = "c486a6b891fca5840aa052421c903829";   // WL_Run_Right.anim
        private const string JumpInPlaceGuid = "03d8d67d519ba8b44a9356d4badc4039"; // WL_Jump_InPlace.anim
        private const string JumpForwardGuid = "7ce4f711dd11df041898390c845db393"; // WL_Jump_Forward.anim
        private const string DashFbxGuid = "af25c5ee9aa180643a14d1acb874c030";    // WL_Dash.fbx
        private const string DeathGuid = "69e70d70d4cea1442bdbe47dc487a263";      // WL_Death1.anim
        private const string SleepFbxGuid = "558c52fb80d72814bbd94a05998c957f";   // WL_Sleep.FBX (sit/lie pose clips)
        private const string JumpInPlaceBakedGuid = "50050480bda5ca24abe00af5c60ffcf6"; // WL_Jump_InPlace_Baked.anim (Y-rise baked into pose)
        private const string Bark2Guid = "2418d78995a33784cbb79676b298922e";      // WL_Bark2.anim
        private const string ShakeWaterFbxGuid = "edafe4aa19de1234195b832fb3556e39"; // WL_ShakeWater.fbx
        private const string ActionsFbxGuid = "e014be69bd49c7d458293d2d8cd0e051"; // WL_Actions.FBX (howl, dig, eat...)

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            // Never touch assets or the scene mid-play, but keep waiting - a
            // domain reload only happens on compile, so if we give up here the
            // rebuild would silently never run after the play session ends.
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            var player = FindPlayer();
            if (player == null)
                return;

            var dog = player.transform.Find(DogName);
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            // Missing, or still the model we replaced - either way, rebuild
            bool dogMissing = dog == null || (modelPrefab != null && !IsInstanceOf(dog.gameObject, modelPrefab));
            var controllerAsset = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            // The version is also stamped into the controller itself (as the
            // default of an unused int parameter), so staleness detection works
            // even if EditorPrefs didn't persist
            var versionParam = controllerAsset == null
                ? null
                : controllerAsset.parameters.FirstOrDefault(p => p.name == "SetupVersion");
            bool controllerStale =
                controllerAsset == null ||
                EditorPrefs.GetInt(VersionPrefKey, 0) < EffectiveVersion ||
                versionParam == null ||
                versionParam.defaultInt < EffectiveVersion;

            if (!dogMissing && !controllerStale)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                // Most likely cause: play mode was entered mid-run. Re-queue so
                // the setup retries once the editor is idle again; a genuinely
                // persistent error will keep logging rather than silently stall.
                Debug.LogError($"[DogPlayerSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Dog Player")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[DogPlayerSetup] Cannot run during play mode - exit play mode first (the auto-run will then apply it).");
                return;
            }

            var player = FindPlayer();
            if (player == null)
            {
                Debug.LogError("[DogPlayerSetup] No PlayerController found in the open scene.");
                return;
            }

            bool sceneWasDirty = player.scene.isDirty;

            using var session = CaicosRetarget.BeginSession();
            if (session == null)
                return;

            var controller = BuildAnimatorController(session);
            if (controller == null)
                return;

            var animator = SetupDogModel(player, session, controller, out var ragdollPrefab);
            if (animator == null)
                return;

            RemoveSphereVisual(player);
            WirePlayerAnimator(player, animator);
            WirePlayerRagdoll(player, animator.gameObject, ragdollPrefab);
            AssetDatabase.SaveAssets();
            SavePlayerPrefab(player);

            // Play mode can begin while this runs; the asset work above is safe,
            // but scene operations would throw - skip them, they're a no-op on
            // idempotent re-runs anyway
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorSceneManager.MarkSceneDirty(player.scene);
                if (!sceneWasDirty)
                    EditorSceneManager.SaveScene(player.scene);
                else
                    Debug.Log("[DogPlayerSetup] Scene had unsaved changes - dog added but scene NOT auto-saved. Save it when ready.");
            }

            EditorPrefs.SetInt(VersionPrefKey, EffectiveVersion);
            Debug.Log($"[DogPlayerSetup] Done (v{SetupVersion}, retarget v{CaicosRetarget.RetargetVersion}). Dog model under '{player.name}', controller at {ControllerPath}, prefab at {PlayerPrefabPath}.");
        }

        private static GameObject FindPlayer()
        {
            return Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include)?.gameObject;
        }

        private static AnimatorState AddOneShotState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            var state = sm.AddState(name, position);
            state.motion = motion;
            state.speed = 1.8f;
            return state;
        }

        private static void AddPoseTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float threshold)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.15f;
            transition.AddCondition(mode, threshold, "Pose");
        }

        private static void AddDodgeTransition(AnimatorState from, AnimatorState to, string trigger, float offset)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.04f;
            transition.offset = offset;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddClipEndTransition(AnimatorState from, AnimatorState to)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = 0.9f;
            transition.hasFixedDuration = true;
            transition.duration = 0.1f;
        }

        /// <summary>
        /// Retargets a Malbers clip onto the caicos rig, caching by source clip so
        /// a clip used twice (the run cycle doubles as the sprint gait) is baked
        /// once and both references point at the same asset.
        /// </summary>
        private static AnimationClip Retarget(CaicosRetarget.Session session, AnimationClip source,
            Dictionary<AnimationClip, AnimationClip> cache, bool snapToGround = true)
        {
            if (source == null)
                return null;
            if (cache.TryGetValue(source, out var existing))
                return existing;

            var baked = CaicosRetarget.RetargetClip(session, source, $"{CaicosRetarget.OutputFolder}/{source.name}.anim", snapToGround);
            cache[source] = baked;
            return baked;
        }

        private static AnimatorController BuildAnimatorController(CaicosRetarget.Session session)
        {
            var sources = new Dictionary<string, AnimationClip>
            {
                ["idle"] = LoadClip(IdlesFbxGuid, "WL_Idle01"),
                ["walk"] = LoadClip(WalkGuid),
                ["walkLeft"] = LoadClip(WalkLeftGuid),
                ["walkRight"] = LoadClip(WalkRightGuid),
                ["run"] = LoadClip(RunGuid),
                ["runLeft"] = LoadClip(RunLeftGuid),
                ["runRight"] = LoadClip(RunRightGuid),
                ["jump"] = LoadClip(JumpInPlaceGuid),
                ["vault"] = LoadClip(JumpForwardGuid),
                ["clamber"] = LoadClip(LedgeGrabFbxGuid, "WL_LedgeGrab"),
                ["death"] = LoadClip(DeathGuid),
                ["sitEnter"] = LoadClip(SleepFbxGuid, "WL_Idle to Sit"),
                ["sit"] = LoadClip(SleepFbxGuid, "WL_Sit"),
                ["sitToLie"] = LoadClip(SleepFbxGuid, "WL_Sit to Lie"),
                ["lie"] = LoadClip(SleepFbxGuid, "WL_Lie01"),
                ["lieToSit"] = LoadClip(SleepFbxGuid, "WL_Lie to Sit"),
                ["sitExit"] = LoadClip(SleepFbxGuid, "WL_Sit to Idle"),
                ["dodgeHop"] = LoadClip(JumpInPlaceBakedGuid),
            };

            var missing = sources.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError($"[DogPlayerSetup] Missing Wolf Lite animation clips: {string.Join(", ", missing)}. Is the Malbers Animations package intact?");
                return null;
            }

            // Every clip is baked onto the caicos rig before it reaches the
            // controller: the Malbers clips carry the wolf's bone offsets, so
            // played raw they would force wolf proportions onto this dog.
            var retargetCache = new Dictionary<AnimationClip, AnimationClip>();
            // The clamber hangs off the palisade and the vault flies over it;
            // PlayerController's scripted arc owns their height, so they must not
            // be snapped down onto the floor like the ground gaits are.
            var airborne = new HashSet<string> { "clamber", "vault" };
            var clips = sources.ToDictionary(kv => kv.Key,
                kv => Retarget(session, kv.Value, retargetCache, !airborne.Contains(kv.Key)));
            if (clips.Any(kv => kv.Value == null))
            {
                Debug.LogError("[DogPlayerSetup] Retargeting failed for one or more clips - see the errors above.");
                return null;
            }

            // Sprint gait: the pack ships exactly three locomotion cycles
            // (walk/trot/run) - Malbers' own controller sprints by playing the
            // Run gait faster (a speed modifier), never a separate clip. The
            // action clips that look like sprint candidates are not gaits:
            // WL_Dash is a one-shot lunge (looping it hitched once a second,
            // v16) and WL_Charge_High/Low are HELD bolt-charge stances - they
            // loop because the charge can be held, and used as a gait they
            // freeze the legs mid-crouch (v17). Sprint is the run cycle at a
            // higher cadence; the speed-line trail and footstep pitch sell the
            // tier change.
            AnimationClip sprintMotion = clips["run"];
            float sprintTimeScale = 1.4f;
            AssetDatabase.DeleteAsset(DashLoopPath); // stale looped-dash sprint from v16
            Debug.Log($"[DogPlayerSetup] Sprint gait: WL_Run at {sprintTimeScale:F2}x cadence.");

            EnsureFolder("Assets/Animations");
            EnsureFolder("Assets/Animations/Player");

            // Rebuild in place (never delete the asset) so the controller keeps its
            // GUID and the Player prefab's Animator reference stays valid.
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
            controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Vault", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Clamber", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Pose", AnimatorControllerParameterType.Int);
            controller.AddParameter("DodgeLeft", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DodgeRight", AnimatorControllerParameterType.Trigger);
            // Version stamp read by TryAutoRun's staleness check; not used by gameplay
            controller.AddParameter(new AnimatorControllerParameter { name = "SetupVersion", type = AnimatorControllerParameterType.Int, defaultInt = EffectiveVersion });

            var sm = baseStateMachine;

            // --- Locomotion: 2D blend of strafe lean (x) vs idle->run->sprint (y) ---
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
            tree.AddChild(clips["walk"], new Vector2(0f, 0.4f));
            tree.AddChild(clips["walkLeft"], new Vector2(-1f, 0.4f));
            tree.AddChild(clips["walkRight"], new Vector2(1f, 0.4f));
            tree.AddChild(clips["run"], new Vector2(0f, 1f));
            tree.AddChild(clips["runLeft"], new Vector2(-1f, 1f));
            tree.AddChild(clips["runRight"], new Vector2(1f, 1f));
            tree.AddChild(sprintMotion, new Vector2(0f, 2f));
            tree.AddChild(clips["runLeft"], new Vector2(-1f, 2f));
            tree.AddChild(clips["runRight"], new Vector2(1f, 2f));

            // Per-child playback rates for the sprint tier (indices match AddChild order)
            var children = tree.children;
            children[7].timeScale = sprintTimeScale;
            children[8].timeScale = Mathf.Max(1.15f, sprintTimeScale);
            children[9].timeScale = Mathf.Max(1.15f, sprintTimeScale);
            tree.children = children;

            var locomotion = sm.AddState("Locomotion", new Vector3(280f, 120f));
            locomotion.motion = tree;
            // Sprint = same run cycle played faster (the Malbers pack has no separate sprint clip)
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = "AnimSpeed";
            sm.defaultState = locomotion;

            // --- Jump (physics drives the arc; clip plays in place) ---
            // The raw WL_Jump_InPlace carries its ~1.9m rise in the CG bone's
            // POSE curve (measured at runtime - it is NOT stripped as root
            // motion), so played as-is it stacks on the physics arc and the two
            // parabolas fight: the visual hovers at the apex and bounces back up
            // after touchdown. Ground-lock the clip (clamp Y to standing,
            // keeping the crouch and landing dips) so physics alone owns the arc.
            var jumpFlat = CreateGroundLockedCopy(clips["jump"], JumpFlatPath);
            float flatRise = MeasureHopRise(jumpFlat);
            if (flatRise > 0.1f)
                Debug.LogWarning($"[DogPlayerSetup] Ground-locked jump clip still rises {flatRise:F2}m - check which curve carries the rise.");
            else
                Debug.Log($"[DogPlayerSetup] Jump clip ground-locked (residual rise {flatRise:F2}m).");

            var jump = sm.AddState("Jump", new Vector3(560f, 20f));
            jump.motion = jumpFlat;
            jump.speed = 1.3f;

            var toJump = locomotion.AddTransition(jump);
            toJump.hasExitTime = false;
            toJump.hasFixedDuration = true;
            toJump.duration = 0.08f;
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            // Exit time tuned to the ~0.48s air time of the 1.7m jump
            var jumpLand = jump.AddTransition(locomotion);
            jumpLand.hasExitTime = true;
            jumpLand.exitTime = 0.4f;
            jumpLand.hasFixedDuration = true;
            jumpLand.duration = 0.2f;
            jumpLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            var jumpEnd = jump.AddTransition(locomotion);
            jumpEnd.hasExitTime = true;
            jumpEnd.exitTime = 0.95f;
            jumpEnd.hasFixedDuration = true;
            jumpEnd.duration = 0.25f;

            // --- Palisade clamber: paused clip, scrubbed by PlayerAnimator to match
            // the minigame's tap progress ---
            var clamber = sm.AddState("Clamber", new Vector3(280f, 320f));
            clamber.motion = clips["clamber"];
            clamber.speed = 0f;

            // --- Vault: forward leap after clamber success. The scripted arc
            // in AnimateOverObstacle owns ALL motion (0.2s over the palisade
            // top), so the raw WL_Jump_Forward clip needs work: its pose-baked
            // rise and forward travel are stripped (they'd fight the arc and
            // drift the model off the collider), the state enters AT the clip's
            // measured takeoff so the leap gesture plays at the top instead of
            // the crouch playing near the ground, and playback is sped so the
            // takeoff->landing segment spans the arc duration. ---
            float vaultTakeoff = FindTakeoffNormalizedTime(clips["vault"]);
            float vaultLanding = FindLandingNormalizedTime(clips["vault"]);
            if (vaultLanding <= vaultTakeoff + 0.05f)
            {
                Debug.LogWarning($"[DogPlayerSetup] Vault takeoff/landing measurement failed ({vaultTakeoff:P0}/{vaultLanding:P0}) - using defaults.");
                vaultTakeoff = 0.2f;
                vaultLanding = 0.8f;
            }

            var vaultClip = CreateGroundLockedCopy(clips["vault"], VaultInPlacePath);
            LockForwardTravel(vaultClip);

            float vaultAirSeconds = clips["vault"].length * (vaultLanding - vaultTakeoff);
            float vaultSpeed = Mathf.Clamp(vaultAirSeconds / VaultArcDuration, 1.5f, 6f);
            Debug.Log($"[DogPlayerSetup] Vault: takeoff {vaultTakeoff:P0}, landing {vaultLanding:P0}, air {vaultAirSeconds:F2}s -> speed {vaultSpeed:F2} to match the {VaultArcDuration}s arc.");

            var vault = sm.AddState("Vault", new Vector3(560f, 320f));
            vault.motion = vaultClip;
            vault.speed = vaultSpeed;

            var clamberToVault = clamber.AddTransition(vault);
            clamberToVault.hasExitTime = false;
            clamberToVault.hasFixedDuration = true;
            clamberToVault.duration = 0.08f;
            clamberToVault.offset = vaultTakeoff;
            clamberToVault.AddCondition(AnimatorConditionMode.If, 0f, "Vault");

            var clamberOut = clamber.AddTransition(locomotion);
            clamberOut.hasExitTime = false;
            clamberOut.hasFixedDuration = true;
            clamberOut.duration = 0.2f;
            clamberOut.AddCondition(AnimatorConditionMode.IfNot, 0f, "Clamber");

            // Blend back to locomotion as soon as the clip's landing moment
            // passes; the 0.25s crossfade covers the landing recovery
            var vaultEnd = vault.AddTransition(locomotion);
            vaultEnd.hasExitTime = true;
            vaultEnd.exitTime = Mathf.Min(0.95f, vaultLanding + 0.05f);
            vaultEnd.hasFixedDuration = true;
            vaultEnd.duration = 0.25f;

            // --- Death: entered from anywhere, no way out (reset via animator.Play) ---
            var death = sm.AddState("Death", new Vector3(560f, 480f));
            death.motion = clips["death"];

            // Death registered before Clamber so a same-frame Die wins the AnyState race
            var anyToDeath = sm.AddAnyStateTransition(death);
            anyToDeath.hasExitTime = false;
            anyToDeath.hasFixedDuration = true;
            anyToDeath.duration = 0.15f;
            anyToDeath.canTransitionToSelf = false;
            anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            var anyToClamber = sm.AddAnyStateTransition(clamber);
            anyToClamber.hasExitTime = false;
            anyToClamber.hasFixedDuration = true;
            anyToClamber.duration = 0.1f;
            anyToClamber.canTransitionToSelf = false;
            anyToClamber.AddCondition(AnimatorConditionMode.If, 0f, "Clamber");

            // --- Simon Says poses: Pose 0 = stand, 1 = sit, 2 = down/lie.
            // Routed through the authored transition clips; a two-step change
            // (stand<->down) chains through Sit automatically. ---
            var sitEnter = AddOneShotState(sm, "Sit Enter", clips["sitEnter"], new Vector3(0f, 220f));
            var sit = sm.AddState("Sit", new Vector3(0f, 320f));
            sit.motion = clips["sit"];
            var sitToLie = AddOneShotState(sm, "Sit To Lie", clips["sitToLie"], new Vector3(0f, 420f));
            var lie = sm.AddState("Lie", new Vector3(0f, 520f));
            lie.motion = clips["lie"];
            var lieToSit = AddOneShotState(sm, "Lie To Sit", clips["lieToSit"], new Vector3(-160f, 420f));
            var sitExit = AddOneShotState(sm, "Sit Exit", clips["sitExit"], new Vector3(-160f, 220f));

            AddPoseTransition(locomotion, sitEnter, AnimatorConditionMode.Greater, 0f);
            AddClipEndTransition(sitEnter, sit);
            AddPoseTransition(sit, sitToLie, AnimatorConditionMode.Equals, 2f);
            AddClipEndTransition(sitToLie, lie);
            AddPoseTransition(lie, lieToSit, AnimatorConditionMode.Less, 2f);
            AddClipEndTransition(lieToSit, sit);
            AddPoseTransition(sit, sitExit, AnimatorConditionMode.Equals, 0f);
            AddClipEndTransition(sitExit, locomotion);

            // --- Mini-level lane dodges: a quick hop using the Baked in-place
            // jump - the vertical rise is baked into the pose so the dog really
            // leaves the ground, while the code-driven lane lerp supplies the
            // sideways motion. The state starts at the clip's measured takeoff
            // moment so the hop is airborne the instant the lane change begins. ---
            float originalRise = MeasureHopRise(clips["dodgeHop"]);
            var dodgeHop = CreateScaledHopClip(clips["dodgeHop"], DodgeHopHeightScale, DodgeHopPath);
            float scaledRise = MeasureHopRise(dodgeHop);
            if (originalRise > 0.05f && scaledRise > originalRise * (DodgeHopHeightScale + 0.25f))
                Debug.LogWarning($"[DogPlayerSetup] Dodge hop scaling had little effect ({originalRise:F2}m -> {scaledRise:F2}m) - check which curve carries the rise.");
            else
                Debug.Log($"[DogPlayerSetup] Dodge hop height scaled {originalRise:F2}m -> {scaledRise:F2}m ({DodgeHopHeightScale:P0}).");

            float dodgeOffset = FindTakeoffNormalizedTime(dodgeHop);
            Debug.Log($"[DogPlayerSetup] Dodge hop takeoff measured at {dodgeOffset:P0}; dodge states start there.");

            var dodgeLeft = sm.AddState("Dodge Left", new Vector3(560f, 620f));
            dodgeLeft.motion = dodgeHop;
            dodgeLeft.speed = 3.2f;
            var dodgeRight = sm.AddState("Dodge Right", new Vector3(280f, 620f));
            dodgeRight.motion = dodgeHop;
            dodgeRight.speed = 3.2f;

            // Trigger transitions first so chained dodges beat the clip-end exit
            AddDodgeTransition(locomotion, dodgeLeft, "DodgeLeft", dodgeOffset);
            AddDodgeTransition(locomotion, dodgeRight, "DodgeRight", dodgeOffset);
            AddDodgeTransition(dodgeLeft, dodgeLeft, "DodgeLeft", dodgeOffset);
            AddDodgeTransition(dodgeLeft, dodgeRight, "DodgeRight", dodgeOffset);
            AddDodgeTransition(dodgeRight, dodgeRight, "DodgeRight", dodgeOffset);
            AddDodgeTransition(dodgeRight, dodgeLeft, "DodgeLeft", dodgeOffset);
            AddClipEndTransition(dodgeLeft, locomotion);
            AddClipEndTransition(dodgeRight, locomotion);

            // --- Home screen flourishes: one-shot character moments PlayerAnimator
            // CrossFades into (no parameters - the code targets the state hash
            // directly) while the dog idles facing the camera; each exits back to
            // Locomotion on clip end. A missing clip skips its state - the runtime
            // guards with Animator.HasState. Names must match
            // PlayerAnimator.FlourishHashes. ---
            var flourishes = new (string state, AnimationClip clip)[]
            {
                ("Flourish Bark", Retarget(session, LoadClip(Bark2Guid), retargetCache)),
                ("Flourish Shake", Retarget(session, LoadClip(ShakeWaterFbxGuid, "WL_ShakeWater"), retargetCache)),
                ("Flourish Howl", Retarget(session, LoadClip(ActionsFbxGuid, "WL_Howl"), retargetCache)),
                ("Flourish Glance", Retarget(session, LoadClip(IdlesFbxGuid, "WL_Idle02"), retargetCache)),
            };
            float flourishY = 20f;
            foreach (var (flourishName, flourishClip) in flourishes)
            {
                if (flourishClip == null)
                {
                    Debug.LogWarning($"[DogPlayerSetup] Clip for '{flourishName}' not found - state skipped.");
                    continue;
                }

                var flourish = sm.AddState(flourishName, new Vector3(840f, flourishY));
                flourish.motion = flourishClip;
                AddClipEndTransition(flourish, locomotion);
                flourishY += 100f;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Animator SetupDogModel(GameObject player, CaicosRetarget.Session session,
            AnimatorController controller, out GameObject ragdollPrefab)
        {
            ragdollPrefab = null;
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null)
            {
                Debug.LogError($"[DogPlayerSetup] caicos.glb not found (guid {ModelGuid}). Export it to Assets/Models/ and let Unity import it.");
                return null;
            }

            // Gameplay (jump heights, hurdle clearance, camera framing) is tuned
            // against the old dog's size, so a differently-sized model is scaled
            // back to the same footprint rather than re-tuning the whole game.
            float modelScale = 1f;
            if (Mathf.Abs(session.SizeRatio - 1f) > ScaleCompensationThreshold)
            {
                modelScale = 1f / session.SizeRatio;
                Debug.Log($"[DogPlayerSetup] Model is {session.SizeRatio:P0} of the old dog's size - compensating with localScale {modelScale:F3} so the gameplay tuning still holds.");
            }

            // Player pivot sits at y=1 with the CharacterController capsule spanning
            // y 0..2 in world space; the model's origin is at its feet, so drop it
            // to the capsule's base. Model faces +Z, matching the -Z world scroll.
            // Ground contact is handled per clip by the retargeter's snap stage,
            // so there is no correction to apply here.
            var localPosition = new Vector3(0f, -1f, 0f);

            ragdollPrefab = CaicosRagdollSetup.Build(modelPrefab, modelScale);

            // Swap inside the prefab ASSET, not the scene instance: the old wolf
            // skeleton is baked into Player.prefab, and editing the asset is what
            // pushes the new model out to every instance (and keeps the caicos
            // model a nested prefab instance, so re-exports propagate).
            if (!SwapModelInPrefabAsset(modelPrefab, controller, localPosition, modelScale, ragdollPrefab))
                SwapModelInScene(player, modelPrefab, controller, localPosition, modelScale);

            var dog = player.transform.Find(DogName);
            if (dog == null)
            {
                Debug.LogError($"[DogPlayerSetup] '{DogName}' is missing from the player after the model swap.");
                return null;
            }

            FixMaterialsIfBroken(dog.gameObject);
            return dog.GetComponent<Animator>();
        }

        /// <summary>
        /// Replaces the "Dog Model" child of the Player prefab asset with a fresh
        /// nested instance of the model prefab. Returns false if the prefab asset
        /// doesn't exist yet (first-time setup), so the caller can fall back to
        /// building it in the scene.
        /// </summary>
        private static bool SwapModelInPrefabAsset(GameObject modelPrefab, AnimatorController controller,
            Vector3 localPosition, float modelScale, GameObject ragdollPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
                return false;

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var dog = BuildDogModel(root.transform, modelPrefab, controller, localPosition, modelScale);

                // The animator and ragdoll references live on the prefab's own
                // components, so rewire them here rather than as scene overrides
                var playerAnimator = root.GetComponent<PlayerAnimator>();
                if (playerAnimator != null)
                {
                    var so = new SerializedObject(playerAnimator);
                    so.FindProperty("animator").objectReferenceValue = dog.GetComponent<Animator>();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                var playerRagdoll = root.GetComponent<PlayerRagdoll>();
                if (playerRagdoll != null)
                {
                    var so = new SerializedObject(playerRagdoll);
                    so.FindProperty("dogModel").objectReferenceValue = dog;
                    if (ragdollPrefab != null)
                        so.FindProperty("ragdollPrefab").objectReferenceValue = ragdollPrefab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.ImportAsset(PlayerPrefabPath);
            return true;
        }

        private static void SwapModelInScene(GameObject player, GameObject modelPrefab, AnimatorController controller,
            Vector3 localPosition, float modelScale)
        {
            BuildDogModel(player.transform, modelPrefab, controller, localPosition, modelScale);
        }

        /// <summary>
        /// Ensures <paramref name="parent"/> has a "Dog Model" child that is a
        /// prefab instance of the current model, configured for the runner's
        /// animator. An existing child from a different model is discarded.
        /// </summary>
        private static GameObject BuildDogModel(Transform parent, GameObject modelPrefab, AnimatorController controller,
            Vector3 localPosition, float modelScale)
        {
            var existing = parent.Find(DogName);
            if (existing != null && !IsInstanceOf(existing.gameObject, modelPrefab))
            {
                Object.DestroyImmediate(existing.gameObject);
                existing = null;
            }

            GameObject dog;
            if (existing != null)
            {
                dog = existing.gameObject;
            }
            else
            {
                dog = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, parent);
                dog.name = DogName;
            }

            dog.transform.SetParent(parent, false);
            dog.transform.localPosition = localPosition;
            dog.transform.localRotation = Quaternion.identity;
            dog.transform.localScale = Vector3.one * modelScale;

            var animator = dog.GetComponent<Animator>();
            if (animator == null)
                animator = dog.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            // Deliberately no Avatar: an Avatar makes the Animator treat the root
            // bone's curve as root motion, and the retargeted clips carry the CG
            // rise as a plain pose curve that the clip post-processing measures.
            animator.avatar = null;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            return dog;
        }

        private static bool IsInstanceOf(GameObject candidate, GameObject prefab)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            return source != null && AssetDatabase.GetAssetPath(source) == AssetDatabase.GetAssetPath(prefab);
        }

        private static void FixMaterialsIfBroken(GameObject dog)
        {
            foreach (var renderer in dog.GetComponentsInChildren<Renderer>())
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
                Debug.Log($"[DogPlayerSetup] Replaced unsupported material(s) on '{renderer.name}' with {FallbackMaterialPath}.");
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
                Debug.LogError("[DogPlayerSetup] No URP shader found for the fallback dog material.");
                return null;
            }

            var material = new Material(shader) { color = new Color(0.45f, 0.36f, 0.28f) };
            AssetDatabase.CreateAsset(material, FallbackMaterialPath);
            return material;
        }

        private static void RemoveSphereVisual(GameObject player)
        {
            var model = player.transform.Find("Playermodel");
            if (model == null)
                return;

            // Keep the GameObject and its trigger collider - only strip the sphere visuals
            var meshFilter = model.GetComponent<MeshFilter>();
            if (meshFilter != null)
                Object.DestroyImmediate(meshFilter);

            var meshRenderer = model.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                Object.DestroyImmediate(meshRenderer);
        }

        private static void WirePlayerAnimator(GameObject player, Animator animator)
        {
            var playerAnimator = player.GetComponent<PlayerAnimator>();
            if (playerAnimator == null)
                playerAnimator = player.AddComponent<PlayerAnimator>();

            var so = new SerializedObject(playerAnimator);
            so.FindProperty("animator").objectReferenceValue = animator;

            // One-time migrations from stale defaults (respect user tweaks).
            // v13 rebalance: the envelope math previously capped weights at ~0.4
            // (Mathf.SmoothStep misuse); with the fixed curves reaching 1.0 the
            // angles come DOWN so the on-screen result stays near what was tuned
            var tiltProp = so.FindProperty("dodgeTiltAngle");
            if (tiltProp != null && (Mathf.Approximately(tiltProp.floatValue, 12f) ||
                                     Mathf.Approximately(tiltProp.floatValue, 20f) ||
                                     Mathf.Approximately(tiltProp.floatValue, 28f)))
                tiltProp.floatValue = 15f;

            var hopPitchProp = so.FindProperty("dodgeHopPitchAngle");
            if (hopPitchProp != null && Mathf.Approximately(hopPitchProp.floatValue, 18f))
                hopPitchProp.floatValue = 8f;

            // Changing a [SerializeField] C# default does NOT reach a scene
            // instance that's already open - domain reload restores the live
            // value - so the turn angle must be written explicitly (stale
            // defaults 60/90 -> 50; a hand-tweaked value is left alone)
            var turnProp = so.FindProperty("dodgeTurnAngle");
            if (turnProp != null && (Mathf.Approximately(turnProp.floatValue, 60f) ||
                                     Mathf.Approximately(turnProp.floatValue, 90f)))
                turnProp.floatValue = 50f;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WirePlayerRagdoll(GameObject player, GameObject dogModel, GameObject ragdollPrefab)
        {
            if (ragdollPrefab == null)
            {
                Debug.LogWarning("[DogPlayerSetup] No ragdoll prefab was generated - death falls back to the Die animation.");
                return;
            }

            var playerRagdoll = player.GetComponent<PlayerRagdoll>();
            bool added = playerRagdoll == null;
            if (added)
                playerRagdoll = player.AddComponent<PlayerRagdoll>();

            var so = new SerializedObject(playerRagdoll);
            so.FindProperty("ragdollPrefab").objectReferenceValue = ragdollPrefab;
            so.FindProperty("dogModel").objectReferenceValue = dogModel;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Push the added component into the Player prefab asset so it isn't
            // just a scene-instance override.
            if (added && PrefabUtility.IsPartOfPrefabInstance(player) && PrefabUtility.IsAddedComponentOverride(playerRagdoll))
            {
                try
                {
                    PrefabUtility.ApplyAddedComponent(playerRagdoll, PlayerPrefabPath, InteractionMode.AutomatedAction);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[DogPlayerSetup] Could not apply PlayerRagdoll to the prefab (left as scene override): {e.Message}");
                }
            }
        }

        private static void SavePlayerPrefab(GameObject player)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(player))
                return;

            EnsureFolder("Assets/Prefabs");
            PrefabUtility.SaveAsPrefabAssetAndConnect(player, PlayerPrefabPath, InteractionMode.AutomatedAction, out bool success);
            if (!success)
                Debug.LogWarning("[DogPlayerSetup] Could not save the Player as a prefab; scene setup is still complete.");
        }

        /// <summary>
        /// How far the model's pelvis moves horizontally over the clip when sampled
        /// directly (i.e. travel baked into the pose, which root-motion settings
        /// can't cancel). Used to reject clips that would drift off the collider.
        /// </summary>
        private static float MeasureHorizontalTravel(AnimationClip clip)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null || clip == null)
                return 0f;

            var temp = Object.Instantiate(modelPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var pelvis = temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pelvis") ?? temp.transform;
                clip.SampleAnimation(temp, 0f);
                Vector3 start = pelvis.position;
                clip.SampleAnimation(temp, clip.length);
                Vector3 end = pelvis.position;
                start.y = 0f;
                end.y = 0f;
                return Vector3.Distance(start, end);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// Samples the clip's pelvis height to find where the (pose-baked) jump
        /// actually leaves the ground: the first time it rises meaningfully above
        /// its starting height, past the crouch dip. Returns a normalized time.
        /// </summary>
        private static float FindTakeoffNormalizedTime(AnimationClip clip)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null || clip == null)
                return 0f;

            var temp = Object.Instantiate(modelPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var pelvis = temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pelvis") ?? temp.transform;

                const int samples = 60;
                var heights = new float[samples + 1];
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(temp, clip.length * i / samples);
                    heights[i] = pelvis.position.y;
                }

                float standing = heights[0];
                float peak = heights.Max();
                if (peak - standing < 0.05f)
                    return 0f;

                // The crouch dips below standing height; takeoff is the first
                // sample clearly above it on the way to the peak
                float threshold = standing + 0.15f * (peak - standing);
                for (int i = 0; i <= samples; i++)
                {
                    if (heights[i] >= threshold)
                        return Mathf.Max(0f, (float)i / samples - 0.04f);
                }

                return 0f;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// Copy of the hop clip with its vertical rise scaled around the standing
        /// baseline. The Y motion lives in the clip's curves (pose-baked), so
        /// halving the hop height means editing the curves, not the state speed.
        /// </summary>
        private static AnimationClip CreateScaledHopClip(AnimationClip source, float heightScale, string path)
        {
            AssetDatabase.DeleteAsset(path);

            var copy = Object.Instantiate(source);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);

            // The vertical rise lives in whichever bone actually moves (the CG
            // root bone for the Malbers rig, "RootT.y" for extracted root motion)
            // - don't guess names, scale every Y curve with meaningful range
            int scaledCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            {
                bool isPositionY = binding.propertyName == "m_LocalPosition.y" || binding.propertyName == "RootT.y";
                if (!isPositionY)
                    continue;

                var curve = AnimationUtility.GetEditorCurve(copy, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;

                float min = curve.keys.Min(k => k.value);
                float max = curve.keys.Max(k => k.value);
                if (max - min < 0.1f)
                    continue;

                var keys = curve.keys;
                float baseline = keys[0].value;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value = baseline + (keys[i].value - baseline) * heightScale;
                    keys[i].inTangent *= heightScale;
                    keys[i].outTangent *= heightScale;
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(copy, binding, curve);
                scaledCurves++;
                Debug.Log($"[DogPlayerSetup] Scaled Y curve '{binding.path}/{binding.propertyName}' (range {max - min:F2}m).");
            }

            if (scaledCurves == 0)
                Debug.LogWarning("[DogPlayerSetup] No vertical curve found to scale in the dodge hop clip - hop height unchanged.");

            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        /// <summary>
        /// Mirror of FindTakeoffNormalizedTime for the way down: the first
        /// sample after the peak where the pelvis is back near standing height.
        /// </summary>
        private static float FindLandingNormalizedTime(AnimationClip clip)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null || clip == null)
                return 1f;

            var temp = Object.Instantiate(modelPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var pelvis = temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pelvis") ?? temp.transform;

                const int samples = 60;
                var heights = new float[samples + 1];
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(temp, clip.length * i / samples);
                    heights[i] = pelvis.position.y;
                }

                float standing = heights[0];
                float peak = heights.Max();
                if (peak - standing < 0.05f)
                    return 1f;

                int peakIndex = System.Array.IndexOf(heights, peak);
                float threshold = standing + 0.15f * (peak - standing);
                for (int i = peakIndex; i <= samples; i++)
                {
                    if (heights[i] <= threshold)
                        return Mathf.Min(1f, (float)i / samples + 0.02f);
                }

                return 1f;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// Flattens every meaningful Z position curve to its first key, killing
        /// the pose-baked forward lunge so the model stays on the collider
        /// while scripted motion (the vault arc + world scroll) does the travel.
        /// </summary>
        private static void LockForwardTravel(AnimationClip clip)
        {
            int lockedCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                bool isPositionZ = binding.propertyName == "m_LocalPosition.z" || binding.propertyName == "RootT.z";
                if (!isPositionZ)
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;

                float min = curve.keys.Min(k => k.value);
                float max = curve.keys.Max(k => k.value);
                if (max - min < 0.1f)
                    continue;

                var keys = curve.keys;
                float baseline = keys[0].value;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value = baseline;
                    keys[i].inTangent = 0f;
                    keys[i].outTangent = 0f;
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                lockedCurves++;
                Debug.Log($"[DogPlayerSetup] Locked Z travel curve '{binding.path}/{binding.propertyName}' (was {min:F2}..{max:F2}).");
            }

            if (lockedCurves == 0)
                Debug.Log("[DogPlayerSetup] No Z travel found to lock in the vault clip.");
            EditorUtility.SetDirty(clip);
        }

        /// <summary>
        /// Copy of a clip with every meaningful Y position curve clamped to its
        /// own resting value (the last key - the clip's recovery/standing pose),
        /// so the airborne rise disappears while crouch and landing dips are
        /// kept. Used for the physics-driven jump, where code owns the arc and
        /// any pose-baked rise fights it.
        /// </summary>
        private static AnimationClip CreateGroundLockedCopy(AnimationClip source, string path)
        {
            AssetDatabase.DeleteAsset(path);

            var copy = Object.Instantiate(source);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);

            int lockedCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            {
                bool isPositionY = binding.propertyName == "m_LocalPosition.y" || binding.propertyName == "RootT.y";
                if (!isPositionY)
                    continue;

                var curve = AnimationUtility.GetEditorCurve(copy, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;

                float min = curve.keys.Min(k => k.value);
                float max = curve.keys.Max(k => k.value);
                if (max - min < 0.1f)
                    continue;

                var keys = curve.keys;
                float standing = keys[keys.Length - 1].value;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i].value > standing)
                    {
                        keys[i].value = standing;
                        keys[i].inTangent = 0f;
                        keys[i].outTangent = 0f;
                    }
                }
                curve.keys = keys;
                AnimationUtility.SetEditorCurve(copy, binding, curve);
                lockedCurves++;
                Debug.Log($"[DogPlayerSetup] Ground-locked Y curve '{binding.path}/{binding.propertyName}' (was {min:F2}..{max:F2}, standing {standing:F2}).");
            }

            if (lockedCurves == 0)
                Debug.LogWarning("[DogPlayerSetup] No vertical curve found to ground-lock in the jump clip - clip unchanged.");

            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        /// <summary>Peak pelvis rise above standing height over the clip, in meters.</summary>
        private static float MeasureHopRise(AnimationClip clip)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null || clip == null)
                return 0f;

            var temp = Object.Instantiate(modelPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var pelvis = temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pelvis") ?? temp.transform;

                const int samples = 40;
                float standing = 0f;
                float peak = float.MinValue;
                for (int i = 0; i <= samples; i++)
                {
                    clip.SampleAnimation(temp, clip.length * i / samples);
                    float height = pelvis.position.y;
                    if (i == 0)
                        standing = height;
                    if (height > peak)
                        peak = height;
                }

                return Mathf.Max(0f, peak - standing);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static AnimationClip GetOrCreateLoopingCopy(AnimationClip source, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
                return existing;

            var copy = Object.Instantiate(source);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

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

        /// <summary>Forces the next idle moment to redo the setup from scratch.</summary>
        internal static void InvalidateAndRerun()
        {
            EditorPrefs.SetInt(VersionPrefKey, 0);
            EditorApplication.delayCall += TryAutoRun;
        }
    }

    /// <summary>
    /// Re-exporting caicos.glb updates the mesh, materials and skeleton through
    /// the nested prefab on its own, but the retargeted clips are baked against
    /// the rig's rest pose - so a new export has to rebake them.
    /// </summary>
    public class CaicosModelPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(CaicosRetarget.CaicosModelGuid);
            if (string.IsNullOrEmpty(modelPath) || !importedAssets.Contains(modelPath))
                return;

            Debug.Log("[DogPlayerSetup] caicos.glb re-imported - rebaking the retargeted clips.");
            DogPlayerSetup.InvalidateAndRerun();
        }
    }
}
