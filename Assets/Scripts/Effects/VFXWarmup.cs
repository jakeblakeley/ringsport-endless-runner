using System.Collections;
using System.Collections.Generic;
using RingSport.Core;
using UnityEngine;

namespace RingSport.Effects
{
    /// <summary>
    /// Renders one near-invisible particle from every shared emitter while the
    /// player is still on the home screen.
    ///
    /// Everything here is already pooled or shared - what stutters is each
    /// system's FIRST visible draw: the GPU buffer allocation plus the shader
    /// variant compile happen on first render, not on Instantiate. On web that
    /// lands as a hitch the first time dust kicks, a coin bursts, or the sprint
    /// trail lights up. One sub-pixel particle per system moves all of that to
    /// the home screen, where a hitch is invisible.
    ///
    /// Same trick as MiniLevelFleeAttack.WarmUpDecoyAssets, which draws the
    /// decoy at 2% scale in front of the camera - proven unnoticeable there.
    /// The particle must actually be VISIBLE to the camera: an off-screen or
    /// inactive system is culled and compiles nothing.
    /// </summary>
    public class VFXWarmup : MonoBehaviour
    {
        private static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (installed)
                return;
            installed = true;

            var go = new GameObject("VFXWarmup");
            DontDestroyOnLoad(go);
            go.AddComponent<VFXWarmup>();
        }

        private IEnumerator Start()
        {
            // Same settle the perf probe uses: wait for the game to reach Home,
            // then a beat for the camera rig and singletons to be in place.
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Home)
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            yield return null;

            Camera cam = Camera.main;
            if (cam == null)
            {
                Destroy(gameObject);
                yield break;
            }

            // Just inside the near plane's comfortable zone, slightly below
            // centre so it overlaps ground rather than sky.
            Vector3 warmPos = cam.transform.position + cam.transform.forward * 1.5f + Vector3.down * 0.3f;

            var systems = new List<ParticleSystem>();
            CollectFrom(ImpactVFX.Instance, systems);          // dust + confetti
            CollectFrom(CollectBurstVFX.Instance, systems);    // sparks + flash

            // The player's own emitters (sprint trail) - otherwise the first
            // sprint of a session pays the compile.
            var player = FindAnyObjectByType<RingSport.Player.PlayerController>();
            CollectFrom(player, systems);

            var emit = new ParticleSystem.EmitParams
            {
                position = warmPos,
                velocity = Vector3.zero,
                startSize = 0.01f,
                startLifetime = 0.15f,
                startColor = new Color(1f, 1f, 1f, 0.05f),
            };

            int warmed = 0;
            foreach (ParticleSystem ps in systems)
            {
                if (ps == null)
                    continue;
                ps.Emit(emit, 1);
                warmed++;
            }

            // Two frames on screen: one to build buffers and compile, one to draw.
            yield return null;
            yield return null;

            GameLog.Info($"[VFXWarmup] Warmed {warmed} particle system(s) on the home screen.");
            Destroy(gameObject);
        }

        private static void CollectFrom(Component root, List<ParticleSystem> into)
        {
            if (root == null)
                return;
            root.GetComponentsInChildren(true, sharedBuffer);
            into.AddRange(sharedBuffer);
        }

        private static readonly List<ParticleSystem> sharedBuffer = new List<ParticleSystem>();
    }
}
