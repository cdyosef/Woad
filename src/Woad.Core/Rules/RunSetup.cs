using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;

namespace Woad.Core.Rules;

/// <summary>
/// A hero joining a run: a saved character's name, stats and the cards they bring in.
/// <paramref name="CharacterId"/> links the hero back to the character for saving results.
/// </summary>
public sealed record HeroSetup(string Name, Stats BaseStats, IReadOnlyList<string> CardIds, Guid? CharacterId = null, string Portrait = "");

/// <summary>One room of the dungeon and the rooms its doors lead to.</summary>
public sealed record RoomSetup(string Id, RoomTemplate Template, IReadOnlyDictionary<Direction, string>? Doors = null);

public sealed record RunSetup
{
    /// <summary>Decides every die, loot draw and monster choice; the same seed and commands replay the same run.</summary>
    public required ulong Seed { get; init; }

    public required string ThemeId { get; init; }

    /// <summary>Heroes in seat (turn) order.</summary>
    public required IReadOnlyList<HeroSetup> Heroes { get; init; }

    public required IReadOnlyList<RoomSetup> Rooms { get; init; }

    /// <summary>Where the heroes begin, on the template's <c>1</c>-<c>4</c> marks or near the center.</summary>
    public required string StartRoomId { get; init; }
}
