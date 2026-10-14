namespace Woad.Core.Model;

public enum TrapKind
{
    /// <summary>A floor tile that stabs whoever steps on it (1 heart). It stays armed until disarmed.</summary>
    Spikes,

    /// <summary>A floor tile that drops whoever steps on it into a room on the floor below (1 heart).</summary>
    TrapDoor,

    /// <summary>A poisoned needle in a chest's lock (1 heart when opened).</summary>
    Needle,

    /// <summary>Darts in a door frame (1 heart to the next hero through).</summary>
    Darts,
}

/// <summary>
/// A trap on a floor tile, chest or door. Hidden traps fire unseen; a search whose total reaches
/// <see cref="Difficulty"/> reveals them, and an Od Hand roll reaching it disarms them.
/// </summary>
public sealed class Trap(TrapKind kind, int difficulty)
{
    public TrapKind Kind { get; } = kind;

    public int Difficulty { get; } = difficulty;

    public bool Revealed { get; internal set; }
}

/// <summary>Something hidden in a room: keys or cards, found by a search (or by breaking the cracked wall it's behind).</summary>
public sealed class Cache(int difficulty, int keys, int cards, bool behindWall)
{
    /// <summary>Search total needed to find it.</summary>
    public int Difficulty { get; } = difficulty;

    public int Keys { get; } = keys;

    /// <summary>Cards drawn from the loot deck when it's found.</summary>
    public int Cards { get; } = cards;

    /// <summary>Sealed inside a cracked wall: only breaking the wall reveals it, not searching.</summary>
    public bool BehindWall { get; } = behindWall;
}
