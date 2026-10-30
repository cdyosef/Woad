namespace Woad.Core.Model;

/// <summary>A companion's standing order, set by its owner and kept until changed.</summary>
public enum CompanionOrder
{
    /// <summary>
    /// Stay close to the owner, through doors and down stairs; attack what comes within reach and go
    /// after monsters that close in on the owner.
    /// </summary>
    Follow,

    /// <summary>Stay on the tile where the order was given; attack what comes within reach.</summary>
    Hold,

    /// <summary>Go after the monsters in the room; follow the owner once the room is clear.</summary>
    Fight,

    /// <summary>Keep away from monsters while staying near the owner; strike back only when cornered.</summary>
    StayBack,

    /// <summary>
    /// Go to allies Keeling Over in the room, even mid-fight, and rouse them, and cure what ails the
    /// allies beside them; otherwise follow the owner. (Saved as "tend", its old name.)
    /// </summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("tend")]
    Aid,

    /// <summary>
    /// Find a way to the owner, wherever they are: like following, but through locked doors too (with a
    /// key, or by picking the lock) and down pits to a floor below. In the owner's room, follow.
    /// </summary>
    FindMe,

    /// <summary>Pick the locks in the room (doors, chests, gates; keys first) and open its chests; then follow.</summary>
    Burgle,

    /// <summary>Gather the room's loot: piles, opened and unlocked chests, pots; then follow.</summary>
    Loot,

    /// <summary>Search each room once for secrets (doors, traps, stashes); then follow.</summary>
    Explore,
}
