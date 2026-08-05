// Automated perf sampler. Editor + development builds only (mirrors DebugMenu).
//
// Trigger: create PerfReports/pending_request.json ({"label":"x","durationSeconds":60})
// and enter play mode (the editor-side PerfRunner does both when the marker
// appears). The probe waits for the Home screen, starts a run with a fixed
// random seed and PerfFlags.Invincible on, samples every frame of the level-1
// run section (state Playing, timeScale > 0), then writes
// PerfReports/report_<label>.json and exits play mode.
//
// Metrics: frame-time distribution (avg/p50/p95/p99/max, % over 60 and 30 fps
// budgets), per-frame managed allocations, GC collection count, draw calls /
// SetPass / batches / triangles averages, reserved memory at end.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using RingSport.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RingSport.DebugTools
{
    public class PerfProbe : MonoBehaviour
    {
        [Serializable]
        private class Request
        {
            public string label = "run";
            public float durationSeconds = 60f;
        }

        [Serializable]
        private class Report
        {
            public string label;
            public string error = "";
            public string unityVersion;
            public string timestamp;
            public int frames;
            public float sampledSeconds;
            public float avgMs;
            public float p50Ms;
            public float p95Ms;
            public float p99Ms;
            public float maxMs;
            public float pctOver16_7ms;
            public float pctOver33_3ms;
            public float gcAllocPerFrameAvgKB;
            public float gcAllocTotalMB;
            public float gcAllocWorstFrameKB;
            public int gcCollections;
            public float drawCallsAvg;
            public long drawCallsMax;
            public float setPassAvg;
            public long setPassMax;
            public float batchesAvg;
            public float trianglesAvgK;
            public float gcReservedEndMB;
            public float systemMemoryEndMB;
            public int levelSampled;
        }

        private const string MarkerPath = "PerfReports/pending_request.json";
        private const int RandomSeed = 20260804;

        private static PerfProbe instance;

        /// <summary>True once a probe exists in the current play session.</summary>
        public static bool IsActive => instance != null;

        /// <summary>
        /// Spawn the probe if a request marker exists. Called from
        /// RuntimeInitializeOnLoadMethod and, as a belt-and-braces fallback,
        /// from the editor-side PerfRunner on play-mode entry.
        /// </summary>
        public static void EnsureSpawned()
        {
            if (instance != null || !Application.isPlaying)
                return;
            try
            {
                if (!File.Exists(MarkerPath))
                    return;
            }
            catch (Exception)
            {
                return; // no filesystem (web player) - probe stays inert
            }

            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PerfProbe>();
            Debug.Log("[PerfProbe] Probe spawned");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureSpawned();
        }

        private Request request;
        private bool sampling;
        private readonly List<float> frameMs = new List<float>(30000);
        private long gcAllocTotal;
        private long gcAllocMax;
        private double drawCallsSum, setPassSum, batchesSum, trisSum;
        private long drawCallsMax, setPassMax;
        private int renderSamples;
        private int gcCollectionsStart;

        private ProfilerRecorder gcAllocRec, gcReservedRec, sysMemRec, drawCallsRec, setPassRec, batchesRec, trisRec;

        private void Start()
        {
            try
            {
                request = JsonUtility.FromJson<Request>(File.ReadAllText(MarkerPath));
            }
            catch (Exception)
            {
                request = null;
            }
            if (request == null)
                request = new Request();
            if (request.durationSeconds <= 0f)
                request.durationSeconds = 60f;

            StartCoroutine(RunSequence());
        }

        private void Update()
        {
            if (!sampling)
                return;
            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Playing || Time.timeScale <= 0f)
                return;

            frameMs.Add(Time.unscaledDeltaTime * 1000f);

            if (gcAllocRec.Valid)
            {
                long v = gcAllocRec.LastValue;
                gcAllocTotal += v;
                if (v > gcAllocMax) gcAllocMax = v;
            }
#if UNITY_EDITOR
            // In the editor the ProfilerRecorder render counters only tick with
            // the Profiler window active; UnityStats is always live.
            long dc = UnityEditor.UnityStats.drawCalls;
            drawCallsSum += dc;
            if (dc > drawCallsMax) drawCallsMax = dc;
            long sp = UnityEditor.UnityStats.setPassCalls;
            setPassSum += sp;
            if (sp > setPassMax) setPassMax = sp;
            batchesSum += dc; // Unity 6 dropped UnityStats.batches; drawCalls is the comparable
            trisSum += UnityEditor.UnityStats.triangles;
            renderSamples++;
#else
            if (drawCallsRec.Valid)
            {
                long v = drawCallsRec.LastValue;
                drawCallsSum += v;
                if (v > drawCallsMax) drawCallsMax = v;
                renderSamples++;
            }
            if (setPassRec.Valid)
            {
                long v = setPassRec.LastValue;
                setPassSum += v;
                if (v > setPassMax) setPassMax = v;
            }
            if (batchesRec.Valid) batchesSum += batchesRec.LastValue;
            if (trisRec.Valid) trisSum += trisRec.LastValue;
#endif
        }

        private IEnumerator RunSequence()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((GameManager.Instance == null || LevelManager.Instance == null) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (GameManager.Instance == null || LevelManager.Instance == null)
            {
                Finish("GameManager/LevelManager never appeared - wrong scene open?");
                yield break;
            }

            while (GameManager.Instance.CurrentState != GameState.Home && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (GameManager.Instance.CurrentState != GameState.Home)
            {
                Finish("never reached Home state");
                yield break;
            }

            // Let the home screen, pools and start scene settle
            yield return new WaitForSecondsRealtime(1.5f);

            UnityEngine.Random.InitState(RandomSeed);
            PerfFlags.Invincible = true;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            Debug.Log($"[PerfProbe] Starting sampled run '{request.label}' ({request.durationSeconds:F0}s max)");
            GameManager.Instance.StartGame();

            deadline = Time.realtimeSinceStartup + 25f;
            while ((GameManager.Instance.CurrentState != GameState.Playing || Time.timeScale <= 0f) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (GameManager.Instance.CurrentState != GameState.Playing)
            {
                Finish("never entered Playing state");
                yield break;
            }

            StartRecorders();
            gcCollectionsStart = GC.CollectionCount(0);
            sampling = true;

            // Fixed-time screenshot: with the deterministic seed, runs capture
            // ~the same world state, so before/after shots are comparable
            StartCoroutine(CaptureShot(8f));

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < request.durationSeconds)
            {
                // Level-1 run section only: stop when the run hands off to the
                // mini level / level complete flow (or an unexpected game over).
                if (GameManager.Instance.CurrentState != GameState.Playing && sampling && frameMs.Count > 60)
                    break;
                yield return null;
            }

            sampling = false;
            Finish(null);
        }

        private IEnumerator CaptureShot(float afterSeconds)
        {
            yield return new WaitForSecondsRealtime(afterSeconds);
            if (!sampling)
                yield break;
            try
            {
                ScreenCapture.CaptureScreenshot($"PerfReports/shot_{request.label}.png");
                Debug.Log($"[PerfProbe] Screenshot queued: PerfReports/shot_{request.label}.png");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PerfProbe] Screenshot failed: {e.Message}");
            }
        }

        private void StartRecorders()
        {
            gcAllocRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcReservedRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");
            sysMemRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            drawCallsRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            batchesRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            trisRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void DisposeRecorders()
        {
            gcAllocRec.Dispose();
            gcReservedRec.Dispose();
            sysMemRec.Dispose();
            drawCallsRec.Dispose();
            setPassRec.Dispose();
            batchesRec.Dispose();
            trisRec.Dispose();
        }

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0)
                return 0f;
            int idx = Mathf.Clamp(Mathf.CeilToInt(p / 100f * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[idx];
        }

        private void Finish(string error)
        {
            sampling = false;

            var report = new Report
            {
                label = request != null ? request.label : "unknown",
                error = error ?? "",
                unityVersion = Application.unityVersion,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                frames = frameMs.Count,
                levelSampled = LevelManager.Instance != null ? LevelManager.Instance.CurrentLevel : 0,
            };

            if (frameMs.Count > 0)
            {
                var sorted = new List<float>(frameMs);
                sorted.Sort();

                float sum = 0f;
                int over60 = 0, over30 = 0;
                for (int i = 0; i < frameMs.Count; i++)
                {
                    float ms = frameMs[i];
                    sum += ms;
                    if (ms > 16.7f) over60++;
                    if (ms > 33.4f) over30++;
                }

                report.sampledSeconds = sum / 1000f;
                report.avgMs = sum / frameMs.Count;
                report.p50Ms = Percentile(sorted, 50f);
                report.p95Ms = Percentile(sorted, 95f);
                report.p99Ms = Percentile(sorted, 99f);
                report.maxMs = sorted[sorted.Count - 1];
                report.pctOver16_7ms = 100f * over60 / frameMs.Count;
                report.pctOver33_3ms = 100f * over30 / frameMs.Count;
                report.gcAllocPerFrameAvgKB = gcAllocTotal / 1024f / frameMs.Count;
                report.gcAllocTotalMB = gcAllocTotal / (1024f * 1024f);
                report.gcAllocWorstFrameKB = gcAllocMax / 1024f;
                report.gcCollections = GC.CollectionCount(0) - gcCollectionsStart;
            }

            if (renderSamples > 0)
            {
                report.drawCallsAvg = (float)(drawCallsSum / renderSamples);
                report.drawCallsMax = drawCallsMax;
                report.setPassAvg = (float)(setPassSum / renderSamples);
                report.setPassMax = setPassMax;
                report.batchesAvg = (float)(batchesSum / renderSamples);
                report.trianglesAvgK = (float)(trisSum / renderSamples / 1000.0);
            }

            report.gcReservedEndMB = gcReservedRec.Valid ? gcReservedRec.LastValue / (1024f * 1024f) : 0f;
            report.systemMemoryEndMB = sysMemRec.Valid ? sysMemRec.LastValue / (1024f * 1024f) : 0f;

            DisposeRecorders();
            PerfFlags.Invincible = false;

            string json = JsonUtility.ToJson(report, true);
            Debug.Log($"[PerfProbe] Report ({report.label}): {json}");

            try
            {
                Directory.CreateDirectory("PerfReports");
                File.WriteAllText($"PerfReports/report_{report.label}.json", json);
                if (File.Exists(MarkerPath))
                    File.Delete(MarkerPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PerfProbe] Could not write report file: {e.Message}");
            }

#if UNITY_EDITOR
            EditorApplication.ExitPlaymode();
#endif
        }
    }
}
#endif
