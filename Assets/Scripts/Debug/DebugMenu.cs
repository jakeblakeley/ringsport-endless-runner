// Entire class is stripped from release builds; only exists in the editor
// and in development ("Debug") builds.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;
using RingSport.Core;
using RingSport.Level;
using RingSport.UI;

namespace RingSport.DebugTools
{
    /// <summary>
    /// Debug menu for quickly testing levels and mini games. Self-instantiates at
    /// startup so no scene setup is required. Shows a small DEBUG button at the top
    /// of the home screen that expands into a panel with shortcuts.
    /// </summary>
    public class DebugMenu : MonoBehaviour
    {
        private static DebugMenu instance;

        private bool isOpen;
        private Vector2 scrollPosition;
        private GUIStyle headerStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var go = new GameObject("DebugMenu");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DebugMenu>();
        }

        private void OnGUI()
        {
            // Only show on the home/start screen
            if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.Home)
            {
                isOpen = false;
                return;
            }

            // IMGUI draws above every canvas - stay hidden while the secret
            // note overlay owns the screen
            if (UIManager.Instance != null && UIManager.Instance.IsSecretNoteOpen)
                return;

            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 14
                };
            }

            // Scale IMGUI up on high-DPI screens so the menu stays readable
            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;

            // Clear the phone's status bar / notch. IMGUI knows nothing about safe
            // areas, and Screen.safeArea is always 0 on web, so reuse the inset
            // TopSafeArea reads out of JavaScript - otherwise this button renders
            // underneath the status bar in fullscreen and looks like it is missing.
            float topInset = TopSafeArea.AppliedFraction * screenHeight;

            const float toggleWidth = 90f;
            Rect toggleRect = new Rect((screenWidth - toggleWidth) / 2f, topInset + 8f, toggleWidth, 26f);
            if (GUI.Button(toggleRect, isOpen ? "DEBUG ▲" : "DEBUG ▼"))
                isOpen = !isOpen;

            if (!isOpen)
                return;

            const float panelWidth = 280f;
            float panelHeight = Mathf.Min(460f, screenHeight - topInset - 60f);
            Rect panelRect = new Rect((screenWidth - panelWidth) / 2f, topInset + 40f, panelWidth, panelHeight);

            GUI.Box(panelRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(panelRect.x + 8f, panelRect.y + 8f, panelRect.width - 16f, panelRect.height - 16f));
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            DrawLevelButtons();
            GUILayout.Space(12f);
            DrawMiniGameButtons();
            GUILayout.Space(12f);
            DrawUtilities();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLevelButtons()
        {
            GUILayout.Label("Levels", headerStyle);

            int maxLevels = LevelManager.Instance != null ? LevelManager.Instance.MaxLevels : 8;
            for (int level = 1; level <= maxLevels; level++)
            {
                LevelConfig config = LevelGenerator.Instance?.GetLevelConfig(level);
                string label = config != null && !string.IsNullOrEmpty(config.LevelName)
                    ? $"{level}. {config.LevelName} ({config.Location})"
                    : $"Level {level}";

                // Mark which mini level the run ends in (this run's order - the
                // opening levels shuffle theirs)
                if (config != null)
                {
                    switch (LevelGenerator.Instance.GetMiniLevelType(level))
                    {
                        case MiniLevelType.FleeAttack: label += " (flee)"; break;
                        case MiniLevelType.StopAttack: label += " (stop)"; break;
                        case MiniLevelType.FaceAttack: label += " (face)"; break;
                        case MiniLevelType.FoodRefusal: label += " (food)"; break;
                        case MiniLevelType.PositionsSimonSays: label += " (positions)"; break;
                    }
                }

                if (GUILayout.Button(label, GUILayout.Height(28f)))
                {
                    isOpen = false;
                    LevelManager.Instance?.DebugStartAtLevel(level);
                }
            }
        }

        private void DrawMiniGameButtons()
        {
            GUILayout.Label("Mini Games", headerStyle);

            foreach (MiniLevelType type in System.Enum.GetValues(typeof(MiniLevelType)))
            {
                // The jump is hosted on the first level that runs this mini
                // level, and the run carries on from there - say which
                string label = Nicify(type.ToString());
                int hostLevel = LevelGenerator.Instance?.FindFirstLevelWithMiniLevel(type) ?? -1;
                if (hostLevel >= 1)
                    label += $" (L{hostLevel})";

                if (GUILayout.Button(label, GUILayout.Height(28f)))
                {
                    isOpen = false;
                    GameManager.Instance?.DebugStartMiniLevel(type);
                }
            }
        }

        private void DrawUtilities()
        {
            GUILayout.Label("Utilities", headerStyle);

            if (GUILayout.Button("Clear High Score", GUILayout.Height(28f)))
            {
                ScoreManager.Instance?.ClearHighScore();
                UIManager.Instance?.ShowHomeScreen(); // refresh high score text
            }

            bool forceNotes = LoveNoteManager.DebugForceSpawnAll;
            if (GUILayout.Button($"Love Note Spawns 100%: {(forceNotes ? "ON" : "OFF")}", GUILayout.Height(28f)))
            {
                LoveNoteManager.DebugForceSpawnAll = !forceNotes;
            }

            // Per-event logs (jumps, coin arcs, ground checks) cost real frame
            // time on web dev builds - the JS console accumulates. Off unless
            // actively chasing something.
            if (GUILayout.Button($"Verbose Logs: {(GameLog.VerboseEnabled ? "ON" : "OFF")}", GUILayout.Height(28f)))
            {
                GameLog.VerboseEnabled = !GameLog.VerboseEnabled;
            }

            if (GUILayout.Button($"Unlock Love Note ({LoveNoteManager.UnlockedCount}/{LoveNoteManager.TotalCount})", GUILayout.Height(28f)))
            {
                LoveNoteManager.TryCollectRandomLockedNote(out _);
                UIManager.Instance?.RefreshHomeLoveNotes();
            }

            if (GUILayout.Button("Reset Love Notes", GUILayout.Height(28f)))
            {
                LoveNoteManager.ClearAllProgress();
                UIManager.Instance?.RefreshHomeLoveNotes();
            }

            if (GUILayout.Button($"Unlock All Hats ({HatManager.UnlockedCount}/{HatManager.TotalCount})", GUILayout.Height(28f)))
            {
                HatManager.UnlockAll();
            }

            if (GUILayout.Button("Reset Hat Unlocks", GUILayout.Height(28f)))
            {
                // Starter hats re-seed themselves inside ClearAllProgress
                HatManager.ClearAllProgress();
                // Take the (now-invalid) hat off the dog right away
                Object.FindAnyObjectByType<RingSport.Player.HatEquipper>()?.ApplySelected();
            }

            // Debug spawn odds so a hat pickup can be found without grinding
            float? hatChance = HatManager.DebugSpawnChanceOverride;
            string hatChanceLabel = hatChance == null
                ? $"{HatManager.MegaCoinReplaceChance * 100f:0}% (default)"
                : $"{hatChance.Value * 100f:0}%";
            if (GUILayout.Button($"Hat Spawn Chance: {hatChanceLabel}", GUILayout.Height(28f)))
            {
                if (hatChance == null) HatManager.DebugSpawnChanceOverride = 0.25f;
                else if (hatChance.Value < 0.99f) HatManager.DebugSpawnChanceOverride = 1f;
                else HatManager.DebugSpawnChanceOverride = null;
            }

            // Seasonal windows: what's live right now, plus a force-all toggle
            // so off-season hats can be tested any day of the year
            string seasonalStatus = HatManager.TryGetActiveSeasonal(out var seasonalDef, out var seasonalEnd)
                ? $"{seasonalDef.DisplayName} until {HatManager.FormatSeasonEnd(seasonalEnd)}"
                : "none active";
            GUILayout.Label($"Seasonal now: {seasonalStatus}");
            string forceLabel = HatManager.DebugForceAllSeasonalActive ? "ON" : "OFF";
            if (GUILayout.Button($"Force All Seasonal Windows: {forceLabel}", GUILayout.Height(28f)))
            {
                HatManager.DebugForceAllSeasonalActive = !HatManager.DebugForceAllSeasonalActive;
            }

            if (GUILayout.Button("Show Secret Note", GUILayout.Height(28f)))
            {
                isOpen = false;
                UIManager.Instance?.ShowSecretNote();
            }

            // Fakes the fullscreen status-bar inset so the top row can be checked
            // without a phone - the real value only arrives from the browser.
            float safeArea = TopSafeArea.DebugFractionOverride;
            string safeAreaLabel = safeArea < 0f ? "auto" : $"{safeArea * 100f:0}%";
            if (GUILayout.Button($"Top Safe Area: {safeAreaLabel}", GUILayout.Height(28f)))
            {
                if (safeArea < 0f) TopSafeArea.DebugFractionOverride = 0.04f;
                else if (safeArea < 0.05f) TopSafeArea.DebugFractionOverride = 0.08f;
                else TopSafeArea.DebugFractionOverride = -1f;
            }
        }

        /// <summary>
        /// "PositionsSimonSays" -> "Positions Simon Says"
        /// </summary>
        private static string Nicify(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }
    }
}
#endif
