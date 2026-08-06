using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Effects
{
    /// <summary>
    /// Runtime-built full-screen overlay providing (1) fade-to-black screen
    /// transitions - every screen swap in the game is an instant SetActive, and
    /// FadeSwap hides that hard cut plus the world resets behind it - and
    /// (2) brief color flashes (the red death flash). Built entirely in code
    /// like the mini-level banner canvases; unscaled time throughout because
    /// most swaps happen while timeScale is 0.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        private static ScreenFader instance;

        public static ScreenFader Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("ScreenFader");
                    instance = go.AddComponent<ScreenFader>();
                    instance.Build();
                }
                return instance;
            }
        }

        private Image flashImage;
        private Image fadeImage;
        private Coroutine fadeRoutine;
        private Coroutine flashRoutine;
        private bool covering; // true from fade-out start until fade-in completes

        /// <summary>True while a transition owns the screen (fade-out through fade-in).</summary>
        public bool IsCovering => covering;

        /// <summary>
        /// Fade to black, run the screen/world swap while covered, optionally
        /// hold on black while the swapped-in world finishes building, then fade
        /// back in. If a fade is already covering the screen, the swap just runs
        /// immediately under it (nested state changes during a transition).
        /// </summary>
        public void FadeSwap(Action atBlack, float outDuration = 0.3f, float inDuration = 0.45f, float holdDuration = 0f)
        {
            if (covering)
            {
                atBlack?.Invoke();
                return;
            }

            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeSwapRoutine(atBlack, outDuration, inDuration, holdDuration));
        }

        /// <summary>Impact flash (death hit): quick tint in, eased fade out.</summary>
        public void Flash(Color color, float peakAlpha = 0.35f, float inDuration = 0.05f, float outDuration = 0.3f)
        {
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine(color, peakAlpha, inDuration, outDuration));
        }

        private IEnumerator FadeSwapRoutine(Action atBlack, float outDuration, float inDuration, float holdDuration)
        {
            covering = true;
            fadeImage.raycastTarget = true; // swallow taps mid-transition

            yield return AnimateFadeAlpha(0f, 1f, outDuration, Juice.InQuad);

            atBlack?.Invoke();
            // Give the swapped-in screen a frame to lay out before revealing
            yield return null;

            // Stay black a beat longer for heavy swaps (level generation): the
            // build hitch and any first-frame pop happen behind the curtain
            if (holdDuration > 0f)
                yield return new WaitForSecondsRealtime(holdDuration);

            yield return AnimateFadeAlpha(1f, 0f, inDuration, Juice.OutQuad);

            fadeImage.raycastTarget = false;
            covering = false;
            fadeImage.enabled = false; // fully faded in - stop submitting the quad
            fadeRoutine = null;
        }

        private IEnumerator AnimateFadeAlpha(float from, float to, float duration, Func<float, float> ease)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = ease(Mathf.Clamp01(elapsed / duration));
                SetFadeAlpha(Mathf.Lerp(from, to, k));
                yield return null;
            }
            SetFadeAlpha(to);
        }

        private IEnumerator FlashRoutine(Color color, float peakAlpha, float inDuration, float outDuration)
        {
            float elapsed = 0f;
            while (elapsed < inDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Juice.OutQuad(Mathf.Clamp01(elapsed / inDuration));
                SetFlash(color, peakAlpha * k);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < outDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Juice.OutQuad(Mathf.Clamp01(elapsed / outDuration));
                SetFlash(color, peakAlpha * (1f - k));
                yield return null;
            }

            SetFlash(color, 0f);
            flashRoutine = null;
        }

        private void SetFadeAlpha(float alpha)
        {
            var c = fadeImage.color;
            c.a = alpha;
            fadeImage.color = c;
            // An alpha-0 UGUI Graphic is still drawn as a full-screen blended
            // quad; keep it enabled while covering so it swallows taps
            fadeImage.enabled = alpha > 0.0005f || covering;
        }

        private void SetFlash(Color color, float alpha)
        {
            color.a = alpha;
            flashImage.color = color;
            flashImage.enabled = alpha > 0.0005f;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950; // above every scene canvas (secret note 10, banners ~400)

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            flashImage = BuildLayer("Flash");
            fadeImage = BuildLayer("Fade"); // last sibling: black covers the flash
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
        }

        private Image BuildLayer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            image.enabled = false; // enabled on demand while a fade/flash is visible
            var rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return image;
        }
    }
}
