using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RingSport.Core;

namespace RingSport.Editor
{
    /// <summary>
    /// One-time migration: aims the Start camera state at the dog so the home
    /// screen features it (idle, facing the camera) instead of leaving it small
    /// and off at the frame edge. The Start state is shared with the mini-level
    /// intro and reward podium shots, which inherit the same centered framing.
    /// Only the stale authored rotation is rewritten - a hand-tuned value is
    /// left alone. Re-runnable from Tools > RingSport > Setup Home Camera.
    /// </summary>
    public static class HomeCameraSetup
    {
        // Authored framing this migration replaces
        private static readonly Vector3 OldCameraRotation = new Vector3(15f, 0f, 0f);

        // With the rig yawed -60 and the camera at local (-2, 2, -4), this
        // points the lens at the dog's chest (world ~(0, 0.95, -1)) so the
        // turned dog stands centered in the 3/4 shot
        private static readonly Vector3 NewCameraRotation = new Vector3(16f, 18f, 0f);

        [InitializeOnLoadMethod]
        private static void AutoRun()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            // Domain reloads only happen on compile; if a play session is
            // running, keep re-queueing so the setup still lands after it ends
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            try
            {
                Run(force: false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HomeCameraSetup] Auto-run failed ({e.Message}) - re-queueing");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Home Camera")]
        private static void RunFromMenu()
        {
            Run(force: true);
        }

        private static void Run(bool force)
        {
            var stateMachine = Object.FindAnyObjectByType<CameraStateMachine>(FindObjectsInactive.Include);
            if (stateMachine == null)
                return; // scene with the camera rig isn't open

            var so = new SerializedObject(stateMachine);
            SerializedProperty rotation = so.FindProperty("startState.cameraLocalRotation");
            if (rotation == null)
            {
                Debug.LogWarning("[HomeCameraSetup] startState.cameraLocalRotation not found on CameraStateMachine");
                return;
            }

            if (Vector3.Distance(rotation.vector3Value, NewCameraRotation) < 0.01f)
                return; // already migrated

            if (!force && Vector3.Distance(rotation.vector3Value, OldCameraRotation) > 0.01f)
            {
                Debug.Log($"[HomeCameraSetup] Start camera rotation is hand-tuned ({rotation.vector3Value}) - leaving it; use Tools > RingSport > Setup Home Camera to overwrite");
                return;
            }

            Vector3 previous = rotation.vector3Value;
            rotation.vector3Value = NewCameraRotation;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorSceneManager.MarkSceneDirty(stateMachine.gameObject.scene);
                EditorSceneManager.SaveScene(stateMachine.gameObject.scene);
            }

            Debug.Log($"[HomeCameraSetup] Aimed Start camera state at the dog: rotation {previous} -> {NewCameraRotation}");
        }
    }
}
