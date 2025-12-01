using UnityEngine;
using UnityEditor;
using RingSport.Core;

namespace RingSport.Editor
{
    [CustomEditor(typeof(ScoreManager))]
    public class ScoreManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw the default inspector
            DrawDefaultInspector();

            ScoreManager scoreManager = (ScoreManager)target;

            // Add some space
            EditorGUILayout.Space(10);

            // Add a header
            EditorGUILayout.LabelField("Debug Tools", EditorStyles.boldLabel);

            // Add the reset button
            if (GUILayout.Button("Clear High Score", GUILayout.Height(30)))
            {
                if (EditorApplication.isPlaying)
                {
                    // If in play mode, call the method directly
                    scoreManager.ClearHighScore();
                    Debug.Log("[ScoreManagerEditor] High score cleared via Inspector button!");
                }
                else
                {
                    // If not in play mode, clear PlayerPrefs directly
                    if (EditorUtility.DisplayDialog(
                        "Clear High Score",
                        "Are you sure you want to clear the saved high score?",
                        "Yes, Clear It",
                        "Cancel"))
                    {
                        PlayerPrefs.DeleteKey("HighScore");
                        PlayerPrefs.Save();
                        Debug.Log("[ScoreManagerEditor] High score cleared! (Editor mode)");
                    }
                }
            }

            // Show current high score info
            EditorGUILayout.Space(5);
            if (EditorApplication.isPlaying && scoreManager != null)
            {
                EditorGUILayout.HelpBox(
                    $"Current High Score: {scoreManager.HighScore}\n" +
                    $"Current Total Score: {scoreManager.TotalScore}\n" +
                    $"Current Level Score: {scoreManager.CurrentScore}",
                    MessageType.Info);
            }
            else
            {
                int savedHighScore = PlayerPrefs.GetInt("HighScore", 0);
                EditorGUILayout.HelpBox(
                    $"Saved High Score: {savedHighScore}\n" +
                    "(Enter Play Mode to see live scores)",
                    MessageType.Info);
            }
        }
    }
}
