using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Level;
using RingSport.Player;

namespace RingSport.UI
{
    /// <summary>
    /// Tab-away pause: a 60% black scrim and a CONTINUE button, raised when the
    /// player switches tabs or the browser window loses focus, so a live run
    /// never carries on (or resumes at full pace) under someone who isn't
    /// looking at it yet.
    ///
    /// The trigger is latched in JavaScript (Plugins/WebGL/FocusEventHandler.jslib):
    /// the browser stops driving the game loop while the tab is hidden, so Unity
    /// only gets to look at the page once it is visible again - polling
    /// document.hidden from here would never see a tab switch at all.
    /// OnApplicationFocus covers the editor and any non-web build, which is also
    /// how this is tested: alt-tab out of a run in play mode.
    ///
    /// Only a LIVE run is pausable (see CanPause). The menus, the reward screens
    /// and the arena mini levels all sit at timeScale 0 and drive themselves on
    /// unscaled time, which a timeScale pause cannot freeze - and nothing is
    /// lost by tabbing away from them anyway.
    ///
    /// Built entirely in code like ScreenFader - no scene wiring.
    /// </summary>
    [DefaultExecutionOrder(-10000)] // freeze before the run's Updates see a resumed frame
    public class PauseScreen : MonoBehaviour
    {
        private const float ScrimAlpha = 0.6f;

        private static PauseScreen instance;

        /// <summary>True while the pause overlay owns the screen.</summary>
        public static bool IsPaused => instance != null && instance.isPaused;

        private GameObject overlay;
        private bool isPaused;

        // What the frozen run looked like, so Resume can hand it all back
        private GameState stateAtPause;
        private float timeScaleAtPause = 1f;
        private PlayerAnimator pausedAnimations;
        private float animatorSpeedAtPause = 1f;
        private bool audioPausedHere;

        // Set by OnApplicationFocus/OnApplicationPause (editor + non-web builds);
        // the web build reads the JavaScript latch instead
        private bool focusLost;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void RingSportWatchFocusLoss();

        [DllImport("__Internal")]
        private static extern int RingSportConsumeFocusLoss();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var go = new GameObject("PauseScreen");
            DontDestroyOnLoad(go);
            go.AddComponent<PauseScreen>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                RingSportWatchFocusLoss();
            }
            catch (System.Exception e)
            {
                GameLog.Error($"[PauseScreen] Browser focus watch unavailable: {e.Message}");
            }
#endif
        }

        private void Update()
        {
            // Consumed every frame, paused or not: a focus loss that lands while
            // the overlay is already up must not queue a second pause for later
            bool lostFocus = ConsumeFocusLoss();

            if (lostFocus && !isPaused && CanPause())
                Pause();
        }

        /// <summary>
        /// Only a running level can be paused: timeScale is the whole freeze, so
        /// this has to be a moment the game actually drives on scaled time and
        /// whose state won't change out from under the overlay.
        /// </summary>
        private static bool CanPause()
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.CurrentState != GameState.Playing)
                return false;

            // The pre-run countdown parks the clock at 0 and owns the resume
            if (Time.timeScale <= 0f)
                return false;

            // Let the death beat and the finish-line run-out play out - both are
            // short, unstoppable, unscaled sequences ending in a screen swap
            if (game.DeathSequenceRunning)
                return false;
            if (LevelManager.Instance != null && LevelManager.Instance.FinishMomentActive)
                return false;

            // Mid-transition: the state is about to change anyway
            if (ScreenFader.Instance.IsCovering)
                return false;

            return true;
        }

        private void Pause()
        {
            isPaused = true;
            stateAtPause = GameManager.Instance.CurrentState;
            timeScaleAtPause = Time.timeScale;
            Time.timeScale = 0f;

            // The dog's animator runs on unscaled time - it would keep galloping
            // under the scrim otherwise
            pausedAnimations = Object.FindAnyObjectByType<PlayerController>()?.Animations;
            if (pausedAnimations != null)
            {
                animatorSpeedAtPause = pausedAnimations.AnimatorSpeed;
                pausedAnimations.SetAnimatorPaused(true);
            }

            audioPausedHere = !AudioListener.pause;
            AudioListener.pause = true;

            if (overlay == null)
                Build();
            overlay.SetActive(true);

            GameLog.Info("[PauseScreen] Paused - browser focus lost during a run");
        }

        /// <summary>The CONTINUE button; also safe to call if the run moved on underneath.</summary>
        public void Resume()
        {
            if (!isPaused)
                return;

            isPaused = false;
            if (overlay != null)
                overlay.SetActive(false);

            // Coroutines on unscaled time keep running behind the scrim and can
            // move the game on (a level-complete swap, say). Only hand back the
            // clock and the animator while this is still the run we froze.
            bool sameState = GameManager.Instance != null && GameManager.Instance.CurrentState == stateAtPause;

            if (sameState && Mathf.Approximately(Time.timeScale, 0f))
                Time.timeScale = timeScaleAtPause;

            if (pausedAnimations != null)
                pausedAnimations.SetAnimatorTimeScale(sameState ? animatorSpeedAtPause : 1f);
            pausedAnimations = null;

            if (audioPausedHere)
                AudioListener.pause = false;
            audioPausedHere = false;

            GameLog.Info("[PauseScreen] Resumed");
        }

        private bool ConsumeFocusLoss()
        {
            bool lost = focusLost;
            focusLost = false;

#if UNITY_WEBGL && !UNITY_EDITOR
            lost |= RingSportConsumeFocusLoss() != 0;
#endif
            return lost;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                focusLost = true;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                focusLost = true;
        }

        // ------------------------------------------------------------------
        // Overlay (built in code - no scene wiring required)
        // ------------------------------------------------------------------

        private void Build()
        {
            overlay = new GameObject("PauseCanvas");
            overlay.transform.SetParent(transform, false);

            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 960; // above the ScreenFader (950), itself above every scene canvas

            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            overlay.AddComponent<GraphicRaycaster>();

            // The scrim also swallows taps aimed past the button, so a swipe on
            // the frozen run can't reach the HUD underneath
            var scrimGO = new GameObject("Scrim");
            scrimGO.transform.SetParent(overlay.transform, false);
            var scrim = scrimGO.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, ScrimAlpha);
            RectTransform scrimRect = scrim.rectTransform;
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = Vector2.zero;
            scrimRect.offsetMax = Vector2.zero;

            BuildContinueButton();
        }

        /// <summary>
        /// Wears the home screen's START button look (pill sprite, label font)
        /// so the overlay matches the game without any wiring of its own - the
        /// pill and the font live in the scene, not somewhere loadable.
        /// </summary>
        private void BuildContinueButton()
        {
            Button template = UIManager.Instance != null ? UIManager.Instance.ButtonStyleTemplate : null;
            var templateImage = template != null ? template.targetGraphic as Image : null;
            var templateLabel = template != null ? template.GetComponentInChildren<TextMeshProUGUI>(true) : null;

            var buttonGO = new GameObject("ContinueButton");
            buttonGO.transform.SetParent(overlay.transform, false);

            var background = buttonGO.AddComponent<Image>();
            if (templateImage != null)
            {
                background.sprite = templateImage.sprite;
                background.type = templateImage.type;
                background.color = templateImage.color;
            }
            else
            {
                background.color = Color.white;
            }

            RectTransform rect = background.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(440f, 140f);
            rect.anchoredPosition = Vector2.zero;

            // Keeps the pill's ends capped at half THIS button's height - the
            // scene buttons' baked multiplier belongs to their own rects
            if (background.sprite != null && background.type == Image.Type.Sliced)
                buttonGO.AddComponent<PillImage>();

            var button = buttonGO.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(Resume);
            buttonGO.AddComponent<JuicyButton>(); // press scale, same as the scene buttons

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(buttonGO.transform, false);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = "CONTINUE";
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSize = templateLabel.fontSize;
                label.fontStyle = templateLabel.fontStyle;
                label.characterSpacing = templateLabel.characterSpacing;
                label.color = templateLabel.color;
            }
            else
            {
                label.fontSize = 54f;
                label.color = new Color(0.13f, 0.14f, 0.16f);
            }

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }
    }
}
