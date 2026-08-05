namespace RingSport.Core
{
    /// <summary>
    /// Flags consumed by gameplay code during automated runs (perf harness).
    /// Lives outside the DEVELOPMENT_BUILD guard so callers compile in release;
    /// nothing sets it there, so it is dead-code-stripped to a few bytes.
    /// </summary>
    public static class PerfFlags
    {
        /// <summary>Obstacle hits are ignored entirely (no death, no palisade minigame).</summary>
        public static bool Invincible;
    }
}
