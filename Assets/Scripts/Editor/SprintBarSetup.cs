using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

namespace RingSport.Editor
{
    public class SprintBarSetup : MonoBehaviour
    {
        [MenuItem("RingSport/Setup Sprint Bar UI")]
        public static void CreateSprintBarUI()
        {
            // Find the GameHud canvas
            GameObject gameHud = GameObject.Find("GameHud");
            if (gameHud == null)
            {
                Debug.LogError("GameHud canvas not found! Make sure you're in the SampleScene.");
                return;
            }

            // Check if sprint bar already exists
            Transform existingBar = gameHud.transform.Find("SprintBarBackground");
            if (existingBar != null)
            {
                Debug.LogWarning("Sprint bar already exists! Skipping creation.");
                return;
            }

            // Create background
            GameObject background = new GameObject("SprintBarBackground");
            background.transform.SetParent(gameHud.transform, false);
            background.layer = 5; // UI layer

            RectTransform backgroundRect = background.AddComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0.5f, 0f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0f);
            backgroundRect.anchoredPosition = new Vector2(0f, 40f);
            backgroundRect.sizeDelta = new Vector2(200f, 20f);

            Image backgroundImage = background.AddComponent<Image>();
            backgroundImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            backgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // Dark gray

            // Create fill bar
            GameObject fill = new GameObject("SprintBarFill");
            fill.transform.SetParent(background.transform, false);
            fill.layer = 5; // UI layer

            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;

            Image fillImage = fill.AddComponent<Image>();
            fillImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            fillImage.color = new Color(0.29f, 0.56f, 0.89f, 1f); // Blue
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;

            Debug.Log("Sprint bar UI created successfully! Now assign the SprintBarFill to UIManager in the Inspector.");

            // Select the UIManager so user can assign the reference
            GameObject uiManagerObj = GameObject.Find("UIManager");
            if (uiManagerObj != null)
            {
                Selection.activeGameObject = uiManagerObj;
                Debug.Log("Selected UIManager - please assign the SprintBarFill reference in the Inspector.");
            }

            // Mark scene as dirty so changes are saved
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
