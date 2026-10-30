using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The turn order along the top (see <see cref="TurnOrderView"/>), and looking over another hero: click
/// their card and the view goes to them, their sheet up on the left, until their card (or the one
/// whose turn it is) is clicked again.
/// </summary>
public partial class GameScreen
{
    private TurnOrderView _turnOrder = null!;

    /// <summary>A hero whose sheet is up instead of the active hero's, or null.</summary>
    private int? _viewedHeroId;

    /// <summary>The hero whose sheet is on the left: the one being looked over, or the active hero.</summary>
    private Hero? SheetHero =>
        _viewedHeroId is int id && _session.State.Heroes.FirstOrDefault(hero => hero.Id == id) is { IsInPlay: true } viewed ? viewed : ActiveHero;

    /// <summary>Over the board (its deck of the rest of the line opens down over it).</summary>
    private void BuildTurnOrder()
    {
        _turnOrder.ZIndex = 3;
        AddChild(_turnOrder);

        // Party, Retreat and Menu sit over the right panel, so they go after it.
        foreach (Control button in new Control[] { _partyButton, _retreat, _menu })
        {
            MoveChild(button, -1);
        }
    }

    /// <summary>
    /// The order from whoever's turn is on show: the heroes from them round, the monsters after the last
    /// seat; or, where turns go by initiative, the round's order.
    /// </summary>
    private void RefreshTurnOrder()
    {
        GameState state = _session.State;
        List<Hero> heroes = [.. state.Heroes.Where(hero => hero.IsInPlay).OrderBy(hero => hero.Seat)];
        // In a graveyard the undead take a turn of their own, after the monsters.
        List<TurnOrderView.Entry> enemies = [new(null, "Monsters", Palette.Danger, null, false)];
        if (Content.GetTheme(state.ThemeId).Graveyard is not null)
        {
            enemies.Add(new(null, "Undead", DieTypes.Nekrot, null, false, Faction.Undead));
        }

        TurnOrderView.Entry Card(Hero hero) =>
            new(hero.Id, hero.Name, Palette.Seat(hero.Seat), hero.Guise is null ? Looks.Resolve(hero.Appearance, hero.Portrait) : null, hero.IsKeelingOver);

        List<TurnOrderView.Entry> order;
        if (state.Initiative is { } slots)
        {
            // By initiative: the round's order, from whoever's turn is on show.
            List<TurnOrderView.Entry> round = [.. slots
                .Where(slot => slot.HeroId is null || heroes.Any(hero => hero.Id == slot.HeroId))
                .Select(slot => slot.HeroId is int id ? Card(state.GetHero(id)) : enemies.First(enemy => enemy.Enemy == slot.Enemy))];
            int at = _playingTurn is { Set: true, HeroId: null } || TurnOnShow is null
                ? round.FindIndex(entry => entry.HeroId is null && entry.Enemy == _playingFaction)
                : round.FindIndex(entry => entry.HeroId == TurnOnShow.Id);
            order = [.. round.Skip(Math.Max(0, at)), .. round.Take(Math.Max(0, at))];
        }
        else if (_playingTurn is { Set: true, HeroId: null } || TurnOnShow is null)
        {
            int at = enemies.FindIndex(enemy => enemy.Enemy == _playingFaction);
            order = [.. enemies.Skip(Math.Max(0, at)), .. heroes.Select(Card), .. enemies.Take(Math.Max(0, at))];
        }
        else
        {
            int at = Math.Max(0, heroes.IndexOf(TurnOnShow));
            order = [.. heroes.Skip(at).Select(Card), .. enemies, .. heroes.Take(at).Select(Card)];
        }

        _turnOrder.ShowOrder(order, _viewedHeroId);
    }

    /// <summary>A hero's card in the turn order: go and look at them (their sheet up), or come back.</summary>
    private void ViewHero(int heroId)
    {
        if (Locked && _viewedHeroId is null && heroId != ActiveHero?.Id)
        {
            // While the monsters play out, looking around waits.
            return;
        }

        bool back = heroId == _viewedHeroId || heroId == ActiveHero?.Id;
        _viewedHeroId = back ? null : heroId;
        CancelSelection();
        Hero? hero = back ? ActiveHero : _session.State.GetHero(heroId);
        if (hero is not null && hero.RoomId != _shownRoomId)
        {
            ShowRoom(hero.RoomId);
        }

        if (!back && hero is not null && _tokens.TryGetValue(hero.Id, out TokenView? token))
        {
            _board.Follow(token);
            Log($"Looking over {hero.Name}. Click their card again (or {ActiveHero?.Name}'s) to come back.", Palette.Seat(hero.Seat));
        }
        else
        {
            FollowActiveHero();
        }

        RefreshPanels();
    }

    private TargetSheet _targetSheet = null!;

    /// <summary>Who was last clicked on the board (a monster, or another hero), for their sheet on the right; null for none.</summary>
    private int? _targetId;

    /// <summary>A click on the board: whoever's there becomes the target, their sheet up on the right.</summary>
    private void PickTarget(GridPosition tile)
    {
        Room room = ShownRoom;
        int? id = room.MonsterAt(tile)?.Id
                  ?? _session.State.HeroesIn(room).FirstOrDefault(hero => hero.Position == tile && hero.IsInPlay && hero != ActiveHero)?.Id;
        if (id is not null)
        {
            _targetId = id;
            RefreshTarget();
        }
    }

    /// <summary>The target's sheet, while there's a target still about and no card's text is up.</summary>
    private void RefreshTarget()
    {
        GameState state = _session.State;
        Monster? monster = _targetId is int id ? state.Rooms.Values.Select(room => room.FindMonster(id)).OfType<Monster>().FirstOrDefault() : null;
        Hero? hero = monster is null && _targetId is int heroId ? state.Heroes.FirstOrDefault(other => other.Id == heroId && other.IsInPlay) : null;
        if (monster is null && hero is null)
        {
            _targetId = null;
        }

        _targetSheet.ThemeId = state.ThemeId;
        _targetSheet.Visible = _targetId is not null && _selectedCardId is null && !_inventory.IsChoosingHand;
        if (monster is not null)
        {
            _targetSheet.ShowMonster(monster);
        }
        else if (hero is not null)
        {
            _targetSheet.ShowHero(hero);
        }
    }

    /// <summary>What the mouse is over, or what to do next, for the right panel.</summary>
    private void SetHint(string text)
    {
        _hoverInfo.Text = text;
        _inventory.ShowHint(text);
    }
}
