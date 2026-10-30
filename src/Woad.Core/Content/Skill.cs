namespace Woad.Core.Content;

/// <summary>
/// What a hero has learned to do. Od Hand checks need their skill to roll Od Hand dice on them; without
/// it a tool can stand in, rolling only its own dice (Lockpicks on a lock, a lit torch on a search).
/// Rumble skills unlock the moves themselves. Bought with points (see <c>PointBuy</c>).
/// </summary>
[Flags]
public enum Skill
{
    None = 0,

    // Od Hand.
    Search = 1 << 0,
    Steal = 1 << 1,
    PickLock = 1 << 2,
    Disarm = 1 << 3,

    /// <summary>Rouse the fallen and cure what ails you or an ally beside you.</summary>
    Aid = 1 << 4,

    /// <summary>An Od Hand die, rolled now and held, added to your next defense before your next turn.</summary>
    Parry = 1 << 5,

    // Rumble.
    Shove = 1 << 6,

    /// <summary>Charge in a straight line with a Wend die (sixes roll more), the tiles left over added to a Rumble attack.</summary>
    BullRush = 1 << 7,

    /// <summary>A Rumble die, rolled now and held, added to your next defense or an ally's beside you.</summary>
    Guard = 1 << 8,

    /// <summary>A Rumble die added to your next search, or rolled to search on its own.</summary>
    HighAlert = 1 << 9,
}

public static class Skills
{
    public const Skill OdHand = Skill.Search | Skill.Steal | Skill.PickLock | Skill.Disarm | Skill.Aid | Skill.Parry;

    public const Skill Rumble = Skill.Shove | Skill.BullRush | Skill.Guard | Skill.HighAlert;

    public const Skill All = OdHand | Rumble;

    public static IReadOnlyList<Skill> Each { get; } = [.. Enum.GetValues<Skill>().Where(skill => skill != Skill.None)];

    public static IEnumerable<Skill> In(Skill set) => Each.Where(skill => set.HasFlag(skill));

    public static int Count(Skill set) => In(set).Count();

    public static string Name(this Skill skill) => skill switch
    {
        Skill.PickLock => "Pick Lock",
        Skill.BullRush => "Bull Rush",
        Skill.HighAlert => "High Alert",
        _ => skill.ToString(),
    };
}

/// <summary>
/// A tool's help on Od Hand checks: <see cref="Bonus"/> and <see cref="Dice"/> (a 6 rolls another) on
/// one check a turn of the skills it serves. Without the skill, the tool alone makes the roll.
/// </summary>
public sealed record ToolUse
{
    public required Skill For { get; init; }

    public int Bonus { get; init; }

    public int Dice { get; init; }

    /// <summary>A failed roll it helped, with a 1 among the dice, uses one up (a snapped lockpick).</summary>
    public bool Snaps { get; init; }
}
