using Woad.Core.Content;
using Woad.Core.Randomness;

namespace Woad.Core.Model;

/// <summary>
/// A theme's loot cards, shuffled and dealt from the top. Cards meant for deeper floors are
/// passed over (and stay in the deck) until the heroes get there. Reshuffles a fresh deck when
/// nothing left fits the floor.
/// </summary>
internal sealed class LootDeck(ThemeDefinition theme, ContentCatalog content, IRandomSource random)
{
    private readonly List<(CardDefinition Card, int MinFloor)> _drawPile = [];

    public int Remaining => _drawPile.Count;

    public CardDefinition Draw(int floor)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int index = _drawPile.FindLastIndex(entry => entry.MinFloor <= floor);
            if (index >= 0)
            {
                CardDefinition card = _drawPile[index].Card;
                _drawPile.RemoveAt(index);
                return card;
            }

            Refill();
        }

        throw new InvalidOperationException($"Theme '{theme.Id}' has no loot for floor {floor}.");
    }

    private void Refill()
    {
        _drawPile.Clear();
        foreach (LootDeckEntry entry in theme.LootDeck)
        {
            _drawPile.AddRange(Enumerable.Repeat((content.GetCard(entry.Card), entry.MinFloor), entry.Copies));
        }

        random.Shuffle(_drawPile);
    }
}
