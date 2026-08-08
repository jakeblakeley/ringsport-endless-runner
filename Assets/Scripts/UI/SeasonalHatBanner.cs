using System;
using TMPro;
using UnityEngine;
using RingSport.Core;

namespace RingSport.UI
{
    /// <summary>
    /// Home-screen callout above the hat selector while a seasonal hat's
    /// window is open and it's still locked:
    ///
    ///     Party Hat for Caicos's Birthday!
    ///     Until August 10
    ///
    /// The label breathes (scale pulse) so it reads as an event, and vanishes
    /// the moment the hat is unlocked - HatManager.TryGetActiveSeasonal is the
    /// single source of truth. Lives on an always-active host under the home
    /// screen; built and wired by Tools > RingSport > Setup Hats.
    /// </summary>
    public class SeasonalHatBanner : MonoBehaviour
    {
        private const float PulseScale = 0.06f;    // +/-6% around rest size
        private const float PulseSeconds = 1.1f;   // one full breath
        private const float RecheckSeconds = 60f;  // date can roll over while the app sits open

        [SerializeField] private TextMeshProUGUI label;

        private int seenStateVersion = -1;
        private float nextCheckAt;
        private string shownHatId;

        private void OnEnable()
        {
            seenStateVersion = -1;
            nextCheckAt = 0f;
            shownHatId = null;
        }

        private void Update()
        {
            if (label == null)
                return;

            // Unlocks bump StateVersion the frame they land; the timer only
            // covers the midnight/day-change edge
            if (seenStateVersion != HatManager.StateVersion || Time.unscaledTime >= nextCheckAt)
            {
                seenStateVersion = HatManager.StateVersion;
                nextCheckAt = Time.unscaledTime + RecheckSeconds;
                RefreshVisibility();
            }

            if (!label.gameObject.activeSelf)
                return;

            float wave = Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / PulseSeconds));
            label.rectTransform.localScale = Vector3.one * (1f + PulseScale * wave);
        }

        private void RefreshVisibility()
        {
            bool show = HatManager.TryGetActiveSeasonal(out HatDef def, out DateTime windowEnd);

            if (!show)
            {
                shownHatId = null;
                if (label.gameObject.activeSelf)
                    label.gameObject.SetActive(false);
                return;
            }

            if (!label.gameObject.activeSelf)
                label.gameObject.SetActive(true);

            if (shownHatId == def.Id)
                return;

            shownHatId = def.Id;
            label.text = $"{def.DisplayName} for {def.HolidayName}!\n" +
                         $"<size=72%>Until {HatManager.FormatSeasonEnd(windowEnd)}</size>";
        }
    }
}
