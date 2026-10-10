using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Rules;

public static class Targeting
{
    /// <summary>Whether an attacker on <paramref name="from"/> can hit <paramref name="to"/>.</summary>
    public static bool CanAttack(RoomLayout layout, AttackPattern pattern, int range, GridPosition from, GridPosition to)
    {
        int distance = from.ManhattanDistanceTo(to);
        if (distance < 1 || distance > range)
        {
            return false;
        }

        return pattern switch
        {
            AttackPattern.Melee => distance == 1,
            AttackPattern.Reach => (from.X == to.X || from.Y == to.Y) && LineOfSight.IsClear(layout, from, to),
            AttackPattern.Ranged => LineOfSight.IsClear(layout, from, to),
            _ => false,
        };
    }

    /// <summary>Heroes attack in melee unless their weapon reaches further, which makes it a ranged attack.</summary>
    public static AttackPattern HeroPattern(int attackRange) => attackRange > 1 ? AttackPattern.Ranged : AttackPattern.Melee;

    /// <summary>Card targets must be within range and in sight. Range 0 means your own tile.</summary>
    public static bool InRangeAndSight(RoomLayout layout, GridPosition from, GridPosition to, int range) =>
        to.IsInRoom && from.ManhattanDistanceTo(to) <= range && LineOfSight.IsClear(layout, from, to);
}
