using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RingSport.Core;

namespace RingSport.UI
{
    public class BiteMinigame : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject minigamePanel;
        [SerializeField] private Image timingBar;
        [SerializeField] private RectTransform targetZone;
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("Settings")]
        [SerializeField] private float barSpeed = 2f;
        [SerializeField] private float targetZoneSize = 0.2f; // 20% of bar
        [SerializeField] private int bonusPoints = 100;

        private PlayerInput playerInput;
        private InputAction attackAction;
        private bool isActive = false;
        private float currentProgress = 0f;
        private bool movingForward = true;

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                playerInput = gameObject.AddComponent<PlayerInput>();
            }

            SetupInputActions();

            if (minigamePanel != null)
                minigamePanel.SetActive(false);
        }

        private void SetupInputActions()
        {
            var actionMap = playerInput.actions.FindActionMap("Player");
            attackAction = actionMap.FindAction("Attack");

            if (attackAction != null)
            {
                attackAction.performed += OnBitePressed;
            }
        }

        private void OnEnable()
        {
            if (attackAction != null)
                attackAction.performed += OnBitePressed;
        }

        private void OnDisable()
        {
            if (attackAction != null)
                attackAction.performed -= OnBitePressed;
        }

        private void Update()
        {
            if (!isActive)
                return;

            // Animate timing bar back and forth
            if (movingForward)
            {
                currentProgress += barSpeed * Time.unscaledDeltaTime;
                if (currentProgress >= 1f)
                {
                    currentProgress = 1f;
                    movingForward = false;
                }
            }
            else
            {
                currentProgress -= barSpeed * Time.unscaledDeltaTime;
                if (currentProgress <= 0f)
                {
                    currentProgress = 0f;
                    movingForward = true;
                }
            }

            // Update visual
            if (timingBar != null)
            {
                timingBar.fillAmount = currentProgress;
            }
        }

        public void StartMinigame()
        {
            isActive = true;
            currentProgress = 0f;
            movingForward = true;

            if (minigamePanel != null)
                minigamePanel.SetActive(true);

            if (feedbackText != null)
                feedbackText.text = "Press BITE at the right time!";
        }

        private void OnBitePressed(InputAction.CallbackContext context)
        {
            if (!isActive)
                return;

            isActive = false;

            // Check if within target zone
            float targetCenter = 0.5f; // Center of the bar
            float halfZone = targetZoneSize / 2f;
            bool success = currentProgress >= (targetCenter - halfZone) &&
                          currentProgress <= (targetCenter + halfZone);

            if (success)
            {
                HandleSuccess();
            }
            else
            {
                HandleFailure();
            }

            // Show reward screen after short delay
            Invoke(nameof(ShowRewards), 1.5f);
        }

        private void HandleSuccess()
        {
            LevelManager.Instance?.AddScore(bonusPoints);

            if (feedbackText != null)
                feedbackText.text = $"Perfect! +{bonusPoints} Bonus!";
        }

        private void HandleFailure()
        {
            if (feedbackText != null)
                feedbackText.text = "Missed!";
        }

        private void ShowRewards()
        {
            if (minigamePanel != null)
                minigamePanel.SetActive(false);

            int level = LevelManager.Instance?.CurrentLevel ?? 1;
            int score = ScoreManager.Instance?.CurrentScore ?? 0;

            UIManager.Instance?.ShowRewardScreen(level, score);
        }
    }
}
