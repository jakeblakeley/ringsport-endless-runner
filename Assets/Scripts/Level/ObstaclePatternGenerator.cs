using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RingSport.Level
{
    /// <summary>
    /// Helper class to create example obstacle patterns
    /// This class can be used to programmatically generate pattern assets in the Unity editor
    /// </summary>
    public class ObstaclePatternGenerator
    {
#if UNITY_EDITOR
        /// <summary>
        /// Create all example patterns and save them as ScriptableObject assets
        /// Call this from a MenuItem or custom editor window
        /// </summary>
        [MenuItem("RingSport/Generate Example Obstacle Patterns")]
        public static void GenerateExamplePatterns()
        {
            string folderPath = "Assets/Resources/Patterns";

            // Create folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Patterns");
            }

            // Easy Patterns (Levels 1-3)
            CreatePattern_EasyZigzag(folderPath);
            CreatePattern_EasyStraightLine(folderPath);
            CreatePattern_EasyAlternate(folderPath);
            CreatePattern_EasyGap(folderPath);

            // Medium Patterns (Levels 3-6)
            CreatePattern_MediumDoubleJump(folderPath);
            CreatePattern_MediumSlalom(folderPath);
            CreatePattern_MediumMixedRow(folderPath);
            CreatePattern_MediumPalisadeIntro(folderPath);

            // Hard Patterns (Levels 6-9)
            CreatePattern_HardRapidFire(folderPath);
            CreatePattern_HardTripleRow(folderPath);
            CreatePattern_HardNarrowWindow(folderPath);
            CreatePattern_HardBroadJumpChallenge(folderPath);

            // Expert Patterns (Levels 7-9)
            CreatePattern_ExpertGauntlet(folderPath);
            CreatePattern_ExpertPalisadeGauntlet(folderPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Generated 14 example obstacle patterns in {folderPath}");
        }

        // ===== EASY PATTERNS (Difficulty 1-3) =====

        private static void CreatePattern_EasyZigzag(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Easy Zigzag";
            pattern.difficultyRating = 2;
            pattern.minLevel = 1;
            pattern.maxLevel = 4;
            pattern.patternLength = 25f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleJump", -1, 0f),
                new ObstacleDefinition("ObstacleJump", 0, 8f),
                new ObstacleDefinition("ObstacleJump", 1, 16f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Easy_Zigzag.asset");
        }

        private static void CreatePattern_EasyStraightLine(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Easy Straight Line";
            pattern.difficultyRating = 1;
            pattern.minLevel = 1;
            pattern.maxLevel = 3;
            pattern.patternLength = 30f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleJump", 0, 0f),
                new ObstacleDefinition("ObstacleJump", 0, 10f),
                new ObstacleDefinition("ObstacleJump", 0, 20f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Easy_StraightLine.asset");
        }

        private static void CreatePattern_EasyAlternate(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Easy Alternate";
            pattern.difficultyRating = 2;
            pattern.minLevel = 1;
            pattern.maxLevel = 4;
            pattern.patternLength = 28f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleJump", -1, 0f),
                new ObstacleDefinition("ObstacleJump", 1, 10f),
                new ObstacleDefinition("ObstacleJump", -1, 20f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Easy_Alternate.asset");
        }

        private static void CreatePattern_EasyGap(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Easy Gap";
            pattern.difficultyRating = 3;
            pattern.minLevel = 2;
            pattern.maxLevel = 5;
            pattern.patternLength = 20f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleAvoid", -1, 0f),
                new ObstacleDefinition("ObstacleAvoid", 1, 0f),
                // Gap in center lane for player to move through
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Easy_Gap.asset");
        }

        // ===== MEDIUM PATTERNS (Difficulty 4-6) =====

        private static void CreatePattern_MediumDoubleJump(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Medium Double Jump";
            pattern.difficultyRating = 4;
            pattern.minLevel = 3;
            pattern.maxLevel = 6;
            pattern.patternLength = 18f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleJump", 0, 0f),
                new ObstacleDefinition("ObstacleJump", 0, 5f),
                new ObstacleDefinition("ObstacleAvoid", 0, 12f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Medium_DoubleJump.asset");
        }

        private static void CreatePattern_MediumSlalom(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Medium Slalom";
            pattern.difficultyRating = 5;
            pattern.minLevel = 3;
            pattern.maxLevel = 7;
            pattern.patternLength = 30f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstaclePylon", -1, 0f),
                new ObstacleDefinition("ObstaclePylon", 1, 7f),
                new ObstacleDefinition("ObstaclePylon", -1, 14f),
                new ObstacleDefinition("ObstaclePylon", 1, 21f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Medium_Slalom.asset");
        }

        private static void CreatePattern_MediumMixedRow(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Medium Mixed Row";
            pattern.difficultyRating = 5;
            pattern.minLevel = 4;
            pattern.maxLevel = 7;
            pattern.patternLength = 15f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleAvoid", -1, 0f),
                new ObstacleDefinition("ObstacleJump", 0, 0f),  // Passable
                new ObstacleDefinition("ObstaclePylon", 1, 0f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Medium_MixedRow.asset");
        }

        private static void CreatePattern_MediumPalisadeIntro(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Medium Palisade Intro";
            pattern.difficultyRating = 6;
            pattern.minLevel = 4;
            pattern.maxLevel = 8;
            pattern.patternLength = 20f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleJump", 0, 0f),
                new ObstacleDefinition("ObstaclePalisade", 0, 10f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Medium_PalisadeIntro.asset");
        }

        // ===== HARD PATTERNS (Difficulty 7-8) =====

        private static void CreatePattern_HardRapidFire(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Hard Rapid Fire";
            pattern.difficultyRating = 7;
            pattern.minLevel = 6;
            pattern.maxLevel = 9;
            pattern.patternLength = 25f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleJump", -1, 0f),
                new ObstacleDefinition("ObstacleJump", 0, 5f),
                new ObstacleDefinition("ObstacleJump", 1, 10f),
                new ObstacleDefinition("ObstacleJump", 0, 15f),
                new ObstacleDefinition("ObstacleJump", -1, 20f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Hard_RapidFire.asset");
        }

        private static void CreatePattern_HardTripleRow(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Hard Triple Row";
            pattern.difficultyRating = 7;
            pattern.minLevel = 6;
            pattern.maxLevel = 9;
            pattern.patternLength = 20f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                // First row - mixed with passable center
                new ObstacleDefinition("ObstacleAvoid", -1, 0f),
                new ObstacleDefinition("ObstacleJump", 0, 0f),  // Passable
                new ObstacleDefinition("ObstacleAvoid", 1, 0f),
                // Second obstacle after gap
                new ObstacleDefinition("ObstaclePylon", 0, 12f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Hard_TripleRow.asset");
        }

        private static void CreatePattern_HardNarrowWindow(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Hard Narrow Window";
            pattern.difficultyRating = 8;
            pattern.minLevel = 7;
            pattern.maxLevel = 9;
            pattern.patternLength = 22f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstaclePylon", -1, 0f),
                new ObstacleDefinition("ObstaclePylon", 1, 0f),
                // Narrow window in center at Z=0
                new ObstacleDefinition("ObstacleJump", 0, 8f),
                new ObstacleDefinition("ObstaclePylon", -1, 15f),
                new ObstacleDefinition("ObstaclePylon", 1, 15f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Hard_NarrowWindow.asset");
        }

        private static void CreatePattern_HardBroadJumpChallenge(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Hard Broad Jump Challenge";
            pattern.difficultyRating = 8;
            pattern.minLevel = 6;
            pattern.maxLevel = 9;
            pattern.patternLength = 25f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleBroadJump", 0, 0f),
                new ObstacleDefinition("ObstacleJump", -1, 10f),
                new ObstacleDefinition("ObstacleBroadJump", 1, 15f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Hard_BroadJumpChallenge.asset");
        }

        // ===== EXPERT PATTERNS (Difficulty 9-10) =====

        private static void CreatePattern_ExpertGauntlet(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Expert Gauntlet";
            pattern.difficultyRating = 9;
            pattern.minLevel = 7;
            pattern.maxLevel = 9;
            pattern.patternLength = 35f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstacleAvoid", -1, 0f),
                new ObstacleDefinition("ObstacleAvoid", 1, 0f),
                // Gap at center Z=0
                new ObstacleDefinition("ObstacleJump", 0, 8f),
                new ObstacleDefinition("ObstacleJump", 0, 13f),
                new ObstacleDefinition("ObstaclePalisade", -1, 20f),  // Passable
                new ObstacleDefinition("ObstacleAvoid", 0, 20f),
                new ObstacleDefinition("ObstacleAvoid", 1, 20f),
                new ObstacleDefinition("ObstaclePylon", 0, 28f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Expert_Gauntlet.asset");
        }

        private static void CreatePattern_ExpertPalisadeGauntlet(string folderPath)
        {
            var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
            pattern.patternName = "Expert Palisade Gauntlet";
            pattern.difficultyRating = 10;
            pattern.minLevel = 8;
            pattern.maxLevel = 9;
            pattern.patternLength = 30f;
            pattern.obstacles = new ObstacleDefinition[]
            {
                new ObstacleDefinition("ObstaclePalisade", 0, 0f),  // Passable via minigame
                new ObstacleDefinition("ObstaclePylon", -1, 8f),
                new ObstacleDefinition("ObstaclePylon", 1, 8f),
                // Note: Recovery zone will trigger after palisade
                new ObstacleDefinition("ObstacleJump", 0, 22f)
            };

            AssetDatabase.CreateAsset(pattern, $"{folderPath}/Pattern_Expert_PalisadeGauntlet.asset");
        }
#endif
    }
}
