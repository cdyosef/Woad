using Woad.Core.Content;
using Woad.Core.Randomness;

namespace Woad.Core.Model;

/// <summary>A theme's loot cards, shuffled and dealt from the top. Reshuffles a fresh deck when empty.</summary>
internal sealed class LootDeck(ThemeDefinition theme, ContentCatalog content, IRandomSource random)
{
    private readonly List<CardDefinition> _drawPile = [];

    public int Remaining => _drawPile.Count;

    public CardDefinition Draw()
    {
        if (_drawPile.Count == 0)
        {
            Refill();
        }

        CardDefinition top = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        return top;
    }

    private void Refill()
    {
        foreach (LootDeckEntry entry in theme.LootDeck)
        {
            _drawPile.AddRange(Enumerable.Repeat(content.GetCard(entry.Card), entry.Copies));
        }

        random.Shuffle(_drawPile);
    }
}
