namespace RingSport.Core
{
    /// <summary>
    /// Constants for object pool tags to ensure type-safety and prevent typos
    /// </summary>
    public static class PoolTags
    {
        // Obstacle pool tags
        public const string ObstacleAvoid = "ObstacleAvoid";
        public const string ObstacleJump = "ObstacleJump";
        public const string ObstaclePalisade = "ObstaclePalisade";
        public const string ObstaclePylon = "ObstaclePylon";
        public const string ObstacleBroadJump = "ObstacleBroadJump";

        // Mini-level specific pool tags
        public const string FoodRefusalSteak = "FoodRefusalSteak";
        public const string FoodRefusalCollectible = "FoodRefusalCollectible";

        // Collectible pool tags
        public const string Collectible = "Collectible";
        public const string MegaCollectible = "MegaCollectible";
        public const string LoveNote = "LoveNote";

        // Floor pool tags
        public const string FloorTile = "FloorTile";
    }
}
