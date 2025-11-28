using UnityEngine;
using RingSport.Level;
using RingSport.UI;

namespace RingSport.Core
{
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Level Settings")]
        [SerializeField] private int maxLevels = 9;
        [SerializeField] private float levelDuration = 60f; // seconds per level

        private int currentLevel = 1;
        private int currentScore = 0;
        private float levelTimer = 0f;
        private float distanceTraveled = 0f;
        private bool levelActive = false;

        public int CurrentLevel => currentLevel;
        public int CurrentScore => currentScore;
        public float LevelProgress => Mathf.Clamp01(levelTimer / levelDuration);
        public float DistanceTraveled => distanceTraveled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            if (!levelActive) return;

            levelTimer += Time.deltaTime;

            if (levelTimer >= levelDuration)
            {
                EndLevel();
            }
        }

        public void StartLevel()
        {
            levelActive = true;
            levelTimer = 0f;
            distanceTraveled = 0f;
            currentScore = 0;

            LevelGenerator.Instance?.GenerateLevel(currentLevel);
        }

        public void EndLevel()
        {
            levelActive = false;

            if (currentLevel < maxLevels)
            {
                GameManager.Instance?.CompleteLevel();
                UIManager.Instance?.ShowRewardScreen(currentLevel, currentScore);
            }
            else
            {
                // All levels completed
                UIManager.Instance?.ShowRewardScreen(currentLevel, currentScore);
            }
        }

        public void AddScore(int points)
        {
            currentScore += points;
            UIManager.Instance?.UpdateScore(currentScore);
        }

        public void AddDistance(float distance)
        {
            distanceTraveled += distance;
        }

        public void NextLevel()
        {
            if (currentLevel < maxLevels)
            {
                currentLevel++;
                GameManager.Instance?.StartGame();
            }
            else
            {
                // Game complete
                GameManager.Instance?.ReturnToHome();
            }
        }

        public void ResetProgress()
        {
            currentLevel = 1;
            currentScore = 0;
            levelTimer = 0f;
            distanceTraveled = 0f;
            levelActive = false;
        }

        public float GetLevelDuration()
        {
            return levelDuration;
        }
    }
}
