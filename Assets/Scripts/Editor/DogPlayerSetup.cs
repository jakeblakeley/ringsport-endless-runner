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
        private const int SetupVersion = 2;
        private const string VersionPrefKey = "RingSport.DogPlayerSetup.Version";

        private const string DogName = "Dog Model";
        private const string ControllerPath = "Assets/Animations/Player/DogPlayer.controller";
        private const string DashLoopPath = "Assets/Animations/Player/WL_Dash_Loop.anim";
        private const string FallbackMaterialPath = "Assets/Materials/DogPlayer.mat";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string AutoRunFailedKey = "DogPlayerSetup_AutoRunFailed";

        // Malbers Animal Controller (lite) asset GUIDs
        private const string ModelGuid = "08e48789449aae64095cc114539cb217";      // Wolf Lite v2.fbx
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

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Still importing/compiling - check again on the next editor tick
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            if (SessionState.GetBool(AutoRunFailedKey, false))
                return;

            var player = FindPlayer();
            if (player == null)
                return;

            bool dogMissing = player.transform.Find(DogName) == null;
            bool controllerStale =
                EditorPrefs.GetInt(VersionPrefKey, 0) < SetupVersion ||
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) == null;

            if (!dogMissing && !controllerStale)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                SessionState.SetBool(AutoRunFailedKey, true);
                Debug.LogError($"[DogPlayerSetup] Auto-setup failed: {e}");
            }
        }

        [MenuItem("Tools/RingSport/Setup Dog Player")]
        public static void Run()
        {
            var player = FindPlayer();
            if (player == null)
            {
                Debug.LogError("[DogPlayerSetup] No PlayerController found in the open scene.");
                return;
            }

            bool sceneWasDirty = player.scene.isDirty;

            var controller = BuildAnimatorController();
            if (controller == null)
                return;

            var animator = SetupDogModel(player, controller);
            if (animator == null)
                return;

            RemoveSphereVisual(player);
            WirePlayerAnimator(player, animator);
            AssetDatabase.SaveAssets();
            SavePlayerPrefab(player);

            EditorSceneManager.MarkSceneDirty(player.scene);
            if (!sceneWasDirty)
                EditorSceneManager.SaveScene(player.scene);
            else
                Debug.Log("[DogPlayerSetup] Scene had unsaved changes - dog added but scene NOT auto-saved. Save it when ready.");

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[DogPlayerSetup] Done (v{SetupVersion}). Dog model under '{player.name}', controller at {ControllerPath}, prefab at {PlayerPrefabPath}.");
        }

        private static GameObject FindPlayer()
        {
            return Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include)?.gameObject;
        }

        private static AnimatorController BuildAnimatorController()
        {
            var clips = new Dictionary<string, AnimationClip>
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
            };

            var missing = clips.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError($"[DogPlayerSetup] Missing Wolf Lite animation clips: {string.Join(", ", missing)}. Is the Malbers Animations package intact?");
                return null;
            }

            // Sprint gait: WL_Dash is the pack's distinct fast gait but doesn't loop,
            // so loop a copy of it - unless the clip travels in-pose, in which case
            // a sped-up run tracks the collider better.
            AnimationClip sprintMotion = null;
            float sprintTimeScale = 1f;
            var dashSource = LoadClip(DashFbxGuid, "WL_Dash");
            if (dashSource != null)
            {
                float travel = MeasureHorizontalTravel(dashSource);
                if (travel < 1f)
                {
                    sprintMotion = GetOrCreateLoopingCopy(dashSource, DashLoopPath);
                    Debug.Log($"[DogPlayerSetup] Sprint gait: WL_Dash loop (in-pose travel {travel:F2}m).");
                }
                else
                {
                    Debug.Log($"[DogPlayerSetup] WL_Dash travels {travel:F2}m in-pose; sprint falls back to sped-up run.");
                }
            }
            if (sprintMotion == null)
            {
                sprintMotion = clips["run"];
                sprintTimeScale = 1.35f;
            }

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
            var jump = sm.AddState("Jump", new Vector3(560f, 20f));
            jump.motion = clips["jump"];
            jump.speed = 1.3f;

            var toJump = locomotion.AddTransition(jump);
            toJump.hasExitTime = false;
            toJump.hasFixedDuration = true;
            toJump.duration = 0.08f;
            toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            var jumpLand = jump.AddTransition(locomotion);
            jumpLand.hasExitTime = true;
            jumpLand.exitTime = 0.5f;
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

            // --- Vault: forward leap after clamber success ---
            var vault = sm.AddState("Vault", new Vector3(560f, 320f));
            vault.motion = clips["vault"];
            vault.speed = 1.2f;

            var clamberToVault = clamber.AddTransition(vault);
            clamberToVault.hasExitTime = false;
            clamberToVault.hasFixedDuration = true;
            clamberToVault.duration = 0.05f;
            clamberToVault.AddCondition(AnimatorConditionMode.If, 0f, "Vault");

            var clamberOut = clamber.AddTransition(locomotion);
            clamberOut.hasExitTime = false;
            clamberOut.hasFixedDuration = true;
            clamberOut.duration = 0.2f;
            clamberOut.AddCondition(AnimatorConditionMode.IfNot, 0f, "Clamber");

            var vaultEnd = vault.AddTransition(locomotion);
            vaultEnd.hasExitTime = true;
            vaultEnd.exitTime = 0.8f;
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

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Animator SetupDogModel(GameObject player, AnimatorController controller)
        {
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelPrefab == null)
            {
                Debug.LogError("[DogPlayerSetup] Wolf Lite v2.fbx not found. Is the Malbers Animations package intact?");
                return null;
            }

            var existing = player.transform.Find(DogName);
            GameObject dog = existing != null ? existing.gameObject : null;
            if (dog == null)
            {
                dog = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, player.scene);
                dog.name = DogName;
                dog.transform.SetParent(player.transform, false);
            }

            // Player pivot sits at y=1 with the CharacterController capsule spanning
            // y 0..2 in world space; the model's origin is at its feet, so drop it
            // to the capsule's base. Model faces +Z, matching the -Z world scroll.
            dog.transform.localPosition = new Vector3(0f, -1f, 0f);
            dog.transform.localRotation = Quaternion.identity;
            dog.transform.localScale = Vector3.one;

            var animator = dog.GetComponent<Animator>();
            if (animator == null)
                animator = dog.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            FixMaterialsIfBroken(dog);
            return animator;
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
            so.ApplyModifiedPropertiesWithoutUndo();
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
    }
}
