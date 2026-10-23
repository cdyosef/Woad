using Woad.Core.Grid;

namespace Woad.Core.Model;

/// <summary>
/// A traveling merchant found chained up in a dungeon. Freed (with a key or a picked lock), they follow
/// whoever freed them, keep away from monsters, and trade with the party; brought out of the dungeon
/// alive, they're rescued. They never fight: one heart, and they only move.
/// </summary>
public sealed class Merchant
{
    internal Merchant(int id, string name, GridPosition position, int lockDifficulty, IEnumerable<string> stock)
    {
        Id = id;
        Name = name;
        Position = position;
        LockDifficulty = lockDifficulty;
        _stock = [.. stock];
    }

    private readonly List<string> _stock;

    public int Id { get; }

    public string Name { get; }

    public GridPosition Position { get; internal set; }

    public int Hearts { get; internal set; } = 1;

    /// <summary>Still chained to the wall: a key, or a picked lock (<see cref="LockDifficulty"/>), frees them.</summary>
    public bool Chained => FreedById is null;

    public int LockDifficulty { get; }

    /// <summary>The hero who freed them (and gets the most thanks), once they're free.</summary>
    public int? FreedById { get; internal set; }

    /// <summary>The hero they follow: whoever freed them, or another if that hero can't lead them any more.</summary>
    public int? LeaderId { get; internal set; }

    /// <summary>Card ids for sale.</summary>
    public IReadOnlyList<string> Stock => _stock;

    /// <summary>Someone has stolen from them already: they watch their wares now.</summary>
    public bool Robbed { get; internal set; }

    internal void AddStock(string cardId) => _stock.Add(cardId);

    internal bool RemoveStock(string cardId) => _stock.Remove(cardId);

    public override string ToString() => $"{Name} (merchant #{Id}) at {Position}";
}
