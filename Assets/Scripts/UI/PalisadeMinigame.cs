using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RingSport.Core;
using RingSport.Player;
using RingSport.Level;

namespace RingSport.UI
{
    public class PalisadeMinigame : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject minigamePanel;
        [SerializeField] private Image progressBar;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI instructionText;

        [Header("Settings")]
        [SerializeField] private float timeLimit = 4f;

        private PlayerInput playerInput;
        private InputAction sprintAction;
        private bool isActive = false;
        private bool isSubscribed = false;
        private int currentTaps = 0;
        private int requiredTaps = 0;
        private float timeRemaining = 0f;
        private Vector3 obstaclePosition;
        private float obstacleHeight;
        private PlayerController player;

        private void Awake()
        {
            // Don't look for PlayerInput here, we'll get it when we need it
            if (minigamePanel != null)
                minigamePanel.SetActive(false);
        }

        private void EnsureInputSetup()
        {
            // Get PlayerInput from the player if we don't have it yet
            if (playerInput == null && player != null)
            {
                playerInput = player.GetComponent<PlayerInput>();
                if (playerInput == null)
                {
                    Debug.LogError("PlayerInput component not found on player GameObject!");
                    return;
                }
            }

            // Fallback: search the scene
            if (playerInput == null)
            {
                playerInput = FindObjectOfType<PlayerInput>();
                if (playerInput == null)
                {
                    Debug.LogError("PlayerInput not found! PalisadeMinigame requires PlayerInput to be in the scene.");
                    return;
                }
            }

            // Check if actions asset is assigned
            if (playerInput.actions == null)
            {
                Debug.LogError("PlayerInput.actions is null! Make sure the InputActions asset is assigned to the PlayerInput component.");
                return;
            }

            if (sprintAction == null)
            {
                var actionMap = playerInput.actions.FindActionMap("Player");
                if (actionMap == null)
                {
                    Debug.LogError("Player action map not found!");
                    return;
                }

                sprintAction = actionMap.FindAction("Sprint");
                if (sprintAction == null)
                {
                    Debug.LogError("Sprint action not found!");
                    return;
                }
            }
        }

        private void SubscribeToInput()
        {
            if (isSubscribed)
                return;

            EnsureInputSetup();

            if (sprintAction != null)
            {
                // Make sure the action is enabled
                if (!sprintAction.enabled)
                {
                    sprintAction.Enable();
                    Debug.Log("PalisadeMinigame enabled sprint action");
                }

                sprintAction.performed += OnTapPressed;
                isSubscribed = true;
                Debug.Log("PalisadeMinigame subscribed to sprint input");
            }
        }

        private void UnsubscribeFromInput()
        {
            if (!isSubscribed)
                return;

            if (sprintAction != null)
            {
                sprintAction.performed -= OnTapPressed;
                isSubscribed = false;
                Debug.Log("PalisadeMinigame unsubscribed from sprint input");
            }
        }

        private void Update()
        {
            if (!isActive)
                return;

            // Countdown timer using unscaled time
            timeRemaining -= Time.unscaledDeltaTime;

            // Update timer UI
            if (timerText != null)
            {
                timerText.text = $"Time: {Mathf.Max(0f, timeRemaining):F1}s";
            }

            // Check for timeout
            if (timeRemaining <= 0f)
            {
                HandleFailure();
            }
        }

        public void StartMinigame(int tapsRequired, Vector3 obstaclePos, float obstHeight, PlayerController playerController)
        {
            Debug.Log($"=== PalisadeMinigame.StartMinigame called ===");
            Debug.Log($"Required taps: {tapsRequired}, Panel assigned: {(minigamePanel != null ? "YES" : "NO")}");

            isActive = true;
            currentTaps = 0;
            requiredTaps = tapsRequired;
            timeRemaining = timeLimit;
            obstaclePosition = obstaclePos;
            obstacleHeight = obstHeight;
            player = playerController;

            // Pause game
            LevelScroller.Instance?.Pause();
            player?.PauseMovement();
            Debug.Log("Game paused");

            // Subscribe to input BEFORE showing UI
            SubscribeToInput();

            // Show UI
            if (minigamePanel != null)
            {
                Debug.Log($"Setting minigamePanel active. Current state: {minigamePanel.activeSelf}, setting to TRUE");
                minigamePanel.SetActive(true);
                Debug.Log($"After SetActive(true), panel active: {minigamePanel.activeSelf}");
            }
            else
            {
                Debug.LogError("minigamePanel is NULL! Assign it in the inspector!");
            }

            // Initialize progress bar
            UpdateProgressBar();

            if (instructionText != null)
                instructionText.text = "Tap SPRINT to climb!";
            else
                Debug.LogWarning("instructionText is null!");

            Debug.Log($"Palisade minigame started! Required taps: {requiredTaps}, Time limit: {timeLimit}s, Input subscribed: {isSubscribed}");
        }

        private void OnTapPressed(InputAction.CallbackContext context)
        {
            Debug.Log($"OnTapPressed called! isActive: {isActive}, currentTaps: {currentTaps}, requiredTaps: {requiredTaps}");

            if (!isActive)
                return;

            currentTaps++;
            UpdateProgressBar();

            Debug.Log($"Tap registered! {currentTaps}/{requiredTaps}");

            // Check if enough taps
            if (currentTaps >= requiredTaps)
            {
                HandleSuccess();
            }
        }

        private void UpdateProgressBar()
        {
            // Calculate progress: starts low based on required taps, fills to 1.0 when complete
            // Start percentage inversely proportional to required taps:
            // 10 taps required = start at 0% (0.0)
            // 5 taps required = start at ~50% (0.5)
            // 1 tap required = start at ~90% (0.9)
            float startFillAmount = Mathf.Lerp(0f, 0.9f, 1f - (requiredTaps / 10f));

            // Current progress from start to 100%
            float progressPercent = requiredTaps > 0 ? (float)currentTaps / requiredTaps : 1f;
            float currentFillAmount = Mathf.Lerp(startFillAmount, 1f, progressPercent);

            if (progressBar != null)
            {
                progressBar.fillAmount = currentFillAmount;
                Debug.Log($"Progress bar updated: {currentFillAmount:F2} (taps: {currentTaps}/{requiredTaps})");
            }
        }

        private void HandleSuccess()
        {
            isActive = false;

            Debug.Log("Palisade cleared successfully!");

            if (instructionText != null)
                instructionText.text = "Success!";

            // Unsubscribe from input
            UnsubscribeFromInput();

            // Hide UI
            if (minigamePanel != null)
                minigamePanel.SetActive(false);

            // Start animation coroutine
            if (player != null)
            {
                StartCoroutine(AnimateAndResume());
            }
        }

        private System.Collections.IEnumerator AnimateAndResume()
        {
            // Animate player over obstacle
            yield return player.StartCoroutine(player.AnimateOverObstacle(obstaclePosition, obstacleHeight));

            // Resume game
            player?.ResumeMovement();
            LevelScroller.Instance?.Resume();

            Debug.Log("Palisade animation complete, game resumed");
        }

        private void HandleFailure()
        {
            isActive = false;

            Debug.Log("Palisade failed - not enough taps in time!");

            if (instructionText != null)
                instructionText.text = "Failed!";

            // Unsubscribe from input
            UnsubscribeFromInput();

            // Hide UI
            if (minigamePanel != null)
                minigamePanel.SetActive(false);

            // Trigger game over
            GameManager.Instance?.TriggerGameOver();
        }

        private void OnDestroy()
        {
            // Cleanup on destroy
            UnsubscribeFromInput();
        }
    }
}
