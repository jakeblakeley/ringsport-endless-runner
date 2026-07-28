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

            const float toggleWidth = 90f;
            Rect toggleRect = new Rect((screenWidth - toggleWidth) / 2f, 8f, toggleWidth, 26f);
            if (GUI.Button(toggleRect, isOpen ? "DEBUG ▲" : "DEBUG ▼"))
                isOpen = !isOpen;

            if (!isOpen)
                return;

            const float panelWidth = 280f;
            float panelHeight = Mathf.Min(460f, screenHeight - 60f);
            Rect panelRect = new Rect((screenWidth - panelWidth) / 2f, 40f, panelWidth, panelHeight);

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

            int maxLevels = LevelManager.Instance != null ? LevelManager.Instance.MaxLevels : 9;
            for (int level = 1; level <= maxLevels; level++)
            {
                LevelConfig config = LevelGenerator.Instance?.GetLevelConfig(level);
                string label = config != null && !string.IsNullOrEmpty(config.LevelName)
                    ? $"{level}. {config.LevelName} ({config.Location})"
                    : $"Level {level}";

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
                if (GUILayout.Button(Nicify(type.ToString()), GUILayout.Height(28f)))
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
