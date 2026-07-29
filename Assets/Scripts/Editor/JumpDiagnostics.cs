using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// TEMPORARY diagnostic (safe to delete): steps the DogPlayer controller
    /// through a jump exactly like runtime (trigger + Grounded from the physics
    /// parabola) and records the pose's vertical motion, to find the source of
    /// the visual stutter near the jump apex. Requested via a marker file so it
    /// only runs when the current investigation asks for it.
    /// </summary>
    [InitializeOnLoad]
    public static class JumpDiagnostics
    {
        private const string ScratchDir = "/private/tmp/claude-501/-Users-jakeblakeley-Documents-ringsport-endless-runner/5f29540a-7daa-43cf-85c3-d2fe1b1066e2/scratchpad";
        private static readonly string RequestPath = ScratchDir + "/jump_diag_request.txt";
        private static readonly string ResultPath = ScratchDir + "/jump_diag_result.csv";
        private static readonly string ErrorPath = ScratchDir + "/jump_diag_error.txt";

        private const string ControllerPath = "Assets/Animations/Player/DogPlayer.controller";
        private const string ModelGuid = "08e48789449aae64095cc114539cb217"; // Wolf Lite v2.fbx

        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        static JumpDiagnostics()
        {
            EditorApplication.delayCall += TryRun;
        }

        [MenuItem("Tools/RingSport/Debug/Jump Diagnostics")]
        public static void RunManual()
        {
            Run();
        }

        private static void TryRun()
        {
            if (!File.Exists(RequestPath))
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }

            // Wait until DogPlayerSetup's auto-run has rebuilt the controller to
            // the current version, so we measure the new Jump state, not the old
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var versionParam = ctrl == null ? null : ctrl.parameters.FirstOrDefault(p => p.name == "SetupVersion");
            if (versionParam == null || versionParam.defaultInt < 14)
            {
                EditorApplication.delayCall += TryRun;
                return;
            }

            File.Delete(RequestPath);
            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                File.WriteAllText(ErrorPath, e.ToString());
                Debug.LogError($"[JumpDiagnostics] Failed: {e}");
            }
        }

        private static void Run()
        {
            // Live jump physics from the Player prefab
            float jumpHeight = 1.12f;
            float gravity = 50f;
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var pc = playerPrefab != null ? playerPrefab.GetComponent<RingSport.Player.PlayerController>() : null;
            if (pc != null)
            {
                var so = new SerializedObject(pc);
                jumpHeight = so.FindProperty("jumpHeight").floatValue;
                gravity = Mathf.Abs(so.FindProperty("gravity").floatValue);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var modelPath = AssetDatabase.GUIDToAssetPath(ModelGuid);
            var modelPrefab = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (controller == null || modelPrefab == null)
            {
                File.WriteAllText(ErrorPath, $"controller null? {controller == null}, model null? {modelPrefab == null}");
                return;
            }

            var temp = Object.Instantiate(modelPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = temp.GetComponent<Animator>();
                if (animator == null)
                    animator = temp.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();

                var pelvis = temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pelvis");
                var cg = temp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "CG");
                if (pelvis == null)
                {
                    File.WriteAllText(ErrorPath, "No Pelvis bone found");
                    return;
                }

                animator.SetFloat("MoveSpeed", 1f);
                animator.SetFloat("AnimSpeed", 1f);
                animator.SetBool("Grounded", true);
                animator.Play("Locomotion", 0, 0f);
                animator.Update(0f);

                const float dt = 1f / 60f;

                // Warm up half a second of run cycle and capture the pose baseline
                float baselineSum = 0f;
                int baselineCount = 0;
                for (int i = 0; i < 30; i++)
                {
                    animator.Update(dt);
                    baselineSum += pelvis.position.y;
                    baselineCount++;
                }
                float baseline = baselineSum / baselineCount;

                // Jump: physics parabola drives Grounded exactly like PlayerController
                float v0 = Mathf.Sqrt(jumpHeight * 2f * gravity);
                float airTime = 2f * v0 / gravity;

                var sb = new StringBuilder();
                sb.AppendLine($"# h={jumpHeight}, g={gravity}, baseline pelvis Y (run cycle avg): {baseline:F4}, v0={v0:F3}, airTime={airTime:F4}");
                sb.AppendLine("t,capsuleY,pelvisPoseY,cgPoseY,state,normTime,inTransition");

                animator.SetTrigger("Jump");

                float capsuleY = 0f;
                float vy = v0;
                for (float t = 0f; t <= 1.3f; t += dt)
                {
                    bool grounded = t > 0f && capsuleY <= 0f;
                    animator.SetBool("Grounded", grounded);
                    animator.Update(dt);

                    // Integrate capsule exactly like PlayerController.Update:
                    // gravity first, then Move
                    if (!grounded || t == 0f)
                    {
                        vy += -gravity * dt;
                        capsuleY = Mathf.Max(0f, capsuleY + vy * dt);
                    }

                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    string state = info.IsName("Jump") ? "Jump" : info.IsName("Locomotion") ? "Locomotion" : "other";
                    sb.AppendLine($"{t:F4},{capsuleY:F4},{pelvis.position.y:F4},{(cg != null ? cg.position.y : 0f):F4},{state},{info.normalizedTime:F3},{(animator.IsInTransition(0) ? 1 : 0)}");
                }

                File.WriteAllText(ResultPath, sb.ToString());
                Debug.Log($"[JumpDiagnostics] Wrote {ResultPath}");
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }
    }
}
