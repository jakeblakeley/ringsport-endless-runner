using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Effects;
using RingSport.Level;
using RingSport.Core;
using RingSport.Player;
using System.Collections;
using System.Collections.Generic;

namespace RingSport.UI
{
    /// <summary>
    /// Positions Simon Says mini level gameplay.
    /// Shows sequences of positions that player must memorize and repeat.
    /// 3 rounds: 3 positions, 4 positions, 5 positions.
    /// </summary>
    public class MiniLevelPositionsSimonSays : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.PositionsSimonSays;

        private enum GamePhase { Idle, Showing, Input }

        [Header("UI References")]
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private TextMeshProUGUI simonSaysText;
        [SerializeField] private Button sitButton;
        [SerializeField] private Button downButton;
        [SerializeField] private Button standButton;

        [Header("Timing Settings")]
        [SerializeField] private float positionDisplayTime = 2f;
        [SerializeField] private float gapBetweenPositions = 0.5f;
        [SerializeField] private float incorrectFeedbackTime = 1f;
        [SerializeField] private float correctFeedbackTime = 0.3f;
        [SerializeField] private float roundTransitionTime = 1f;

        [Header("Round Configuration")]
        [SerializeField] private int[] sequenceLengths = { 3, 4, 5 };

        [Header("Juice (temporary clips - see SOUND_EFFECTS.md)")]
        [Tooltip("Per-pose tone: Sit / Down / Stand each get their own pitch.")]
        [SerializeField] private AudioClip poseToneSound;
        [SerializeField] private AudioClip correctSound;
        [SerializeField] private AudioClip wrongSound;
        [SerializeField] [Range(0f, 1f)] private float juiceVolume = 0.85f;

        [Header("Camera Framing")]
        [Tooltip("Scale on the mini-level camera's distance from its rig - 0.5 = twice as close.")]
        [SerializeField] private float cameraDistanceScale = 0.5f;
        [Tooltip("Lowers the camera after scaling; the look-at keeps the dog centered.")]
        [SerializeField] private float cameraHeightOffset = -1.2f;
        [Tooltip("Aim point height relative to the player pivot (pivot sits ~1m above the ground; -0.5 aims at the dog's chest).")]
        [SerializeField] private float cameraFocusHeight = -0.5f;

        private readonly string[] positions = { "Sit", "Down", "Stand" };

        private GamePhase currentPhase = GamePhase.Idle;
        private int currentRound = 0;
        private List<string> currentSequence = new List<string>();
        private int playerInputIndex = 0;
        private Coroutine gameCoroutine;
        private bool isProcessingInput = false;
        private PlayerController player;
        private AudioSource sfxSource;
        private Vector2 promptBasePos;
        private bool promptBaseCaptured;
        private Coroutine promptShakeRoutine;

        private void Start()
        {
            SetupButtons();
            HidePanel();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        private void PlayClip(AudioClip clip, float pitch = 1f)
        {
            if (clip == null || sfxSource == null)
                return;
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, juiceVolume);
        }

        /// <summary>Sit / Down / Stand each get a distinct pitch, so the shown sequence reads as a melody.</summary>
        private void PlayPoseTone(string position)
        {
            float pitch = position switch
            {
                "Sit" => 1f,
                "Down" => 0.8f,
                _ => 1.2f,
            };
            PlayClip(poseToneSound, pitch);
        }

        private Button ButtonFor(string position)
        {
            return position switch
            {
                "Sit" => sitButton,
                "Down" => downButton,
                _ => standButton,
            };
        }

        /// <summary>Prompt pop on each step (scale) - shake is separate (position).</summary>
        private void PunchPrompt()
        {
            if (simonSaysText != null)
                Juice.PunchScale(simonSaysText.transform, 0.2f, 0.15f);
        }

        private void ShakePrompt()
        {
            if (simonSaysText == null)
                return;

            if (!promptBaseCaptured)
            {
                promptBasePos = simonSaysText.rectTransform.anchoredPosition;
                promptBaseCaptured = true;
            }

            if (promptShakeRoutine != null)
                StopCoroutine(promptShakeRoutine);
            promptShakeRoutine = StartCoroutine(PromptShakeRoutine());
        }

        private IEnumerator PromptShakeRoutine()
        {
            const float duration = 0.4f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(elapsed / duration);
                float x = 12f * Mathf.Sin(elapsed * 55f) * (1f - n) * (1f - n);
                simonSaysText.rectTransform.anchoredPosition = promptBasePos + new Vector2(x, 0f);
                yield return null;
            }
            simonSaysText.rectTransform.anchoredPosition = promptBasePos;
            promptShakeRoutine = null;
        }

        private void SetupButtons()
        {
            if (sitButton != null)
                sitButton.onClick.AddListener(() => OnPositionButtonClicked("Sit"));

            if (downButton != null)
                downButton.onClick.AddListener(() => OnPositionButtonClicked("Down"));

            if (standButton != null)
                standButton.onClick.AddListener(() => OnPositionButtonClicked("Stand"));
        }

        public override void OnPrepareGame()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Preparing game - setting camera to MiniLevel state (close + low)");
            player = Object.FindAnyObjectByType<PlayerController>();

            // Same straight-on framing as Food Refusal but closer and lower,
            // aimed at the dog so it stays centered
            Vector3? focus = player != null
                ? player.transform.position + Vector3.up * cameraFocusHeight
                : (Vector3?)null;
            CameraStateMachine.Instance?.SetState(CameraStateType.MiniLevel, cameraDistanceScale, cameraHeightOffset, focus);

            // Dog turns around to face the mini-level camera
            player?.Animations?.SetFacing(true);
        }

        public override void StartGame()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Starting game...");

            // Reset state
            currentRound = 0;
            currentSequence.Clear();
            playerInputIndex = 0;
            isProcessingInput = false;

            player = Object.FindAnyObjectByType<PlayerController>();
            SetDogPose("Stand");

            // Show panel
            ShowPanel();
            SetButtonsInteractable(false);

            // Start first round
            gameCoroutine = StartCoroutine(RunGame());
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Stopping game...");

            if (gameCoroutine != null)
            {
                StopCoroutine(gameCoroutine);
                gameCoroutine = null;
            }

            currentPhase = GamePhase.Idle;
            SetDogPose("Stand");
            // The dog's camera-facing is NOT reset here - on failure it stays
            // toward the camera for the retry; success turns it back explicitly
            HidePanel();
        }

        /// <summary>Has the dog perform a named position (Sit / Down / Stand).</summary>
        private void SetDogPose(string position)
        {
            var animations = player?.Animations;
            if (animations == null)
                return;

            switch (position)
            {
                case "Sit":
                    animations.SetPose(DogPose.Sit);
                    break;
                case "Down":
                    animations.SetPose(DogPose.Down);
                    break;
                default:
                    animations.SetPose(DogPose.Stand);
                    break;
            }
        }

        private void ShowPanel()
        {
            if (gamePanel != null)
                gamePanel.SetActive(true);
        }

        private void HidePanel()
        {
            if (gamePanel != null)
                gamePanel.SetActive(false);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (sitButton != null) sitButton.interactable = interactable;
            if (downButton != null) downButton.interactable = interactable;
            if (standButton != null) standButton.interactable = interactable;
        }

        private void UpdateText(string text)
        {
            if (simonSaysText != null)
                simonSaysText.text = text;
        }

        private IEnumerator RunGame()
        {
            // Run through all rounds
            for (currentRound = 0; currentRound < sequenceLengths.Length; currentRound++)
            {
                Debug.Log($"[MiniLevelPositionsSimonSays] Starting round {currentRound + 1}");

                // Generate sequence for this round
                GenerateSequence(sequenceLengths[currentRound]);

                // Show the sequence
                yield return ShowSequenceCoroutine();

                // Wait for player input
                bool roundSuccess = false;
                yield return WaitForPlayerInput((success) => roundSuccess = success);

                if (!roundSuccess)
                {
                    // Player failed - game over
                    yield break;
                }

                // Show success feedback before next round
                if (currentRound < sequenceLengths.Length - 1)
                {
                    UpdateText("Correct!");
                    SetDogPose("Stand");
                    PlayClip(correctSound);
                    PunchPrompt();
                    yield return new WaitForSecondsRealtime(roundTransitionTime);
                }
            }

            // All rounds complete - success!
            Debug.Log("[MiniLevelPositionsSimonSays] All rounds complete!");
            UpdateText("Well Done!");
            SetDogPose("Stand");
            PlayClip(correctSound, 1.15f);
            PunchPrompt();

            // Success - turn back away from the camera before the next level
            player?.Animations?.SetFacing(false);

            yield return new WaitForSecondsRealtime(1f);

            HidePanel();
            CompleteGame();
        }

        private void GenerateSequence(int length)
        {
            currentSequence.Clear();

            for (int i = 0; i < length; i++)
            {
                int randomIndex = Random.Range(0, positions.Length);
                currentSequence.Add(positions[randomIndex]);
            }

            Debug.Log($"[MiniLevelPositionsSimonSays] Generated sequence: {string.Join(", ", currentSequence)}");
        }

        private IEnumerator ShowSequenceCoroutine()
        {
            currentPhase = GamePhase.Showing;
            SetButtonsInteractable(false);

            // Initial pause before showing
            UpdateText("Watch carefully");
            yield return new WaitForSecondsRealtime(1f);

            for (int i = 0; i < currentSequence.Count; i++)
            {
                // Show the position - the dog demonstrates it, its tone plays,
                // and the matching button wiggles so the mapping sinks in
                UpdateText(currentSequence[i]);
                SetDogPose(currentSequence[i]);
                PlayPoseTone(currentSequence[i]);
                PunchPrompt();
                var shownButton = ButtonFor(currentSequence[i]);
                if (shownButton != null)
                    Juice.PunchRotation(shownButton.transform, 8f, 0.35f);

                // Wait for display time
                yield return new WaitForSecondsRealtime(positionDisplayTime);

                // Show gap (blank or neutral text) if not the last position
                if (i < currentSequence.Count - 1)
                {
                    UpdateText("...");
                    yield return new WaitForSecondsRealtime(gapBetweenPositions);
                }
            }

            // Brief pause before input phase - dog back to neutral
            UpdateText("Your turn!");
            SetDogPose("Stand");
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private IEnumerator WaitForPlayerInput(System.Action<bool> onComplete)
        {
            currentPhase = GamePhase.Input;
            playerInputIndex = 0;
            SetButtonsInteractable(true);
            UpdateText("?");

            // Wait until all inputs received or failure
            while (playerInputIndex < currentSequence.Count && currentPhase == GamePhase.Input)
            {
                yield return null;
            }

            // Check if we completed successfully or failed
            bool success = playerInputIndex >= currentSequence.Count && currentPhase == GamePhase.Input;
            onComplete?.Invoke(success);
        }

        private void OnPositionButtonClicked(string position)
        {
            if (currentPhase != GamePhase.Input || isProcessingInput)
                return;

            Debug.Log($"[MiniLevelPositionsSimonSays] Button clicked: {position}");

            string expectedPosition = currentSequence[playerInputIndex];

            if (position == expectedPosition)
            {
                // Correct!
                StartCoroutine(HandleCorrectInput(position));
            }
            else
            {
                // Wrong!
                StartCoroutine(HandleIncorrectInput());
            }
        }

        private IEnumerator HandleCorrectInput(string position)
        {
            isProcessingInput = true;

            // Show feedback - the dog performs the commanded position
            UpdateText(position);
            SetDogPose(position);
            PlayPoseTone(position);
            PunchPrompt();
            yield return new WaitForSecondsRealtime(correctFeedbackTime);

            playerInputIndex++;

            // Check if sequence complete
            if (playerInputIndex >= currentSequence.Count)
            {
                // Round complete - the RunGame coroutine will handle the transition
                isProcessingInput = false;
            }
            else
            {
                // More inputs needed
                UpdateText("?");
                isProcessingInput = false;
            }
        }

        private IEnumerator HandleIncorrectInput()
        {
            isProcessingInput = true;
            currentPhase = GamePhase.Idle;
            SetButtonsInteractable(false);

            // Show incorrect feedback
            UpdateText("Incorrect!");
            PlayClip(wrongSound);
            ShakePrompt();
            yield return new WaitForSecondsRealtime(incorrectFeedbackTime);

            // Stop the game coroutine
            if (gameCoroutine != null)
            {
                StopCoroutine(gameCoroutine);
                gameCoroutine = null;
            }

            // Hide panel and trigger game over
            HidePanel();
            TriggerGameOver();
        }

        private void TriggerGameOver()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Triggering game over");
            GameManager.Instance?.TriggerMiniLevelGameOver();
        }
    }
}
