using RingSport.Core;
using RingSport.Effects;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.UI
{
    /// <summary>
    /// Speaker toggle in the bottom-left of the home screen: taps flip
    /// AudioMuteManager's global mute (which on iOS also releases the audio
    /// session so the player's own music keeps playing - see AudioMuteManager).
    ///
    /// Built in code and self-installed at runtime like SyncPanel, so the
    /// scene stays untouched. The icons are Material Symbols Rounded
    /// volume_up / volume_off (Apache 2.0), whitened into Resources/Icons by
    /// the one-shot installer that added them; if they ever go missing the
    /// speaker is drawn procedurally instead.
    /// </summary>
    public class AudioToggleButton : MonoBehaviour
    {
        private Image icon;
        private Sprite onSprite;
        private Sprite mutedSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var uiRoot = GameObject.Find("UI");
            Transform home = uiRoot != null ? uiRoot.transform.Find("HomeScreen") : null;
            if (home == null || home.Find("AudioToggle") != null)
                return;

            var go = new GameObject("AudioToggle", typeof(RectTransform));
            go.transform.SetParent(home, false);
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(112f, 112f);
            // 48 is the home screen's corner margin; extra height clears the
            // iOS home indicator (Screen.safeArea is always 0 on web).
            rt.anchoredPosition = new Vector2(48f, 72f);

            // The notes panel is a full-screen overlay - keep drawing under it
            var notesPanel = home.Find("LoveNotesPanel");
            if (notesPanel != null)
                go.transform.SetSiblingIndex(notesPanel.GetSiblingIndex());

            var toggle = go.AddComponent<AudioToggleButton>();

            // Invisible image keeps the whole area tappable without a visible
            // background (same trick as the love notes button)
            var tapArea = go.AddComponent<Image>();
            tapArea.color = new Color(0f, 0f, 0f, 0f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = tapArea;
            button.onClick.AddListener(AudioMuteManager.Toggle);
            go.AddComponent<JuicyButton>();

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            iconGo.layer = go.layer;
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            toggle.icon = iconGo.AddComponent<Image>();
            toggle.icon.raycastTarget = false;
            toggle.icon.preserveAspect = true;

            toggle.onSprite = Resources.Load<Sprite>("Icons/round_volume_up_white");
            toggle.mutedSprite = Resources.Load<Sprite>("Icons/round_volume_off_white");
            if (toggle.onSprite == null)
                toggle.onSprite = MakeSprite(DrawSpeaker(muted: false));
            if (toggle.mutedSprite == null)
                toggle.mutedSprite = MakeSprite(DrawSpeaker(muted: true));

            toggle.Refresh(AudioMuteManager.Muted);
            AudioMuteManager.MutedChanged += toggle.Refresh;
        }

        private void OnDestroy()
        {
            AudioMuteManager.MutedChanged -= Refresh;
        }

        private void Refresh(bool muted)
        {
            icon.sprite = muted ? mutedSprite : onSprite;
        }

        // ------------------------------------------------------------------
        // Fallback icon drawing: shapes evaluated in the unit square,
        // supersampled. Only used when the Material sprites fail to load.
        // ------------------------------------------------------------------

        private const int TextureSize = 128;

        private static Sprite MakeSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private static Texture2D DrawSpeaker(bool muted)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[TextureSize * TextureSize];
            const int Sub = 3; // 3x3 supersample smooths the diagonals
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float coverage = 0f;
                    for (int sy = 0; sy < Sub; sy++)
                        for (int sx = 0; sx < Sub; sx++)
                            if (SpeakerCovers((x + (sx + 0.5f) / Sub) / TextureSize,
                                              (y + (sy + 0.5f) / Sub) / TextureSize, muted))
                                coverage += 1f;
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, coverage / (Sub * Sub));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static bool SpeakerCovers(float x, float y, bool muted)
        {
            // Speaker body + widening cone
            if (x >= 0.16f && x <= 0.33f && y >= 0.37f && y <= 0.63f)
                return true;
            float coneHalf = Mathf.Lerp(0.10f, 0.26f, Mathf.InverseLerp(0.30f, 0.55f, x));
            if (x >= 0.30f && x <= 0.55f && Mathf.Abs(y - 0.5f) <= coneHalf)
                return true;

            if (muted)
            {
                // Diagonal slash across the whole icon
                float dx = x - 0.5f, dy = y - 0.5f;
                return Mathf.Abs(dy - dx) <= 0.05f && dx * dx + dy * dy <= 0.37f * 0.37f;
            }

            // Two sound arcs in a right-facing wedge off the cone tip
            float ax = x - 0.55f, ay = y - 0.5f;
            float dist = Mathf.Sqrt(ax * ax + ay * ay);
            return ax >= Mathf.Abs(ay) * 0.7f
                && ((dist >= 0.11f && dist <= 0.17f) || (dist >= 0.24f && dist <= 0.30f));
        }
    }
}
