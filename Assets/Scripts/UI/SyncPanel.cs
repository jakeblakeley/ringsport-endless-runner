using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RingSport.Core;

namespace RingSport.UI
{
    /// <summary>
    /// Hidden cloud-sync modal, opened by triple-tapping the title art on the
    /// home screen. Shows this device's sync code (the key the progress backs
    /// up under - see SyncManager) and lets a code be typed in to pull that
    /// cloud save onto this device, behind a tap-again confirm.
    ///
    /// Deliberately not part of the visible UI: the whole hierarchy is built
    /// in code on first open (no scene object, no editor setup script) and
    /// parented to the HomeScreen canvas so it draws over everything there.
    /// Styling borrows the START button's font so it doesn't look alien.
    /// </summary>
    public class SyncPanel : MonoBehaviour
    {
        private static SyncPanel instance;

        private TextMeshProUGUI codeText;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI restoreLabel;
        private TMP_InputField input;
        private bool armed; // first RESTORE tap arms, second executes

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallTitleTapDetector()
        {
            var uiRoot = GameObject.Find("UI");
            Transform title = uiRoot != null ? uiRoot.transform.Find("HomeScreen/TitleImage") : null;
            if (title == null)
                return;

            // TitleImageSetup ships the title with raycastTarget off so it
            // can't block anything; the secret entrance needs it back on. The
            // title floats over non-interactive sky/dog, so nothing is stolen.
            var image = title.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = true;
            if (title.GetComponent<TitleTapDetector>() == null)
                title.gameObject.AddComponent<TitleTapDetector>();
        }

        public static void Show()
        {
            if (instance == null)
                instance = Build();
            if (instance == null)
                return;
            instance.transform.SetAsLastSibling();
            instance.gameObject.SetActive(true);
            instance.Refresh();
        }

        private void Refresh()
        {
            armed = false;
            codeText.text = SyncManager.Instance != null ? SyncManager.Instance.Code : "(unavailable)";
            restoreLabel.text = "RESTORE";
            statusText.text = "Progress backs up to this code automatically.\nEnter a code to load its save onto this device.";
            statusText.color = new Color(1f, 1f, 1f, 0.75f);
            input.SetTextWithoutNotify("");
        }

        private void SetStatus(string message, bool error)
        {
            statusText.text = message;
            statusText.color = error ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 1f, 1f, 0.75f);
        }

        private void OnRestoreTapped()
        {
            if (SyncManager.Instance != null && SyncManager.Instance.Restoring)
                return;

            string code = input.text.Trim().ToUpperInvariant();
            if (code.Length < 6 || !code.Contains("-"))
            {
                SetStatus("Enter a full code like WOOF-1234.", true);
                return;
            }
            if (SyncManager.Instance != null && code == SyncManager.Instance.Code)
            {
                SetStatus("That's already this device's code.", true);
                return;
            }

            if (!armed)
            {
                armed = true;
                restoreLabel.text = "TAP AGAIN";
                SetStatus("Replaces THIS device's progress with the " + code + " cloud save.", false);
                return;
            }

            armed = false;
            restoreLabel.text = "RESTORE";
            if (SyncManager.Instance == null)
            {
                SetStatus("Sync manager missing.", true);
                return;
            }
            SyncManager.Instance.BeginRestore(
                code,
                msg => SetStatus(msg, false),
                err => SetStatus(err, true));
        }

        private void OnCloseTapped()
        {
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        private static SyncPanel Build()
        {
            var uiRoot = GameObject.Find("UI");
            Transform home = uiRoot != null ? uiRoot.transform.Find("HomeScreen") : null;
            if (home == null)
                return null;

            // Borrow the game font from the START button so the panel matches.
            TMP_FontAsset font = null;
            var uiManager = UIManager.Instance;
            if (uiManager != null && uiManager.ButtonStyleTemplate != null)
            {
                var sample = uiManager.ButtonStyleTemplate.GetComponentInChildren<TMP_Text>();
                if (sample != null)
                    font = sample.font;
            }

            var overlayGo = new GameObject("SyncPanel", typeof(RectTransform));
            overlayGo.transform.SetParent(home, false);
            overlayGo.layer = LayerMask.NameToLayer("UI");
            var overlayRt = (RectTransform)overlayGo.transform;
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            var dim = overlayGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.88f); // also swallows raycasts

            var panel = overlayGo.AddComponent<SyncPanel>();

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(overlayGo.transform, false);
            var cardRt = (RectTransform)card.transform;
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(900f, 0f);
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 48, 48);
            layout.spacing = 28f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = card.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var cardBg = card.AddComponent<Image>();
            cardBg.color = new Color(0.09f, 0.07f, 0.07f, 0.97f);

            MakeText(card.transform, "Header", "CLOUD SYNC", 58f, Color.white, font, TextAlignmentOptions.Center);
            MakeText(card.transform, "CodeCaption", "THIS DEVICE'S CODE", 30f, new Color(1f, 1f, 1f, 0.55f), font, TextAlignmentOptions.Center);
            panel.codeText = MakeText(card.transform, "Code", "----", 88f, new Color(1f, 0.85f, 0.35f), font, TextAlignmentOptions.Center);

            panel.statusText = MakeText(card.transform, "Status", "", 32f, new Color(1f, 1f, 1f, 0.75f), font, TextAlignmentOptions.Center);
            panel.statusText.textWrappingMode = TextWrappingModes.Normal;

            panel.input = MakeInput(card.transform, font);
            panel.input.onValueChanged.AddListener(value =>
            {
                string upper = value.ToUpperInvariant();
                if (upper != value)
                    panel.input.SetTextWithoutNotify(upper);
                // Any edit disarms a pending confirm
                panel.armed = false;
                panel.restoreLabel.text = "RESTORE";
            });

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(card.transform, false);
            var row = buttonRow.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            var rowElement = buttonRow.AddComponent<LayoutElement>();
            rowElement.minHeight = 110f;

            MakeButton(buttonRow.transform, "Close", "CLOSE", font,
                new Color(0.22f, 0.2f, 0.2f), panel.OnCloseTapped, out _);
            MakeButton(buttonRow.transform, "Restore", "RESTORE", font,
                new Color(0.75f, 0.25f, 0.2f), panel.OnRestoreTapped, out panel.restoreLabel);

            overlayGo.SetActive(false);
            return panel;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string content,
            float size, Color color, TMP_FontAsset font, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_InputField MakeInput(Transform parent, TMP_FontAsset font)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.name = "CodeInput";
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 110f;

            var field = go.GetComponent<TMP_InputField>();
            field.characterLimit = 14;
            var text = field.textComponent as TextMeshProUGUI;
            if (text != null)
            {
                if (font != null)
                    text.font = font;
                text.fontSize = 48f;
                text.alignment = TextAlignmentOptions.Center;
            }
            var placeholder = field.placeholder as TextMeshProUGUI;
            if (placeholder != null)
            {
                if (font != null)
                    placeholder.font = font;
                placeholder.fontSize = 48f;
                placeholder.alignment = TextAlignmentOptions.Center;
                placeholder.text = "ENTER CODE";
            }
            return field;
        }

        private static void MakeButton(Transform parent, string name, string label, TMP_FontAsset font,
            Color background, UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI labelText)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            var image = go.AddComponent<Image>();
            image.color = background;
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            labelText = MakeText(go.transform, "Label", label, 40f, Color.white, font, TextAlignmentOptions.Center);
            var labelRt = (RectTransform)labelText.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Counts quick successive taps on the title art; three within half a
    /// second of each other opens the sync panel. Lives on TitleImage,
    /// installed at runtime by SyncPanel so the scene stays untouched.
    /// </summary>
    public class TitleTapDetector : MonoBehaviour, IPointerClickHandler
    {
        private const int TapsRequired = 3;
        private const float MaxGapSeconds = 0.5f;

        private float lastTapTime = -10f;
        private int tapCount;

        public void OnPointerClick(PointerEventData eventData)
        {
            float now = Time.unscaledTime;
            tapCount = now - lastTapTime <= MaxGapSeconds ? tapCount + 1 : 1;
            lastTapTime = now;
            if (tapCount >= TapsRequired)
            {
                tapCount = 0;
                SyncPanel.Show();
            }
        }
    }
}
