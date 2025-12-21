using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Level;
using RingSport.Core;
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

        private readonly string[] positions = { "Sit", "Down", "Stand" };

        private GamePhase currentPhase = GamePhase.Idle;
        private int currentRound = 0;
        private List<string> currentSequence = new List<string>();
        private int playerInputIndex = 0;
        private Coroutine gameCoroutine;
        private bool isProcessingInput = false;

        private void Start()
        {
            SetupButtons();
            HidePanel();
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

        public override void StartGame()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Starting game...");

            // Reset state
            currentRound = 0;
            currentSequence.Clear();
            playerInputIndex = 0;
            isProcessingInput = false;

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
            HidePanel();
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
                    yield return new WaitForSecondsRealtime(roundTransitionTime);
                }
            }

            // All rounds complete - success!
            Debug.Log("[MiniLevelPositionsSimonSays] All rounds complete!");
            UpdateText("Well Done!");
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
            UpdateText("Watch carefully...");
            yield return new WaitForSecondsRealtime(1f);

            for (int i = 0; i < currentSequence.Count; i++)
            {
                // Show the position
                UpdateText(currentSequence[i]);

                // Wait for display time
                yield return new WaitForSecondsRealtime(positionDisplayTime);

                // Show gap (blank or neutral text) if not the last position
                if (i < currentSequence.Count - 1)
                {
                    UpdateText("...");
                    yield return new WaitForSecondsRealtime(gapBetweenPositions);
                }
            }

            // Brief pause before input phase
            UpdateText("Your turn!");
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

            // Show feedback
            UpdateText(position);
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
