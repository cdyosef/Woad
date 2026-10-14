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
}
