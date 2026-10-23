using Woad.Core.Content;

namespace Woad.Core.Model;

/// <summary>
/// One physical card in a run, or a stack of copies of a stackable card (see
/// <see cref="CardDefinition.IsStackable"/>): one card with a <see cref="Count"/>.
/// </summary>
public sealed class CardInstance
{
    internal CardInstance(int id, CardDefinition definition, int? broughtInByHeroId, int count = 1)
    {
        Id = id;
        Definition = definition;
        ChargesRemaining = definition.Charges;
        BroughtInByHeroId = broughtInByHeroId;
        Count = count;
        BroughtInCount = broughtInByHeroId is null ? 0 : count;
    }

    /// <summary>How many copies this is: 1, or more for a stack.</summary>
    public int Count { get; internal set; }

    /// <summary>How many of the stack <see cref="BroughtInByHeroId"/> brought into the run; the rest were found.</summary>
    public int BroughtInCount { get; internal set; }

    /// <summary>The copies found in the dungeon, not brought in by <paramref name="heroId"/>.</summary>
    public int FoundBy(int heroId) => Count - (BroughtInByHeroId == heroId ? BroughtInCount : 0);

    public int Id { get; }

    public CardDefinition Definition { get; }

    public int ChargesRemaining { get; internal set; }

    /// <summary>A crossbow that's been shot and not yet reloaded. Every crossbow is loaded again on reaching a new floor.</summary>
    public bool Unloaded { get; internal set; }

    /// <summary>
    /// The hero who brought this card into the run, or null if it was found in the dungeon.
    /// A downed hero keeps only the cards they brought in.
    /// </summary>
    public int? BroughtInByHeroId { get; private set; }

    /// <summary>Gathers another stack of the same card into this one.</summary>
    internal void Absorb(CardInstance other)
    {
        if (other.BroughtInByHeroId is { } owner && BroughtInByHeroId is null)
        {
            BroughtInByHeroId = owner;
            BroughtInCount = other.BroughtInCount;
        }
        else if (other.BroughtInByHeroId == BroughtInByHeroId)
        {
            BroughtInCount += other.BroughtInCount;
        }

        Count += other.Count;
    }

    /// <summary>Takes <paramref name="count"/> copies off this stack as a new card (found ones first).</summary>
    internal CardInstance Split(int newId, int count)
    {
        int found = Count - BroughtInCount;
        int brought = Math.Max(0, count - found);
        Count -= count;
        BroughtInCount -= brought;
        return new CardInstance(newId, Definition, brought > 0 ? BroughtInByHeroId : null, count) { BroughtInCount = brought };
    }

    public override string ToString() => Count > 1 ? $"{Definition.Name} x{Count} #{Id}" : $"{Definition.Name} #{Id}";
}
