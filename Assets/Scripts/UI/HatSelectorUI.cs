using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Player;

namespace RingSport.UI
{
    /// <summary>
    /// Home-screen hat carousel above the START button, built by
    /// Tools > RingSport > Setup Hats. The centre box shows the browsed hat's
    /// baked 3D thumbnail; the part-visible side boxes preview the
    /// neighbours. Landing on an unlocked hat (or the leading "None" slot)
    /// wears and saves it immediately; a locked hat previews as a black
    /// silhouette under a "?" and leaves the worn hat alone.
    ///
    /// Hats unlocked since the selector was last seen front the carousel
    /// (worn) and carry a red NEW badge for that visit - the same
    /// seen-tracking flow as the love notes grid.
    /// </summary>
    public class HatSelectorUI : MonoBehaviour
    {
        [SerializeField] private Button leftArrow;
        [SerializeField] private Button rightArrow;
        [SerializeField] private RawImage leftThumb;
        [SerializeField] private RawImage centerThumb;
        [SerializeField] private RawImage rightThumb;
        [SerializeField] private TextMeshProUGUI leftLock;
        [SerializeField] private TextMeshProUGUI centerLock;
        [SerializeField] private TextMeshProUGUI rightLock;
        [SerializeField] private TextMeshProUGUI leftNone;
        [SerializeField] private TextMeshProUGUI centerNone;
        [SerializeField] private TextMeshProUGUI rightNone;
        [SerializeField] private TextMeshProUGUI leftHoliday;
        [SerializeField] private TextMeshProUGUI centerHoliday;
        [SerializeField] private TextMeshProUGUI rightHoliday;
        [SerializeField] private GameObject leftBadge;
        [SerializeField] private GameObject centerBadge;
        [SerializeField] private GameObject rightBadge;
        [SerializeField] private TextMeshProUGUI countText;
        [SerializeField] private RectTransform centerBox;

        // Unseen-at-open snapshot: badges show for this whole home visit even
        // though MarkAllSeen runs immediately (mirrors LoveNotesPanel.Rebuild
        // running before MarkAllSeen)
        private readonly HashSet<string> unseenSnapshot = new HashSet<string>();

        private int browseIndex;
        private int seenStateVersion = -1;

        private void Awake()
        {
            if (leftArrow != null)
                leftArrow.onClick.AddListener(() => Step(-1));
            if (rightArrow != null)
                rightArrow.onClick.AddListener(() => Step(1));
        }

        private void OnEnable()
        {
            unseenSnapshot.Clear();
            foreach (string id in HatManager.HatIds)
            {
                if (HatManager.IsHatUnseen(id))
                    unseenSnapshot.Add(id);
            }

            string newest = HatManager.NewestUnseenId;
            if (newest != null)
            {
                // A hat unlocked since the last visit fronts the carousel,
                // already worn, under its NEW badge
                browseIndex = EntryIndexOf(newest);
                HatManager.SelectedId = newest;
                FindAnyObjectByType<HatEquipper>()?.ApplySelected();
            }
            else
            {
                browseIndex = EntryIndexOf(HatManager.SelectedId);
            }

            HatManager.MarkAllSeen();

            // Visual refresh waits for the first Update: thumbnails render on
            // demand, and a render request during scene-load OnEnable would
            // run before URP has set up. The screen is still behind the fade.
            seenStateVersion = -1;
        }

        private void Update()
        {
            // Debug-menu unlocks/resets and mid-session changes land while
            // this is on screen - the version compare keeps the poll free
            if (seenStateVersion != HatManager.StateVersion)
                Refresh();
        }

        // Slot 0 is the always-available "None" entry
        private static int EntryCount => HatManager.HatIds.Length + 1;

        private static string EntryId(int index)
        {
            return index <= 0 ? "" : HatManager.HatIds[index - 1];
        }

        private static int EntryIndexOf(string id)
        {
            for (int i = 1; i < EntryCount; i++)
            {
                if (EntryId(i) == id)
                    return i;
            }
            return 0;
        }

        private void Step(int direction)
        {
            browseIndex = (browseIndex + direction + EntryCount) % EntryCount;

            // Landing on something wearable selects it right away; a locked
            // hat just previews and the worn hat stays on
            string id = EntryId(browseIndex);
            if (id.Length == 0 || HatManager.IsUnlocked(id))
            {
                HatManager.SelectedId = id;
                FindAnyObjectByType<HatEquipper>()?.ApplySelected();
            }

            Refresh();

            if (centerBox != null)
                Juice.PunchScale(centerBox, 0.1f, 0.16f);
        }

        private void Refresh()
        {
            seenStateVersion = HatManager.StateVersion;
            RefreshSlot(leftThumb, leftLock, leftNone, leftHoliday, leftBadge, (browseIndex - 1 + EntryCount) % EntryCount);
            RefreshSlot(centerThumb, centerLock, centerNone, centerHoliday, centerBadge, browseIndex);
            RefreshSlot(rightThumb, rightLock, rightNone, rightHoliday, rightBadge, (browseIndex + 1) % EntryCount);

            if (countText != null)
                countText.text = $"{HatManager.UnlockedCount}/{HatManager.TotalCount}";
        }

        private void RefreshSlot(RawImage thumb, TextMeshProUGUI lockMark, TextMeshProUGUI noneMark,
            TextMeshProUGUI holidayMark, GameObject badge, int entryIndex)
        {
            string id = EntryId(entryIndex);
            bool locked = id.Length > 0 && !HatManager.IsUnlocked(id);

            if (thumb != null)
            {
                Texture texture = id.Length > 0 ? HatThumbnails.Get(id, locked) : null;
                thumb.texture = texture;
                thumb.enabled = texture != null;
            }

            if (lockMark != null)
                lockMark.gameObject.SetActive(locked);

            if (noneMark != null)
                noneMark.gameObject.SetActive(id.Length == 0);

            // Seasonal hats wear their holiday on the box - a locked "?" with
            // "Halloween" under it tells you exactly when to come back
            if (holidayMark != null)
            {
                HatDef def = HatManager.GetDef(id);
                bool seasonal = def != null && def.IsSeasonal;
                holidayMark.gameObject.SetActive(seasonal);
                if (seasonal)
                    holidayMark.text = def.HolidayShort;
            }

            if (badge != null)
                badge.SetActive(!locked && id.Length > 0 && unseenSnapshot.Contains(id));
        }
    }
}
