namespace Woad.Core.Characters;

/// <summary>Levels come from XP; each level grants points to spend on stats.</summary>
public static class Progression
{
    public const int PointsPerLevel = 1;

    /// <summary>Total XP needed to reach a level: 0, 10, 30, 60, 100, ... (10 times the triangular numbers).</summary>
    public static int XpForLevel(int level) => level <= 1 ? 0 : 10 * (level - 1) * level / 2;

    public static int LevelForXp(int xp)
    {
        int level = 1;
        while (xp >= XpForLevel(level + 1))
        {
            level++;
        }

        return level;
    }
}
