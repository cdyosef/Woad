using Woad.Core.Characters;
using Woad.Core.Model;
using Woad.Core.Tavern;

namespace Woad.Scenes.Woarld;

/// <summary>
/// A shop as the Party menu shows it: its wares in a column beside the party's, what it asks each
/// shopper (a character, or the Party Chest), and, where it does, what it pays for a card and what
/// mending costs. Anyone in the party can buy, sell or have things mended, without changing the leader.
/// </summary>
public sealed class ShopFront
{
    public required string Name { get; init; }

    /// <summary>A line under its name (the Guild's level, the smith's rates).</summary>
    public string Note { get; init; } = "";

    /// <summary>Its wares by card id, a copy each.</summary>
    public required Func<IReadOnlyList<string>> Wares { get; init; }

    /// <summary>What it asks a shopper for a ware, in copper.</summary>
    public required Func<string, Shopper, int> Price { get; init; }

    public required Func<string, Shopper, ServiceResult> Buy { get; init; }

    /// <summary>What it pays for a card (by key), in copper; null if it won't buy it. Null: it buys nothing.</summary>
    public Func<string, int?>? Pays { get; init; }

    /// <summary>Sells it a card: the purse paid, and how to take the card from whoever has it.</summary>
    public Func<string, IPurse, Func<bool>, ServiceResult>? Sell { get; init; }

    /// <summary>What mending a worn card (by key) costs, in copper; null where nothing's mended.</summary>
    public Func<string, int>? MendPrice { get; init; }

    /// <summary>Mends one of a character's worn cards, paid from a purse.</summary>
    public Func<Character, string, IPurse, ServiceResult>? Mend { get; init; }

    /// <summary>What mending a card takes instead of gold ("2 Emeralds"), or null for gold.</summary>
    public Func<string, string?>? MendWith { get; init; }

    /// <summary>What making a card finer takes, said ("Make it the Emerald Axe +3: 3 Emeralds and 3g"), or null if it can't be.</summary>
    public Func<string, string?>? Improvement { get; init; }

    /// <summary>Makes one of a character's cards finer, the gold from a purse.</summary>
    public Func<Character, string, IPurse, ServiceResult>? Improve { get; init; }
}
