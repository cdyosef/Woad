using Woad.Core.Grid;

namespace Woad.Core.Model;

/// <summary>
/// A bomb lying in a room. A lit one burns down on its owner's turns (whoever threw it or sprang the
/// trap it came from) and goes off at the end of their next turn. An unlit one is loot: pick it up as a
/// Bomb card. Either kind goes off at once when fire touches it or a blast catches it.
/// </summary>
public sealed class Bomb
{
    internal Bomb(int id, GridPosition position, bool lit, int? ownerId, int fuseTurns)
    {
        Id = id;
        Position = position;
        Lit = lit;
        OwnerId = ownerId;
        FuseTurns = fuseTurns;
    }

    /// <summary>Shared with heroes, monsters and cards, so every id in a run is unique.</summary>
    public int Id { get; }

    public GridPosition Position { get; internal set; }

    public bool Lit { get; internal set; }

    /// <summary>The hero or monster whose turns burn the fuse; null for an unlit bomb.</summary>
    public int? OwnerId { get; internal set; }

    /// <summary>Ends of the owner's turns left until it goes off: 1, at the end of their next (or current) turn.</summary>
    public int FuseTurns { get; internal set; }
}
