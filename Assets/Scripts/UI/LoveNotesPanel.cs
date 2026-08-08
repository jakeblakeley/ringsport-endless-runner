using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Core;
using RingSport.Effects;

namespace RingSport.UI
{
    /// <summary>
    /// Full-screen overlay on the home screen showing every love note in a
    /// 2-column grid: unlocked notes first (newest unlock at the top), then
    /// dimmed "?" placeholders for notes still waiting to be found in-game.
    /// Opening the panel marks all notes as seen (clears the NEW badge).
    /// Tapping an unlocked note expands it to a centered full-screen view
    /// over a darkened backdrop, with its own X (the grid's X hides while
    /// a note is focused).
    /// </summary>
    public class LoveNotesPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private GameObject noteCellTemplate;
        [SerializeField] private Button closeButton;
        [SerializeField] private ScrollRect scrollRect;

        // Focused-note animation: the backdrop fades ahead of the note
        // expanding so the grid recedes before the note lands.
        private const float BackdropAlpha = 0.8f;
        private const float BackdropFadeDuration = 0.1f;
        private const float ExpandDuration = 0.28f;
        private const float CollapseDuration = 0.16f;

        private readonly List<GameObject> spawnedCells = new List<GameObject>();

        private GameObject focusedOverlay;
        private Coroutine focusRoutine;
        private Vector2 focusReturnPosition;
        private float focusReturnScale;

        private void Awake()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);

                // The scroll view is authored after the header row, so its
                // invisible drag catcher raycasts on top of the close button
                // wherever the two ever overlap. Last sibling wins for input, so
                // the X always takes the tap.
                closeButton.transform.SetAsLastSibling();
            }
        }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // render above the rest of the home screen
            Rebuild();

            // Opening the grid counts as seeing every unlocked note. This must
            // stay AFTER Rebuild - the cells' NEW stamps read the seen state.
            LoveNoteManager.MarkAllSeen();
            UIManager.Instance?.RefreshHomeLoveNotes();

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f; // start at the top
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            // Closing the panel (or leaving the home screen) while a note is
            // focused: drop the overlay instantly and restore the grid's X.
            if (focusRoutine != null)
            {
                StopCoroutine(focusRoutine);
                focusRoutine = null;
            }
            if (focusedOverlay != null)
            {
                Destroy(focusedOverlay);
                focusedOverlay = null;
            }
            if (closeButton != null)
                closeButton.gameObject.SetActive(true);
        }

        private void Rebuild()
        {
            foreach (GameObject cell in spawnedCells)
            {
                if (cell != null)
                    Destroy(cell);
            }
            spawnedCells.Clear();

            if (contentRoot == null || noteCellTemplate == null)
            {
                GameLog.Error("[LoveNotesPanel] Content root or cell template not assigned!");
                return;
            }

            // Unlocked notes, newest unlock at the top of the grid; notes
            // unlocked since the last visit get a NEW stamp
            foreach (int noteIndex in LoveNoteManager.GetUnlockedNewestFirst())
            {
                SpawnCell(LoveNoteManager.GetNoteText(noteIndex), unlocked: true,
                    isNew: LoveNoteManager.IsNoteUnseen(noteIndex));
            }

            // Locked notes trail behind as dimmed placeholders
            int lockedCount = LoveNoteManager.TotalCount - LoveNoteManager.UnlockedCount;
            for (int i = 0; i < lockedCount; i++)
            {
                SpawnCell("?", unlocked: false, isNew: false);
            }
        }

        private void SpawnCell(string text, bool unlocked, bool isNew)
        {
            GameObject cell = Instantiate(noteCellTemplate, contentRoot);
            cell.SetActive(true);

            var label = cell.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.text = text;

            if (!unlocked)
            {
                var image = cell.GetComponent<Image>();
                if (image != null)
                    image.color = new Color(0.35f, 0.35f, 0.35f, 0.6f);
            }
            else
            {
                // Tapping an unlocked note expands it to the focused view.
                // Button has no drag handling, so scroll drags that start on a
                // cell still bubble up to the ScrollRect.
                var button = cell.AddComponent<Button>();
                button.targetGraphic = cell.GetComponent<Image>();
                var cellRect = (RectTransform)cell.transform;
                string noteText = text;
                button.onClick.AddListener(() => FocusNote(cellRect, noteText));
            }

            if (isNew)
                AddNewStamp(cell, label != null ? label.font : null);

            spawnedCells.Add(cell);
        }

        // ------------------------------------------------------------------
        // Focused note view
        // ------------------------------------------------------------------

        /// <summary>
        /// Expands the tapped note into a full-screen focused view: a fresh
        /// clone of the pristine cell template flies from the tapped cell to
        /// the panel centre while a backdrop fades in underneath it.
        /// </summary>
        private void FocusNote(RectTransform sourceCell, string text)
        {
            if (focusedOverlay != null)
                return;

            var overlayObject = new GameObject("FocusedNote", typeof(RectTransform));
            overlayObject.transform.SetParent(transform, false);
            overlayObject.transform.SetAsLastSibling();
            var overlayRect = (RectTransform)overlayObject.transform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            // Backdrop also swallows raycasts so the grid underneath is inert
            var backdropObject = new GameObject("Backdrop", typeof(RectTransform));
            backdropObject.transform.SetParent(overlayRect, false);
            var backdropRect = (RectTransform)backdropObject.transform;
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            var backdrop = backdropObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0f);

            // The template is pristine (no dim tint, no NEW stamp), so a clone
            // of it is the clean blown-up note.
            GameObject note = Instantiate(noteCellTemplate, overlayRect);
            note.SetActive(true);
            var noteRect = (RectTransform)note.transform;
            noteRect.anchorMin = noteRect.anchorMax = new Vector2(0.5f, 0.5f);
            noteRect.pivot = new Vector2(0.5f, 0.5f);
            float targetSize = Mathf.Min(overlayRect.rect.width, overlayRect.rect.height) - 96f;
            noteRect.sizeDelta = new Vector2(targetSize, targetSize);

            var label = note.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = text;
                // Keep the handwriting margins and size proportional to the
                // bigger sheet (cell: 112 inset / 52 max on a 452 square)
                var labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(-targetSize * 0.25f, -targetSize * 0.25f);
                label.fontSizeMin = 36f;
                label.fontSizeMax = 104f;
            }

            // Start exactly over the tapped cell, at its on-screen size
            Vector3 cellWorldCenter = sourceCell.TransformPoint(sourceCell.rect.center);
            focusReturnPosition = overlayRect.InverseTransformPoint(cellWorldCenter);
            focusReturnScale = sourceCell.rect.width * sourceCell.lossyScale.x
                / (targetSize * overlayRect.lossyScale.x);
            noteRect.anchoredPosition = focusReturnPosition;
            noteRect.localScale = Vector3.one * focusReturnScale;

            CanvasGroup closeGroup = BuildFocusCloseButton(overlayRect,
                label != null ? label.font : null, noteRect, backdrop);

            // Swap the grid's X out while the focused view owns closing
            if (closeButton != null)
                closeButton.gameObject.SetActive(false);

            focusedOverlay = overlayObject;
            focusRoutine = StartCoroutine(FocusInRoutine(backdrop, closeGroup, noteRect));
        }

        /// <summary>
        /// Marker-drawn X inked into the note's top-right corner. Parented to
        /// the note so it rides the expand/collapse animation with the card;
        /// fades in with the backdrop. Returns its CanvasGroup.
        /// </summary>
        private CanvasGroup BuildFocusCloseButton(RectTransform overlayRect, TMP_FontAsset font,
            RectTransform noteRect, Image backdrop)
        {
            var closeObject = new GameObject("CloseButton", typeof(RectTransform));
            closeObject.transform.SetParent(noteRect, false);
            var closeRect = (RectTransform)closeObject.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(96f, 96f);
            closeRect.anchoredPosition = new Vector2(-48f, -48f);

            var tapArea = closeObject.AddComponent<Image>();
            tapArea.color = new Color(0f, 0f, 0f, 0f);
            var button = closeObject.AddComponent<Button>();
            button.targetGraphic = tapArea;
            var group = closeObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(closeRect, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var tmp = textObject.AddComponent<TextMeshProUGUI>();
            tmp.text = "X";
            tmp.fontSize = 72f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.black;
            tmp.raycastTarget = false;
            if (font != null)
                tmp.font = font;

            button.onClick.AddListener(() =>
            {
                button.interactable = false;
                if (focusRoutine != null)
                    StopCoroutine(focusRoutine);
                focusRoutine = StartCoroutine(FocusOutRoutine(backdrop, group, noteRect));
            });

            return group;
        }

        private IEnumerator FocusInRoutine(Image backdrop, CanvasGroup closeGroup, RectTransform noteRect)
        {
            Vector2 startPosition = noteRect.anchoredPosition;
            float startScale = noteRect.localScale.x;

            float elapsed = 0f;
            while (elapsed < ExpandDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                float fade = Juice.OutQuad(Mathf.Clamp01(elapsed / BackdropFadeDuration));
                backdrop.color = new Color(0f, 0f, 0f, BackdropAlpha * fade);
                closeGroup.alpha = fade;

                // Unclamped lerp: OutBack overshoots past 1 for the settle
                float k = Juice.OutBack(Mathf.Clamp01(elapsed / ExpandDuration));
                noteRect.anchoredPosition = Vector2.LerpUnclamped(startPosition, Vector2.zero, k);
                noteRect.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, 1f, k);
                yield return null;
            }

            noteRect.anchoredPosition = Vector2.zero;
            noteRect.localScale = Vector3.one;
            focusRoutine = null;
        }

        private IEnumerator FocusOutRoutine(Image backdrop, CanvasGroup closeGroup, RectTransform noteRect)
        {
            Vector2 startPosition = noteRect.anchoredPosition;
            float startScale = noteRect.localScale.x;
            float startBackdropAlpha = backdrop.color.a;
            float startCloseAlpha = closeGroup.alpha;

            float elapsed = 0f;
            while (elapsed < CollapseDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(elapsed / CollapseDuration);
                float k = Juice.OutQuad(n);

                backdrop.color = new Color(0f, 0f, 0f, startBackdropAlpha * (1f - k));
                closeGroup.alpha = startCloseAlpha * (1f - k);
                noteRect.anchoredPosition = Vector2.LerpUnclamped(startPosition, focusReturnPosition, k);
                noteRect.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, focusReturnScale, k);
                yield return null;
            }

            Destroy(focusedOverlay);
            focusedOverlay = null;
            focusRoutine = null;
            if (closeButton != null)
                closeButton.gameObject.SetActive(true);
        }

        /// <summary>
        /// Small runtime-built NEW stamp tucked into the cell's top-right
        /// corner, with a bounce-in pop. Built in code like the banner
        /// canvases - no template changes needed.
        /// </summary>
        private static void AddNewStamp(GameObject cell, TMP_FontAsset font)
        {
            var badgeGO = new GameObject("NewStamp", typeof(RectTransform));
            badgeGO.transform.SetParent(cell.transform, false);

            var rt = (RectTransform)badgeGO.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(124f, 56f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 6f); // slight stamp tilt

            var background = badgeGO.AddComponent<Image>();
            background.color = new Color(0.91f, 0.30f, 0.24f);
            background.raycastTarget = false;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(badgeGO.transform, false);
            var textRect = (RectTransform)textGO.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "NEW";
            tmp.fontSize = 34f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            if (font != null)
                tmp.font = font;

            Juice.PunchScale(badgeGO.transform, 0.35f, 0.3f);
        }
    }
}
