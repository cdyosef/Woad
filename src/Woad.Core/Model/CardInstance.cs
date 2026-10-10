using Woad.Core.Content;

namespace Woad.Core.Model;

/// <summary>One physical card in a run.</summary>
public sealed class CardInstance
{
    internal CardInstance(int id, CardDefinition definition, int? broughtInByHeroId)
    {
        Id = id;
        Definition = definition;
        ChargesRemaining = definition.Charges;
        BroughtInByHeroId = broughtInByHeroId;
    }

    public int Id { get; }

    public CardDefinition Definition { get; }

    public int ChargesRemaining { get; internal set; }

    /// <summary>
    /// The hero who brought this card into the run, or null if it was found in the dungeon.
    /// A downed hero keeps only the cards they brought in.
    /// </summary>
    public int? BroughtInByHeroId { get; }

    public override string ToString() => $"{Definition.Name} #{Id}";
}
