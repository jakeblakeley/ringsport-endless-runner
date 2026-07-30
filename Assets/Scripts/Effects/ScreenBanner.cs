using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Effects
{
    /// <summary>
    /// Shared full-screen banner ("FINISH!") for moments outside the chase
    /// mini-levels, which each carry their own copy of this pop-and-fade
    /// banner. Same look and timing as theirs: 0.14s alpha+scale pop, hold,
    /// 0.4s fade, runtime-built canvas, unscaled time.
    /// </summary>
    public class ScreenBanner : MonoBehaviour
    {
        private static ScreenBanner instance;

        private Canvas bannerCanvas;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerText;
        private Coroutine bannerRoutine;

        private static ScreenBanner Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("ScreenBanner");
                    instance = go.AddComponent<ScreenBanner>();
                }
                return instance;
            }
        }

        public static void Show(string message, Color color, float holdSeconds, float fontSize = 110f, TMP_FontAsset font = null)
        {
            Instance.ShowInternal(message, color, holdSeconds, fontSize, font);
        }

        private void ShowInternal(string message, Color color, float holdSeconds, float fontSize, TMP_FontAsset font)
        {
            EnsureCanvas();

            if (font != null)
            {
                bannerText.font = font;
                bannerText.fontStyle = FontStyles.Italic;
            }

            if (bannerRoutine != null)
                StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine(message, color, holdSeconds, fontSize));
        }

        private IEnumerator BannerRoutine(string message, Color color, float holdSeconds, float fontSize)
        {
            bannerText.text = message;
            bannerText.color = color;
            bannerText.fontSize = fontSize;

            var rt = bannerText.rectTransform;
            float t = 0f;
            const float popDuration = 0.14f;
            while (t < popDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / popDuration);
                bannerGroup.alpha = k;
                rt.localScale = Vector3.one * Mathf.Lerp(1.45f, 1f, k);
                yield return null;
            }
            bannerGroup.alpha = 1f;
            rt.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(holdSeconds);

            t = 0f;
            const float fadeDuration = 0.4f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                bannerGroup.alpha = 1f - Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            bannerGroup.alpha = 0f;
            bannerRoutine = null;
        }

        private void EnsureCanvas()
        {
            if (bannerCanvas != null)
                return;

            var canvasGO = new GameObject("SharedBannerCanvas");
            canvasGO.transform.SetParent(transform, false);
            bannerCanvas = canvasGO.AddComponent<Canvas>();
            bannerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            bannerCanvas.sortingOrder = 404; // alongside the chase banner canvases (400-402)

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            bannerGroup = canvasGO.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;

            var textGO = new GameObject("BannerText");
            textGO.transform.SetParent(canvasGO.transform, false);
            bannerText = textGO.AddComponent<TextMeshProUGUI>();
            bannerText.alignment = TextAlignmentOptions.Center;
            bannerText.fontSize = 110f;
            bannerText.raycastTarget = false;
            bannerText.fontStyle = FontStyles.Bold | FontStyles.Italic; // replaced when a font is passed

            var rt = bannerText.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.74f);
            rt.sizeDelta = new Vector2(1000f, 360f);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
