using Godot;
using Woad.Core.Commands;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Rummaging: clicking loot on or beside you takes it straight away when it's a single thing, and
/// otherwise (a bag of two or more, or an opened chest) opens the <see cref="RummagePanel"/> to take
/// what you want and put things in. Dropping or putting in part of a stack asks how many.
/// </summary>
public partial class GameScreen
{
    private RummagePanel _rummage = null!;
    private CountPrompt _countPrompt = null!;

    private void BuildLootWindows()
    {
        _rummage = new RummagePanel { Position = BoardOrigin + new Vector2(28, 24) };
        _rummage.TakeRequested += (cardId, count) =>
        {
            if (_rummage.Tile is { } tile)
            {
                ExecuteForActiveHero(heroId => new TakeLoot(heroId, tile, cardId, count));
            }
        };
        _rummage.TakeAllRequested += () =>
        {
            if (_rummage.Tile is { } tile)
            {
                ExecuteForActiveHero(heroId => new PickUpLoot(heroId, tile));
            }
        };
        _rummage.PutInRequested += PutIn;
        _rummage.CloseRequested += CloseRummage;
        AddChild(_rummage);

        _countPrompt = new CountPrompt { Position = BoardOrigin + new Vector2(44, 128) };
        AddChild(_countPrompt);
    }

    /// <summary>Everything on a tile, counting each copy in a stack, and gold, keys and an unlit bomb as one thing each.</summary>
    private static int ThingsAt(Room room, GridPosition tile) =>
        room.LootAt(tile).Sum(card => card.Count)
        + (room.GoldAt(tile) > 0 ? 1 : 0) + room.KeysAt(tile) + (room.BossKeyAt == tile ? 1 : 0)
        + (room.BombAt(tile) is { Lit: false } ? 1 : 0);

    /// <summary>Loot on or beside the hero: a single thing is taken at once; a bag or a chest opens to rummage through.</summary>
    private void Rummage(Hero hero, Room room, GridPosition tile)
    {
        if (room.Layout[tile] != TileKind.Chest && ThingsAt(room, tile) < 2)
        {
            Execute(new PickUpLoot(hero.Id, tile));
            return;
        }

        if (_session.CheckRummage(tile) is { } error)
        {
            Log(error, Palette.TextDim);
            return;
        }

        _rummage.ShowPile(room, tile, CanPutIn(hero));
    }

    private bool CanPutIn(Hero hero) => _selectedCardId is int id && hero.FindCard(id) is not null;

    /// <summary>Keeps the panel up to date after each command; it closes once the pile is out of reach or empty.</summary>
    private void RefreshRummage()
    {
        if (_rummage.Tile is not { } tile)
        {
            return;
        }

        if (ActiveHero is not { IsCompanion: false } hero || hero.RoomId != _shownRoomId || _session.CheckRummage(tile) is not null
            || (ShownRoom.Layout[tile] != TileKind.Chest && ThingsAt(ShownRoom, tile) == 0))
        {
            CloseRummage();
            return;
        }

        _rummage.ShowPile(ShownRoom, tile, CanPutIn(hero));
    }

    private void CloseRummage()
    {
        _rummage.Close();
        _countPrompt.Visible = false;
    }

    /// <summary>Puts the selected card into the pile (asking how many of a stack).</summary>
    private void PutIn()
    {
        if (_rummage.Tile is not { } tile || ActiveHero is not { } hero || _selectedCardId is not int cardId || hero.FindCard(cardId) is not var (card, _))
        {
            Log("Select a card in your belt or backpack first.", Palette.TextDim);
            return;
        }

        AskCount($"Put how many {card.Definition.Name} in?", card, count => ExecuteForActiveHero(heroId => new PutLoot(heroId, tile, cardId, count)));
    }

    /// <summary>Drops the card (asking how many of a stack).</summary>
    private void Drop(CardInstance card) =>
        AskCount($"Drop how many {card.Definition.Name}?", card, count => ExecuteForActiveHero(heroId => new DropCard(heroId, card.Id, count)));

    /// <summary>All of a single card at once; for a stack, asks how many first.</summary>
    private void AskCount(string question, CardInstance card, Action<int> then)
    {
        if (card.Count > 1)
        {
            _countPrompt.Ask(question, card.Count, then);
        }
        else
        {
            then(1);
        }
    }
}
