using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Player;
using RingSport.Level;
using RingSport.Input;

namespace RingSport.UI
{
    public class PalisadeMinigame : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject minigamePanel;
        [SerializeField] private Image progressBar;
        [SerializeField] private RectTransform progressBarRect;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private TextMeshProUGUI instructionText;

        [Header("Settings")]
        [SerializeField] private float timeLimit = 4f;

        [Header("Juice (temporary clips - see SOUND_EFFECTS.md)")]
        [SerializeField] private AudioClip wallHitSound;
        [SerializeField] private AudioClip tapThockSound;
        [SerializeField] private AudioClip timerTickSound;
        [SerializeField] private AudioClip successBarkSound;
        [SerializeField] [Range(0f, 1f)] private float juiceSfxVolume = 0.85f;
        [Tooltip("Timer turns urgent (red + pulse + ticks) under this many seconds.")]
        [SerializeField] private float urgencyThreshold = 1.5f;

        private PlayerInput playerInput;
        private InputAction sprintAction;
        private MobileInputHandler mobileInputHandler;
        private bool isActive = false;
        private bool isSubscribed = false;
        private bool isMobileSubscribed = false;
        private int currentTaps = 0;
        private int requiredTaps = 0;
        private float timeRemaining = 0f;
        private Vector3 obstaclePosition;
        private float obstacleHeight;
        private PlayerController player;
        private AudioSource sfxSource;
        private float startFillAmount;
        private float targetFill;
        private float displayedFill;
        private Color timerBaseColor = Color.white;
        private bool timerColorCaptured;
        private int lastTickHalfSecond;

        private void Awake()
        {
            // Don't look for PlayerInput here, we'll get it when we need it
            if (minigamePanel != null)
                minigamePanel.SetActive(false);

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
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
                playerInput = FindAnyObjectByType<PlayerInput>();
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

            // Also subscribe to mobile input if available
            SubscribeToMobileInput();
        }

        private void SubscribeToMobileInput()
        {
            if (isMobileSubscribed)
                return;

            // Get mobile input handler from player if we don't have it
            if (mobileInputHandler == null && player != null)
            {
                mobileInputHandler = player.GetComponent<MobileInputHandler>();
            }

            // Subscribe to mobile press events
            if (mobileInputHandler != null)
            {
                mobileInputHandler.OnPressTriggered += OnMobileTap;
                isMobileSubscribed = true;
                Debug.Log("PalisadeMinigame subscribed to mobile input");
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

            // Also unsubscribe from mobile input
            UnsubscribeFromMobileInput();
        }

        private void UnsubscribeFromMobileInput()
        {
            if (!isMobileSubscribed)
                return;

            if (mobileInputHandler != null)
            {
                mobileInputHandler.OnPressTriggered -= OnMobileTap;
                isMobileSubscribed = false;
                Debug.Log("PalisadeMinigame unsubscribed from mobile input");
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
                timerText.text = $"{Mathf.Max(0f, timeRemaining):F1}s";

                // Urgency: red + pulse + half-second ticks in the last stretch
                if (timeRemaining <= urgencyThreshold)
                {
                    timerText.color = new Color(0.91f, 0.3f, 0.24f);
                    timerText.transform.localScale =
                        Vector3.one * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 6f));

                    int halfSecond = Mathf.FloorToInt(Mathf.Max(0f, timeRemaining) * 2f);
                    if (halfSecond != lastTickHalfSecond)
                    {
                        lastTickHalfSecond = halfSecond;
                        PlayClip(timerTickSound, 0.9f, 0.6f);
                    }
                }
                else
                {
                    timerText.color = timerBaseColor;
                    timerText.transform.localScale = Vector3.one;
                }
            }

            // Bar + dog climb ease toward the tap target instead of snapping
            displayedFill = Mathf.MoveTowards(displayedFill, targetFill, 4f * Time.unscaledDeltaTime);
            if (progressBarRect != null)
                progressBarRect.anchorMax = new Vector2(displayedFill, progressBarRect.anchorMax.y);
            player?.Animations?.SetClamberProgress(displayedFill);

            // Check for timeout
            if (timeRemaining <= 0f)
            {
                HandleFailure();
            }
        }

        private void PlayClip(AudioClip clip, float pitch = 1f, float volumeScale = 1f)
        {
            if (clip == null || sfxSource == null)
                return;

            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, juiceSfxVolume * volumeScale);
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

            // Dog grabs the palisade and hangs on while the player taps
            player?.Animations?.SetClambering(true);

            // Wall-hit impact: thud + shake + dust where the dog grabbed on
            PlayClip(wallHitSound);
            CameraStateMachine.Instance?.AddShake(0.3f);
            if (player != null)
                ImpactVFX.PlayDust(player.transform.position + Vector3.up * 0.5f, 10);

            // Reset timer urgency visuals and the smoothed bar
            if (timerText != null)
            {
                if (!timerColorCaptured)
                {
                    timerBaseColor = timerText.color;
                    timerColorCaptured = true;
                }
                timerText.color = timerBaseColor;
                timerText.transform.localScale = Vector3.one;
            }
            lastTickHalfSecond = int.MaxValue;
            startFillAmount = Mathf.Lerp(0f, 0.9f, 1f - (requiredTaps / 10f));
            targetFill = startFillAmount;
            displayedFill = startFillAmount;

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
                instructionText.text = "TAP!";
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
            OnTapFeedback();

            Debug.Log($"Tap registered! {currentTaps}/{requiredTaps}");

            // Check if enough taps
            if (currentTaps >= requiredTaps)
            {
                HandleSuccess();
            }
        }

        private void OnMobileTap()
        {
            Debug.Log($"OnMobileTap called! isActive: {isActive}, currentTaps: {currentTaps}, requiredTaps: {requiredTaps}");

            if (!isActive)
                return;

            currentTaps++;
            UpdateProgressBar();
            OnTapFeedback();

            Debug.Log($"Mobile tap registered! {currentTaps}/{requiredTaps}");

            // Check if enough taps
            if (currentTaps >= requiredTaps)
            {
                HandleSuccess();
            }
        }

        /// <summary>Per-tap juice: rising thock + a small punch on the bar.</summary>
        private void OnTapFeedback()
        {
            float progressPercent = requiredTaps > 0 ? (float)currentTaps / requiredTaps : 1f;
            PlayClip(tapThockSound, 1f + 0.35f * progressPercent);
            if (progressBarRect != null && progressBarRect.parent != null)
                Juice.PunchScale(progressBarRect.parent, 0.08f, 0.12f);
        }

        private void UpdateProgressBar()
        {
            // Progress starts low based on required taps and fills to 1.0:
            // 10 taps = start 0%, 5 taps = ~50%, 1 tap = ~90%. Update() eases
            // the displayed bar (and the dog's clamber scrub) toward this.
            float progressPercent = requiredTaps > 0 ? (float)currentTaps / requiredTaps : 1f;
            targetFill = Mathf.Lerp(startFillAmount, 1f, progressPercent);
        }

        private void HandleSuccess()
        {
            isActive = false;

            // Snap the climb to the top before the vault takes over
            displayedFill = 1f;
            targetFill = 1f;
            player?.Animations?.SetClamberProgress(1f);

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
            // Resume the world scroll BEFORE the vault so the palisade passes
            // underneath the dog mid-arc and it lands beyond it - with the
            // scroll frozen the dog would hop in place and the palisade would
            // then slide through the model. Player movement stays paused so
            // gravity doesn't fight the scripted arc.
            LevelScroller.Instance?.Resume();

            // Triumphant bark as the vault fires (landing dust comes from the
            // regular landing feedback once movement resumes)
            PlayClip(successBarkSound);

            // Animate player over obstacle
            yield return player.StartCoroutine(player.AnimateOverObstacle(obstaclePosition, obstacleHeight));

            // Trigger recovery zone in level generator (fairness feature)
            if (LevelGenerator.Instance != null)
            {
                LevelGenerator.Instance.OnPalisadeCompleted();
            }

            // Resume game
            player?.ResumeMovement();

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

            // Drop off the palisade before the death animation takes over
            player?.Animations?.SetClambering(false);

            // Trigger game over
            GameManager.Instance?.TriggerGameOver();
        }

        private void OnDestroy()
        {
            // Cleanup on destroy
            UnsubscribeFromInput();
            UnsubscribeFromMobileInput();
        }
    }
}
