using System.Text.Json.Serialization;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Core.Saves;

/// <summary>
/// The Party Chest: cards and coins the whole Raiding Party shares, reached from the Party menu anywhere
/// out of a dungeon, and able to pay for things itself. It never goes into a dungeon. Saved with the raid.
/// </summary>
public sealed class PartyChest : IPurse
{
    [JsonIgnore]
    public string Name => "the Party Chest";

    /// <summary>The cards in it, by key.</summary>
    public List<string> Cards { get; init; } = [];

    /// <summary>The coins in it, in copper.</summary>
    public int Coins { get; set; }

    public bool Spend(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount > Coins)
        {
            return false;
        }

        Coins -= amount;
        return true;
    }

    public void Earn(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Coins += amount;
    }

    /// <summary>A character puts a card (by key) in, from wherever they keep it (a worn belt goes in packed whole). Why not, or null.</summary>
    public string? Put(Character from, string key, CardPlace place, ContentCatalog content)
    {
        if (from.TakeFrom(place, key, content) is not { } taken)
        {
            return $"{from.Name} hasn't got that.";
        }

        Cards.Add(taken);
        return null;
    }

    /// <summary>A character takes a card (by key) out: it goes where it fits (worn if its slot is free, else belt or pack). Why not, or null.</summary>
    public string? Take(Character to, string key, ContentCatalog content)
    {
        if (!Cards.Remove(key))
        {
            return "That isn't in the chest.";
        }

        to.GiveCard(key, content);
        return null;
    }

    /// <summary>Moves coins (copper) from one purse to another: into the chest, or out of it. Why not, or null.</summary>
    public static string? Transfer(IPurse from, IPurse to, int amount)
    {
        if (amount < 1)
        {
            return "Nothing to move.";
        }

        if (!from.Spend(amount))
        {
            return $"{Capitalized(from.Name)} has only {Money.Long(from.Coins)}.";
        }

        to.Earn(amount);
        return null;
    }

    private static string Capitalized(string name) => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
}
