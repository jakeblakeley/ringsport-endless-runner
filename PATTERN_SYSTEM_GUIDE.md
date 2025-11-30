# Obstacle Pattern System - Usage Guide

## Overview
The pattern-based generation system allows you to create hand-crafted, memorable obstacle sequences that mix with random generation for varied yet consistent gameplay.

## Quick Start

### 1. Generate Example Patterns
In Unity Editor:
1. Click **RingSport > Generate Example Obstacle Patterns** from the menu bar
2. This creates 14 example patterns in `Assets/Resources/Patterns/`
3. Patterns are automatically validated for solvability

### 2. Assign Patterns to LevelGenerator
1. Select the `LevelGenerator` GameObject in your scene
2. In the Inspector, find the **Pattern Library** section
3. Set the **Size** of the `Obstacle Patterns` array to 14 (or however many you want)
4. Drag the pattern assets from `Assets/Resources/Patterns/` into the array slots
5. Adjust **Pattern Usage Ratio** (default: 0.7 = 70% patterns, 30% random)

### 3. Test Patterns
- Run the game and observe logs for pattern spawning
- You'll see messages like: "Spawning pattern: Easy Zigzag (difficulty 2)"
- Patterns will automatically be selected based on current level

## Pattern Categories

### Easy Patterns (Levels 1-4, Difficulty 1-3)
- **Easy Straight Line**: Three jumps in center lane - teaches jumping
- **Easy Zigzag**: Left → Center → Right jumps - teaches lane switching
- **Easy Alternate**: Left → Right → Left jumps - basic rhythm
- **Easy Gap**: Two avoid obstacles with gap in center - teaches dodging

### Medium Patterns (Levels 3-7, Difficulty 4-6)
- **Medium Double Jump**: Two quick jumps followed by avoid - timing practice
- **Medium Slalom**: Four pylons in alternating lanes - fluid lane switching
- **Medium Mixed Row**: 3-lane row with mixed obstacle types (1 passable)
- **Medium Palisade Intro**: Jump followed by palisade - introduces minigame

### Hard Patterns (Levels 6-9, Difficulty 7-8)
- **Hard Rapid Fire**: Five jumps in quick succession across lanes - speed test
- **Hard Triple Row**: 3-lane mixed row + follow-up obstacle - complex decision making
- **Hard Narrow Window**: Pylons force narrow path with jumps - precision required
- **Hard Broad Jump Challenge**: Mix of broad jumps and regular jumps - varied mechanics

### Expert Patterns (Levels 7-9, Difficulty 9-10)
- **Expert Gauntlet**: 8-obstacle sequence requiring multiple mechanics
- **Expert Palisade Gauntlet**: Palisade followed by tight obstacles (uses recovery zone)

## Creating Custom Patterns

### In Unity Editor:
1. Right-click in Project window
2. Select **Create > RingSport > Obstacle Pattern**
3. Configure the pattern:

#### Basic Info:
- **Pattern Name**: Descriptive name (e.g., "Medium Slalom")
- **Difficulty Rating**: 1-10 (affects when it appears)
- **Min/Max Level**: Which levels this pattern can appear in (1-9)
- **Pattern Length**: Total length in units (determines next spawn position)

#### Obstacles Array:
For each obstacle, specify:
- **Obstacle Type**:
  - `ObstacleJump` - Can be jumped over (passable)
  - `ObstacleAvoid` - Instant death, must dodge
  - `ObstaclePalisade` - Minigame (passable, triggers recovery zone)
  - `ObstaclePylon` - Instant death, must dodge
  - `ObstacleBroadJump` - Long jump required (passable)

- **Lane**: -1 (left), 0 (center), 1 (right)
- **Z Offset**: Distance from pattern start position

### Example Pattern Configuration:
```
Pattern Name: "Medium Zigzag Fast"
Difficulty: 5
Min Level: 4
Max Level: 7
Pattern Length: 20

Obstacles:
  [0] Type: ObstacleJump, Lane: -1, Z Offset: 0
  [1] Type: ObstacleJump, Lane: 0, Z Offset: 6
  [2] Type: ObstacleJump, Lane: 1, Z Offset: 12
```

## Pattern Design Best Practices

### 1. Ensure Solvability
- **At least one passable obstacle in 3-lane rows**
  - Good: Avoid + Jump + Pylon (Jump is passable)
  - Bad: Avoid + Pylon + Avoid (all instant death)
- Patterns are auto-validated on creation
- Unsolvable patterns will show warnings and won't spawn

### 2. Appropriate Difficulty Progression
- Levels 1-3: Single mechanics, wide spacing (8-10 units)
- Levels 4-6: Combined mechanics, medium spacing (5-8 units)
- Levels 7-9: Complex sequences, tight spacing (4-6 units)

### 3. Pattern Length Guidelines
- Short patterns (15-20 units): Quick, focused challenges
- Medium patterns (20-30 units): Standard sequences
- Long patterns (30-40 units): Epic gauntlet challenges

### 4. Lane Change Considerations
- Allow ~8 units for comfortable lane changes at normal speed
- Reduce to ~5 units for hard difficulty
- Never require more than 2 lane changes in <10 units

### 5. Palisade Patterns
- Always include recovery space after palisades
- The system automatically creates a 15-unit clear zone after palisade completion
- Don't place obstacles within 15 units after a palisade in your pattern

### 6. Coin Placement Synergy
- Patterns don't spawn coins directly
- Collectible spawner will intelligently place coins:
  - Above jumps and palisades
  - In clear lanes during multi-lane obstacles
  - Following obstacle patterns

## Tuning Pattern Mix

Adjust in LevelGenerator Inspector:
- **Pattern Usage Ratio: 1.0** = 100% patterns (very predictable)
- **Pattern Usage Ratio: 0.7** = 70% patterns, 30% random (recommended)
- **Pattern Usage Ratio: 0.5** = 50/50 mix (balanced)
- **Pattern Usage Ratio: 0.3** = Mostly random with pattern accents
- **Pattern Usage Ratio: 0.0** = Pure random generation (original behavior)

## Debugging Patterns

### Console Logs:
- `"Spawning pattern: [name] (difficulty [X])"` = Pattern spawned successfully
- `"Pattern '[name]' failed clearance check"` = Pattern too close to previous obstacles
- `"Pattern '[name]' is not solvable"` = Pattern has no valid path (design error)
- `"No valid patterns found for level X"` = No patterns match current level
- `"Pattern spawn failed, using random generation as fallback"` = Clearance issue, random used instead

### Testing Individual Patterns:
1. Set Pattern Usage Ratio to 1.0 (100% patterns)
2. Assign only the pattern you want to test
3. Set its Min/Max Level to match your test level
4. Run the game and observe

## Pattern Performance

- Patterns have negligible performance impact
- Clearance checking is O(n) where n = obstacles in tracking list
- List is aggressively cleaned to stay small (typically <50 items)
- Pattern validation happens once at level start, not per-spawn

## Advanced: Procedural Patterns

You can create patterns programmatically:
```csharp
var pattern = ScriptableObject.CreateInstance<ObstaclePattern>();
pattern.patternName = "My Custom Pattern";
pattern.difficultyRating = 5;
pattern.minLevel = 3;
pattern.maxLevel = 7;
pattern.patternLength = 25f;
pattern.obstacles = new ObstacleDefinition[]
{
    new ObstacleDefinition("ObstacleJump", -1, 0f),
    new ObstacleDefinition("ObstacleJump", 0, 8f),
    new ObstacleDefinition("ObstacleJump", 1, 16f)
};

// Validate before use
if (!pattern.IsSolvable())
{
    Debug.LogError("Pattern is not solvable!");
}
```

## Tips for Level Designers

1. **Start Simple**: Use the generated patterns as templates
2. **Playtest Often**: What looks good on paper may feel unfair
3. **Mind the Speed**: Higher levels have speed multipliers - account for this
4. **Create Signatures**: Give each level a "signature pattern" players remember
5. **Balance Challenge**: Mix easy breathers with hard challenges
6. **Use Themes**: Group patterns by mechanic (jump-heavy, dodge-heavy, etc.)
7. **Difficulty Curves**: Gradually increase pattern difficulty within a level

## Troubleshooting

**Q: Patterns never spawn**
- A: Check Pattern Usage Ratio > 0 and patterns are assigned in Inspector

**Q: Same pattern repeats constantly**
- A: Add more patterns for the current level range

**Q: Pattern clearance failures**
- A: Increase pattern length or reduce obstacle density in patterns

**Q: Player can't complete pattern**
- A: Check lane change spacing (needs ~8 units at normal speed)

**Q: Warnings about unsolvable patterns**
- A: Ensure 3-lane rows have at least one passable obstacle type
