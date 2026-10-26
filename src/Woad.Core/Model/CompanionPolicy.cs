namespace Woad.Core.Model;

/// <summary>
/// How a companion manages its own things on its turns, whatever its order: set by its owner from the
/// Party menu, on the Woarld Map or in a dungeon, and kept with the character.
/// </summary>
public sealed record CompanionPolicy
{
    public static CompanionPolicy Default { get; } = new();

    /// <summary>Drinks a healing potion from its belt when it's badly hurt.</summary>
    public bool DrinksPotions { get; init; } = true;

    /// <summary>Picks up loot it can reach while there's no fight, whatever its order.</summary>
    public bool PicksUpLoot { get; init; }

    /// <summary>Equips gear it carries that's better than what it has on (worth more), while there's no fight.</summary>
    public bool EquipsBetterGear { get; init; }
}
