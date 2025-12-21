using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Level;
using RingSport.UI;
using System;

namespace RingSport.Core
{
    [Serializable]
    public class MiniLevelInfo
    {
        public MiniLevelType type;
        public string displayName;
        [TextArea(2, 4)]
        public string instructions;
    }

    /// <summary>
    /// Manages mini level state and UI. Has shared start panel and countdown.
    /// Coordinates with individual MiniLevelBase scripts for gameplay logic.
    /// After mini level completes, transitions directly to LevelComplete state.
    /// </summary>
    public class MiniLevelManager : MonoBehaviour
    {
        public static MiniLevelManager Instance { get; private set; }

        [Header("Mini Level Configurations")]
        [SerializeField] private MiniLevelInfo[] miniLevelConfigs = new MiniLevelInfo[]
        {
            new() { type = MiniLevelType.PositionsSimonSays, displayName = "Positions: Simon Says", instructions = "Follow the positions shown!" },
            new() { type = MiniLevelType.FaceAttack, displayName = "Face Attack", instructions = "Defend against the face attack!" },
            new() { type = MiniLevelType.FleeAttack, displayName = "Flee Attack", instructions = "Chase down the fleeing target!" },
            new() { type = MiniLevelType.DecoyBattle, displayName = "Decoy Battle", instructions = "Ignore the decoys, find the real target!" },
            new() { type = MiniLevelType.FoodRefusal, displayName = "Food Refusal", instructions = "Resist the temptation!" },
        };

        [Header("Start Panel")]
        [SerializeField] private GameObject startPanel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI instructionText;
        [SerializeField] private Button startButton;

        [Header("Countdown Settings")]
        [SerializeField] private float countdownDuration = 3f;

        private MiniLevelType currentMiniLevelType;
        private MiniLevelBase currentMiniLevelGame;
        private MiniLevelBase[] miniLevelGames;
        private bool isMiniLevelActive = false;

        public MiniLevelType CurrentMiniLevelType => currentMiniLevelType;
        public bool IsMiniLevelActive => isMiniLevelActive;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            HideStartPanel();
        }

        private void Start()
        {
            if (startButton != null)
                startButton.onClick.AddListener(OnStartButtonClicked);

            // Auto-discover mini level game scripts
            miniLevelGames = FindObjectsByType<MiniLevelBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[MiniLevelManager] Discovered {miniLevelGames.Length} mini level games");
        }

        private void HideStartPanel()
        {
            if (startPanel != null)
                startPanel.SetActive(false);
        }

        /// <summary>
        /// Starts a mini level of the specified type
        /// </summary>
        public void StartMiniLevel(MiniLevelType type)
        {
            // If already in an active mini-level, reset it first
            if (isMiniLevelActive && currentMiniLevelGame != null)
            {
                Debug.Log("[MiniLevelManager] Restarting mini-level - stopping current game first");
                currentMiniLevelGame.StopGame();
            }

            currentMiniLevelType = type;
            isMiniLevelActive = true;

            // Find the game script for this type
            currentMiniLevelGame = GetMiniLevelGame(type);

            Debug.Log($"[MiniLevelManager] Starting mini level: {type}");

            // Ensure game HUD is hidden during mini level
            UIManager.Instance?.HideGameHUD();

            // Set title and instructions from mini level config
            if (titleText != null)
                titleText.text = GetDisplayName(type);

            if (instructionText != null)
                instructionText.text = GetInstructions(type);

            // Show start panel
            if (startPanel != null)
                startPanel.SetActive(true);
        }

        private void OnStartButtonClicked()
        {
            Debug.Log("[MiniLevelManager] Start button clicked, beginning countdown");

            // Ensure game HUD is hidden during mini level
            UIManager.Instance?.HideGameHUD();

            // Hide start panel
            HideStartPanel();

            // Call prepare hook on the mini level (for camera setup, etc.)
            currentMiniLevelGame?.OnPrepareGame();

            // Use UIManager's shared countdown
            UIManager.Instance?.StartCountdown(countdownDuration, OnCountdownComplete);
        }

        private void OnCountdownComplete()
        {
            Debug.Log($"[MiniLevelManager] Countdown complete, currentMiniLevelGame: {(currentMiniLevelGame != null ? currentMiniLevelGame.name : "NULL")}");

            // Start the mini level game
            if (currentMiniLevelGame != null)
            {
                Debug.Log($"[MiniLevelManager] Starting mini level game: {currentMiniLevelGame.MiniLevelType}");
                currentMiniLevelGame.StartGame();
            }
            else
            {
                Debug.Log($"[MiniLevelManager] No game script found for {currentMiniLevelType}, showing reward panel immediately");
                OnMiniLevelGameComplete();
            }
        }

        /// <summary>
        /// Called by mini level game scripts when gameplay is complete
        /// </summary>
        public void OnMiniLevelGameComplete()
        {
            Debug.Log("[MiniLevelManager] OnMiniLevelGameComplete called, completing mini level");

            // Go directly to level complete (no separate mini level reward screen)
            CompleteMiniLevel();
        }

        /// <summary>
        /// Called when mini level is completed
        /// </summary>
        public void CompleteMiniLevel()
        {
            if (!isMiniLevelActive)
            {
                Debug.LogWarning("[MiniLevelManager] CompleteMiniLevel called but mini level is not active");
                return;
            }

            isMiniLevelActive = false;

            Debug.Log($"[MiniLevelManager] Mini level completed: {currentMiniLevelType}");

            // Stop current game if running
            if (currentMiniLevelGame != null)
                currentMiniLevelGame.StopGame();

            // Hide all mini level UI
            HideStartPanel();
            UIManager.Instance?.StopCountdown();

            currentMiniLevelGame = null;

            // Capture the level score BEFORE finalizing (so it's available for the reward screen)
            int levelScore = ScoreManager.Instance?.CurrentScore ?? 0;
            Debug.Log($"[MiniLevelManager] Captured level score before finalize: {levelScore}");

            // Finalize level score (stores it in best scores for the level)
            ScoreManager.Instance?.FinalizeLevelScore();

            // Transition to LevelComplete state which shows level reward screen
            GameManager.Instance?.CompleteMiniLevel();
        }

        /// <summary>
        /// Resets mini level state
        /// </summary>
        public void Reset()
        {
            if (currentMiniLevelGame != null)
                currentMiniLevelGame.StopGame();

            isMiniLevelActive = false;
            currentMiniLevelGame = null;
            HideStartPanel();
            UIManager.Instance?.StopCountdown();
        }

        /// <summary>
        /// Gets the mini level game script for the specified type
        /// </summary>
        private MiniLevelBase GetMiniLevelGame(MiniLevelType type)
        {
            if (miniLevelGames == null)
                return null;

            foreach (var game in miniLevelGames)
            {
                if (game != null && game.MiniLevelType == type)
                    return game;
            }

            return null;
        }

        /// <summary>
        /// Gets the config for a mini level type
        /// </summary>
        private MiniLevelInfo GetMiniLevelConfig(MiniLevelType type)
        {
            if (miniLevelConfigs == null)
                return null;

            foreach (var config in miniLevelConfigs)
            {
                if (config != null && config.type == type)
                    return config;
            }

            return null;
        }

        /// <summary>
        /// Gets display name for mini level type
        /// </summary>
        private string GetDisplayName(MiniLevelType type)
        {
            var config = GetMiniLevelConfig(type);
            return config != null ? config.displayName : type.ToString();
        }

        /// <summary>
        /// Gets instructions for mini level type
        /// </summary>
        private string GetInstructions(MiniLevelType type)
        {
            var config = GetMiniLevelConfig(type);
            return config != null ? config.instructions : "Complete the challenge!";
        }
    }
}
