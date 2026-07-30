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
    /// </summary>
    public class LoveNotesPanel : MonoBehaviour
    {
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private GameObject noteCellTemplate;
        [SerializeField] private Button closeButton;
        [SerializeField] private ScrollRect scrollRect;

        private readonly List<GameObject> spawnedCells = new List<GameObject>();

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
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
                Debug.LogError("[LoveNotesPanel] Content root or cell template not assigned!");
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

            if (isNew)
                AddNewStamp(cell, label != null ? label.font : null);

            spawnedCells.Add(cell);
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
            rt.sizeDelta = new Vector2(62f, 28f);
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
            tmp.fontSize = 17f;
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
