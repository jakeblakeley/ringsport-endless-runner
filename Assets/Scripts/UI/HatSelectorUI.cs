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
    /// Freshly unlocked hats front the carousel (worn) and carry a red NEW
    /// badge until the player actually browses the carousel - merely passing
    /// through the home screen doesn't consume it (a post-death bounce home
    /// used to eat the badge before anyone saw it). Mirrors the love notes
    /// grid, whose badge lives until the panel is opened.
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
            EnsureEntryOrder();

            string newest = HatManager.NewestUnseenId;
            if (newest != null)
            {
                // A hat unlocked since the player last browsed fronts the
                // carousel, already worn, under its NEW badge. Seen-marking
                // waits for an actual browse (Step) - not this enable - so
                // the badge survives home visits the player taps through.
                browseIndex = EntryIndexOf(newest);
                HatManager.SelectedId = newest;
                FindAnyObjectByType<HatEquipper>()?.ApplySelected();
            }
            else
            {
                browseIndex = EntryIndexOf(HatManager.SelectedId);
            }

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

        // Slot 0 is the always-available "None" entry; hats follow with every
        // unlocked one first (catalog order within each group), so browsing
        // the wardrobe never wades through locked silhouettes to reach it
        private readonly List<string> entryOrder = new List<string>();
        private int entryOrderVersion = -1;

        private int EntryCount => entryOrder.Count + 1;

        private void EnsureEntryOrder()
        {
            if (entryOrderVersion == HatManager.StateVersion)
                return;

            // An unlock while the selector is visible reorders the list -
            // keep the view on the same hat, not the same slot number
            string centerId = entryOrder.Count > 0 ? EntryId(browseIndex) : null;

            entryOrder.Clear();
            foreach (string id in HatManager.HatIds)
            {
                if (HatManager.IsUnlocked(id))
                    entryOrder.Add(id);
            }
            foreach (string id in HatManager.HatIds)
            {
                if (!HatManager.IsUnlocked(id))
                    entryOrder.Add(id);
            }
            entryOrderVersion = HatManager.StateVersion;

            if (centerId != null)
                browseIndex = EntryIndexOf(centerId);
        }

        private string EntryId(int index)
        {
            return index <= 0 || index > entryOrder.Count ? "" : entryOrder[index - 1];
        }

        private int EntryIndexOf(string id)
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
            // Browsing is the "I've seen it" signal that clears NEW badges -
            // the refresh below drops them this same tap
            HatManager.MarkAllSeen();

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
            EnsureEntryOrder();
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
            HatDef def = HatManager.GetDef(id);
            bool seasonal = def != null && def.IsSeasonal;

            if (thumb != null)
            {
                Texture texture = id.Length > 0 ? HatThumbnails.Get(id, locked) : null;
                thumb.texture = texture;
                thumb.enabled = texture != null;
            }

            // LOCKED seasonal boxes wear their holiday as the diagonal sash in
            // place of the "?" - silhouette + holiday name says exactly what
            // to come back for. Once unlocked the hat speaks for itself.
            if (lockMark != null)
                lockMark.gameObject.SetActive(locked && !seasonal);

            if (noneMark != null)
                noneMark.gameObject.SetActive(id.Length == 0);

            if (holidayMark != null)
            {
                bool showHoliday = seasonal && locked;
                holidayMark.gameObject.SetActive(showHoliday);
                if (showHoliday)
                    holidayMark.text = def.HolidayShort;
            }

            if (badge != null)
                badge.SetActive(!locked && id.Length > 0 && HatManager.IsHatUnseen(id));
        }
    }
}
