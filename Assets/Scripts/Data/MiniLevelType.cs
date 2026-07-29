namespace RingSport.Level
{
    // Values are explicit because they're serialized into LevelConfig assets
    // and the scene. DecoyBattle (3) was removed - never reuse its slot.
    public enum MiniLevelType
    {
        PositionsSimonSays = 0,
        FaceAttack = 1,
        FleeAttack = 2,
        FoodRefusal = 4,
        StopAttack = 5
    }
}
