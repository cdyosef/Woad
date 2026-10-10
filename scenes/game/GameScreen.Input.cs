using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Turning clicks and hovers into commands, hints and tile descriptions.</summary>
public partial class GameScreen
{
    private void OnTileClicked(GridPosition tile)
    {
        if (_busy || ActiveHero is not { } hero || _session.State.Turn is not { } turn || hero.RoomId != _shownRoomId)
        {
            return;
        }

        if (_targeting && _selectedCardId is int cardId)
        {
            if (_session.GetCardTargets(cardId).Contains(tile))
            {
                Execute(new UseCard(hero.Id, cardId, tile));
            }
            else
            {
                CancelSelection();
            }

            return;
        }

        Room room = ShownRoom;
        if (room.MonsterAt(tile) is { } monster)
        {
            Execute(new AttackMonster(hero.Id, monster.Id));
        }
        else if (room.Layout[tile] == TileKind.Chest)
        {
            Execute(new OpenChest(hero.Id, tile));
        }
        else if (room.Layout[tile] == TileKind.Pot)
        {
            Execute(new SmashPot(hero.Id, tile));
        }
        else if (tile == hero.Position)
        {
            UseOwnTile(hero, room);
        }
        else if (turn.MovementRoll is null && !turn.MovementLocked)
        {
            Log("Roll to move first.", Palette.TextDim);
        }
        else
        {
            Execute(new MoveHero(hero.Id, tile));
        }
    }

    /// <summary>Clicking the tile you stand on: pick up loot, take the stairs, escape, or go through the door.</summary>
    private void UseOwnTile(Hero hero, Room room)
    {
        if (room.LootAt(hero.Position).Count > 0)
        {
            Execute(new PickUpLoot(hero.Id));
        }
        else if (room.Layout[hero.Position] == TileKind.StairsDown)
        {
            Execute(new TakeStairs(hero.Id));
        }
        else if (room.Layout[hero.Position] == TileKind.Exit)
        {
            Execute(new Escape(hero.Id));
        }
        else if (DoorAt(room, hero.Position) is { } side)
        {
            Execute(new GoThroughDoor(hero.Id, side));
        }
    }

    private static Direction? DoorAt(Room room, GridPosition tile) =>
        room.Doors.Keys.Where(side => side.DoorTile() == tile).Select(side => (Direction?)side).FirstOrDefault();

    private void OnTileHovered(GridPosition? tile)
    {
        _hovered = tile;
        _hoverInfo.Text = tile is { } hovered ? DescribeTile(hovered) : Hint();

        if (_targeting && tile is { } target && _selectedCardId is int cardId
            && ActiveHero?.FindCard(cardId)?.Card.Definition.Effect is { AreaRadius: > 0 } effect
            && _session.GetCardTargets(cardId).Contains(target))
        {
            _board.SetBlast(RoomGrid.AllPositions().Where(position => position.ChebyshevDistanceTo(target) <= effect.AreaRadius));
        }
        else
        {
            _board.SetBlast([]);
        }
    }

    /// <summary>What to do next, shown in the top strip when the mouse isn't over a tile.</summary>
    private string Hint()
    {
        if (_busy || ActiveHero is not { } hero || _session.State.Turn is not { } turn)
        {
            return "";
        }

        if (turn.PendingAttack is not null)
        {
            return "Click a die to reroll it with Luck, or Confirm the attack.";
        }

        if (_targeting)
        {
            return "Click an orange tile to aim. Right-click cancels.";
        }

        if (turn.MovementRoll is null && !turn.MovementLocked)
        {
            return "Roll to move, or act first: attack a red-outlined monster or use a card.";
        }

        return "Click a blue tile to move. Gold outlines can be clicked. End your turn when you're done.";
    }

    private string DescribeTile(GridPosition tile)
    {
        Room room = ShownRoom;
        if (room.MonsterAt(tile) is { } monster)
        {
            MonsterDefinition definition = monster.Definition;
            return $"{definition.Name}: {monster.Hearts}/{definition.Hearts} hearts, move {definition.Move}, " +
                   $"attack {definition.AttackDice}, defense {definition.DefenseDice}{Traits(monster)}";
        }

        if (_session.State.ActiveHeroesIn(room).FirstOrDefault(hero => hero.Position == tile) is { } standing)
        {
            return $"{standing.Name}: {standing.Hearts}/{standing.MaxHearts} hearts";
        }

        if (room.LootAt(tile) is { Count: > 0 } loot)
        {
            return "Loot: " + string.Join(", ", loot.Select(card => card.Definition.Name)) + ". Stand on it and click to pick it up.";
        }

        bool bossAlive = room.Monsters.Any(other => other.Definition.Boss);
        string terrain = room.Layout[tile] switch
        {
            TileKind.Chest when room.IsChestOpened(tile) => "An empty chest.",
            TileKind.Chest => "A closed chest. Stand next to it and click it to open it.",
            TileKind.Pot => "A clay pot. Stand next to it and click to smash it: coins, a heart, or nothing.",
            TileKind.Wall => "A stone pillar.",
            TileKind.Pit => "A bottomless pit. Only flyers cross it.",
            TileKind.StairsDown => $"Stairs down to floor {room.Floor + 1}. Stand on them and click again to descend (1 step).",
            TileKind.Exit when bossAlive => "The way out, sealed until the boss here falls.",
            TileKind.Exit => "The way out! Stand on it and click again to escape with your treasure.",
            _ when DoorAt(room, tile) is { } side => DescribeDoor(room, side),
            _ => "",
        };

        if (_session.GetReachableTiles().TryGetValue(tile, out int steps))
        {
            terrain = $"{UiTheme.Count(steps, "step", "steps")} away. {terrain}";
        }

        return terrain;
    }

    private string DescribeDoor(Room room, Direction side)
    {
        if (room.IsSealed)
        {
            return "A sealed door. It opens when every monster here is defeated.";
        }

        bool explored = _session.State.Rooms[room.Doors[side]].Explored;
        return $"A door to {(explored ? "a room you've seen" : "an unexplored room")}. Stand in it and click again to go through (1 step).";
    }

    private static string Traits(Monster monster)
    {
        MonsterDefinition definition = monster.Definition;
        var traits = new List<string>();
        if (definition.Boss)
        {
            traits.Add("boss");
        }

        if (definition.AttackPattern == AttackPattern.Reach)
        {
            traits.Add($"reach {definition.AttackRange}");
        }

        if (definition.AttackPattern == AttackPattern.Ranged)
        {
            traits.Add($"ranged {definition.AttackRange}");
        }

        if (definition.Sweep is { } sweep)
        {
            traits.Add($"sweeps everyone within {sweep.Radius} every {sweep.Every} turns");
        }

        if (definition.Flying)
        {
            traits.Add("flies");
        }

        if (definition.SplitsInto is not null)
        {
            traits.Add("splits");
        }

        if (definition.Reassembles && !monster.HasReassembled)
        {
            traits.Add("reassembles");
        }

        if (monster.StunnedTurns > 0)
        {
            traits.Add("stunned");
        }

        return traits.Count == 0 ? "" : $" ({string.Join(", ", traits)})";
    }

    private void SelectCard(int cardId)
    {
        if (_busy)
        {
            return;
        }

        _selectedCardId = _selectedCardId == cardId ? null : cardId;
        _targeting = false;
        _board.SetBlast([]);
        RefreshPanels();
    }

    private void CancelSelection()
    {
        _selectedCardId = null;
        _targeting = false;
        _board.SetBlast([]);
        RefreshPanels();
    }

    private void OnCardAction(CardAction action)
    {
        if (_busy || _selectedCardId is not int cardId || ActiveHero is not { } hero || hero.FindCard(cardId) is not var (card, _))
        {
            return;
        }

        switch (action)
        {
            case CardAction.Use when card.Definition.Effect?.Kind == CardEffectKind.Heal:
                Execute(new UseCard(hero.Id, cardId));
                break;
            case CardAction.Use:
                _targeting = true;
                Log($"Choose a target for the {card.Definition.Name}. Right-click to cancel.", Palette.Target with { A = 1 });
                RefreshPanels();
                break;
            case CardAction.Equip:
                Execute(new EquipCard(hero.Id, cardId));
                break;
            case CardAction.Belt:
                Execute(new BeltCard(hero.Id, cardId));
                break;
            case CardAction.Stow:
                Execute(new StowCard(hero.Id, cardId));
                break;
            case CardAction.Drop:
                Execute(new DropCard(hero.Id, cardId));
                break;
        }
    }
}
