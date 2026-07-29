using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Core;

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

            // Opening the grid counts as seeing every unlocked note
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

            // Unlocked notes, newest unlock at the top of the grid
            foreach (int noteIndex in LoveNoteManager.GetUnlockedNewestFirst())
            {
                SpawnCell(LoveNoteManager.GetNoteText(noteIndex), unlocked: true);
            }

            // Locked notes trail behind as dimmed placeholders
            int lockedCount = LoveNoteManager.TotalCount - LoveNoteManager.UnlockedCount;
            for (int i = 0; i < lockedCount; i++)
            {
                SpawnCell("?", unlocked: false);
            }
        }

        private void SpawnCell(string text, bool unlocked)
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

            spawnedCells.Add(cell);
        }
    }
}
