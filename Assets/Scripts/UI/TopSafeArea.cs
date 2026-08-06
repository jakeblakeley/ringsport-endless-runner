using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RingSport.UI
{
    /// <summary>
    /// Pushes the top row of every screen down out from under the phone's status
    /// bar / notch while the game is running fullscreen.
    ///
    /// Only fullscreen needs this. In a normal browser tab the status bar sits
    /// above the page rather than over it, so the HUD is already clear - which
    /// is why the inset is read from JavaScript (Plugins/WebGL/SafeAreaHandler.jslib)
    /// rather than baked into the layout: it has to come and go with the
    /// fullscreen state, and Unity's own Screen.safeArea is always 0 on web.
    ///
    /// The shift is applied to each element's *anchors*, not its
    /// anchoredPosition, so it composes with everything that animates position -
    /// UIManager's entrance choreography, the sprint bar's jitter - instead of
    /// fighting it. Only elements pinned to the top edge move; scrims, centred
    /// banners and bottom-anchored buttons are left alone.
    ///
    /// Self-installs on the "UI" root at startup like ScreenFader/PauseScreen -
    /// no scene wiring, and it survives a PhoneUILayoutSetup re-run.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TopSafeArea : MonoBehaviour
    {
        /// <summary>Never steal more than this much of the screen, whatever the page claims.</summary>
        private const float MaxFraction = 0.15f;

        /// <summary>Below this the shift isn't worth a layout write.</summary>
        private const float Epsilon = 0.0005f;

        private const float PollInterval = 0.25f;

        /// <summary>
        /// Editor/dev preview: forces an inset (as a fraction of screen height)
        /// so the fullscreen layout can be checked without a phone. -1 = off.
        /// </summary>
        public static float DebugFractionOverride = -1f;

        /// <summary>The inset currently applied, as a fraction of screen height.</summary>
        public static float AppliedFraction { get; private set; }

        private struct Pinned
        {
            public RectTransform Rect;
            public float BaseMinY;
            public float BaseMaxY;
        }

        private readonly List<Pinned> pinned = new List<Pinned>();
        private float applied;
        private float pollTimer;
        private int lastScreenHeight;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int RingSportSafeAreaTopBasisPoints();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var root = GameObject.Find("UI");
            if (root == null)
                return; // not the game scene

            if (root.GetComponent<TopSafeArea>() == null)
                root.AddComponent<TopSafeArea>();
        }

        private void Awake()
        {
            Collect();
        }

        /// <summary>
        /// Every direct child of a screen canvas that is pinned to the top edge.
        /// Direct children only: PhoneUILayoutSetup authors the top row at that
        /// level, and a top-anchored grandchild would otherwise be shifted twice.
        /// </summary>
        private void Collect()
        {
            pinned.Clear();

            foreach (Transform canvas in transform)
            {
                if (!(canvas is RectTransform))
                    continue;

                foreach (Transform child in canvas)
                {
                    if (!(child is RectTransform rect))
                        continue;

                    // Pinned to the top edge (zero-height anchor span at y = 1).
                    // A full-height stretch - scrims, backgrounds - is skipped so
                    // it keeps covering the whole screen.
                    if (rect.anchorMin.y < 0.999f || rect.anchorMax.y < 0.999f)
                        continue;

                    pinned.Add(new Pinned
                    {
                        Rect = rect,
                        BaseMinY = rect.anchorMin.y,
                        BaseMaxY = rect.anchorMax.y
                    });
                }
            }
        }

        private void Update()
        {
            pollTimer -= Time.unscaledDeltaTime;

            // A rotation or a fullscreen toggle both change the viewport, so
            // react to it on the frame it lands rather than up to a poll later.
            bool resized = Screen.height != lastScreenHeight;
            if (pollTimer > 0f && !resized)
                return;

            lastScreenHeight = Screen.height;
            pollTimer = PollInterval;

            float target = Mathf.Clamp(CurrentFraction(), 0f, MaxFraction);
            if (Mathf.Abs(target - applied) < Epsilon)
                return;

            Apply(target);
        }

        private static float CurrentFraction()
        {
            if (DebugFractionOverride >= 0f)
                return DebugFractionOverride;

#if UNITY_WEBGL && !UNITY_EDITOR
            int basisPoints = RingSportSafeAreaTopBasisPoints();
            return basisPoints < 0 ? 0f : basisPoints / 10000f;
#else
            // Editor and any native build: Unity already knows the cutout.
            if (Screen.height <= 0)
                return 0f;

            float inset = Screen.height - Screen.safeArea.yMax;
            return Mathf.Max(0f, inset) / Screen.height;
#endif
        }

        /// <summary>
        /// Slides the anchor of every pinned element down by <paramref name="fraction"/>
        /// of the canvas height. anchoredPosition is deliberately untouched, so
        /// anything mid-animation carries on without a pop.
        /// </summary>
        private void Apply(float fraction)
        {
            bool stale = false;

            foreach (var item in pinned)
            {
                if (item.Rect == null)
                {
                    stale = true;
                    continue;
                }

                Vector2 min = item.Rect.anchorMin;
                Vector2 max = item.Rect.anchorMax;
                item.Rect.anchorMin = new Vector2(min.x, item.BaseMinY - fraction);
                item.Rect.anchorMax = new Vector2(max.x, item.BaseMaxY - fraction);
            }

            applied = fraction;
            AppliedFraction = fraction;

            if (stale)
                pinned.RemoveAll(p => p.Rect == null);
        }
    }
}
