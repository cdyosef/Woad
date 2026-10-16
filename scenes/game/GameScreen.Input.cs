using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Turning clicks and hovers into commands, hints and tile descriptions.</summary>
public partial class GameScreen
{
    private void OnTileClicked(GridPosition tile)
    {
        if (Locked || ActiveHero is not { } hero || _session.State.Turn is not { } turn || hero.RoomId != _shownRoomId)
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
        if (_leaping)
        {
            if (_session.GetLeapTargets().Contains(tile))
            {
                Execute(new Leap(hero.Id, tile));
            }
            else
            {
                _leaping = false;
                RefreshPanels();
            }

            return;
        }

        if (_shoving)
        {
            int? shovedId = room.MonsterAt(tile)?.Id ?? _session.State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == tile && other != hero)?.Id;
            if (shovedId is int targetId)
            {
                if (_session.AttackDiceLeft > 0 && SelectedAttackDice < 1)
                {
                    Log("Select at least one Rumble die.", Palette.TextDim);
                    return;
                }

                Execute(new Shove(hero.Id, targetId, SelectedAttackDice, !_weaponHeld, SelectedPowerDice));
            }
            else
            {
                _shoving = false;
                RefreshPanels();
            }

            return;
        }

        if (_stealing)
        {
            int? victim = room.MonsterAt(tile)?.Id ?? _session.State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == tile && other != hero)?.Id;
            if (victim is int targetId)
            {
                ExecuteOdHand((heroId, dice, tools, power) => new Steal(heroId, targetId, dice, tools, power));
            }
            else
            {
                _stealing = false;
                RefreshPanels();
            }

            return;
        }

        if (_session.GetRousableAllies().FirstOrDefault(fallen => fallen.Position == tile) is { } fallen)
        {
            ExecuteOdHand((heroId, dice, tools, power) => new Rouse(heroId, fallen.Id, dice, tools, power));
            return;
        }

        if (room.MonsterAt(tile) is { } monster)
        {
            if (_session.AttackDiceLeft > 0 && SelectedAttackDice < 1)
            {
                Log("Select at least one Attack die.", Palette.TextDim);
                return;
            }

            Execute(new AttackMonster(hero.Id, monster.Id, SelectedAttackDice, !_weaponHeld, SelectedPowerDice));
        }
        else if (_session.GetAttackableAllies().FirstOrDefault(ally => ally.Position == tile) is { } ally)
        {
            // Friendly fire is on and an ally is in reach.
            if (SelectedAttackDice < 1)
            {
                Log("Select at least one Rumble die.", Palette.TextDim);
                return;
            }

            Execute(new AttackAlly(hero.Id, ally.Id, SelectedAttackDice, !_weaponHeld, SelectedPowerDice));
        }
        else if (room.Layout[tile] == TileKind.Gate)
        {
            // A locked gate: a key opens it, otherwise pick the lock.
            if (hero.Keys > 0)
            {
                Execute(new OpenGate(hero.Id, tile));
            }
            else
            {
                ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, tile, dice, tools, power));
            }
        }
        else if (room.Layout[tile] == TileKind.Chest)
        {
            ClickChest(hero, room, tile);
        }
        else if (room.TrapAt(tile) is { Revealed: true } trap && tile != hero.Position)
        {
            // Beside a found trap: jump down a trap door or disarm the trap. Further off: step onto it.
            bool beside = hero.Position.ManhattanDistanceTo(tile) == 1;
            if (trap.Kind == TrapKind.TrapDoor && beside && turn.MovementPoints > 0)
            {
                Execute(new JumpDown(hero.Id, tile));
            }
            else if (beside || !_session.GetReachableTiles().ContainsKey(tile))
            {
                ExecuteOdHand((heroId, dice, tools, power) => new Disarm(heroId, tile, dice, tools, power));
            }
            else
            {
                Execute(new MoveHero(hero.Id, tile));
            }
        }
        else if (room.Layout[tile] == TileKind.Pot)
        {
            Execute(new SmashPot(hero.Id, tile));
        }
        else if (tile == hero.Position)
        {
            UseOwnTile(hero, room);
        }
        else if (turn.MovementPoints == 0)
        {
            Log(_session.MoveDiceLeft > 0 ? "Roll to move first." : "No Move dice left this turn.", Palette.TextDim);
        }
        else
        {
            Execute(new MoveHero(hero.Id, tile));
        }
    }

    /// <summary>A chest: disarm a trap you found, pick the lock if you've no key, otherwise open it (using a key if locked).</summary>
    private void ClickChest(Hero hero, Room room, GridPosition chest)
    {
        if (room.ChestTrapAt(chest) is { Revealed: true })
        {
            ExecuteOdHand((heroId, dice, tools, power) => new Disarm(heroId, chest, dice, tools, power));
        }
        else if (room.ChestLockAt(chest) is not null && hero.Keys < 1)
        {
            ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, chest, dice, tools, power));
        }
        else
        {
            Execute(new OpenChest(hero.Id, chest));
        }
    }

    /// <summary>Runs an Od Hand command with the selected Od Hand dice, tools and Power Dice.</summary>
    private void ExecuteOdHand(Func<int, int?, bool, int, GameCommand> command)
    {
        if (ActiveHero is not { } hero)
        {
            return;
        }

        if (_session.OdHandDiceLeft > 0 && SelectedOdHandDice < 1)
        {
            Log("Select at least one Od Hand die.", Palette.TextDim);
            return;
        }

        Execute(command(hero.Id, SelectedOdHandDice, !_toolsHeld, SelectedPowerDice));
    }

    /// <summary>
    /// Clicking the tile you stand on: pick up loot, take the stairs, escape, or use the door: disarm
    /// its trap, unlock it (a key, or the Boss Key), pick its lock, or go through.
    /// </summary>
    private void UseOwnTile(Hero hero, Room room)
    {
        if (room.HasLootAt(hero.Position))
        {
            Execute(new PickUpLoot(hero.Id));
        }
        else if (room.Breaches.ContainsKey(hero.Position) && room.Layout[hero.Position] != TileKind.CrackedWall)
        {
            Execute(new GoThroughBreach(hero.Id));
        }
        else if (room.Layout[hero.Position] is TileKind.StairsDown or TileKind.StairsUp)
        {
            Execute(new TakeStairs(hero.Id));
        }
        else if (room.Layout[hero.Position] == TileKind.Exit)
        {
            Execute(new Escape(hero.Id));
        }
        else if (DoorAt(room, hero.Position) is { } side)
        {
            Passage? passage = room.PassageOn(side);
            if (passage?.Trap is { Revealed: true })
            {
                ExecuteOdHand((heroId, dice, tools, power) => new Disarm(heroId, hero.Position, dice, tools, power));
            }
            else if (passage is { IsLocked: true } && (passage.NeedsBossKey || hero.Keys > 0))
            {
                Execute(new UnlockDoor(hero.Id, side));
            }
            else if (passage is { IsLocked: true })
            {
                ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, hero.Position, dice, tools, power));
            }
            else
            {
                Execute(new GoThroughDoor(hero.Id, side));
            }
        }
    }

    /// <summary>The door whose doorway is this tile, unless it's a secret door nobody has found.</summary>
    private static Direction? DoorAt(Room room, GridPosition tile) =>
        room.Doors.Keys.Where(side => side.DoorTile() == tile && room.PassageOn(side) is not { IsHidden: true })
            .Select(side => (Direction?)side).FirstOrDefault();

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
        if (Locked || ActiveHero is not { } hero || _session.State.Turn is not { } turn)
        {
            return "";
        }

        if (turn.KeelingOver)
        {
            int doom = hero.DoomDiceOwed + hero.KeelRolls;
            string dooms = doom > 0 ? $" {doom} Doom {(doom == 1 ? "Die rolls" : "Dice roll")} with it (1s are death)." : "";
            return $"{hero.Name} is Keeling Over (life {hero.LifeMarks}/3, death {hero.DeathMarks}/3). Roll the Keel Die: 6 is a mark of life, 1 a mark of death.{dooms} Select Power Dice to add their 6s.";
        }

        if (turn.UnsettledPowerDice > 0)
        {
            return "You rolled a 6! Add a Power Die to this roll, or bank it for later.";
        }

        if (turn.PendingRoll is not null)
        {
            return "Click a die to reroll it with Luck, or Confirm the roll.";
        }

        if (_stealing)
        {
            return "Steal: click a monster or hero next to you (orange). Right-click to cancel.";
        }

        if (_shoving)
        {
            return "Shove: click a monster next to you (orange) to push it a tile. Right-click to cancel.";
        }

        if (_leaping)
        {
            return $"Leap: click a purple tile up to {GameRules.MaxLeap} away. You roll a Wend die as you jump and carry half the roll. Right-click to cancel.";
        }

        if (_targeting)
        {
            return "Click an orange tile to aim. Right-click cancels.";
        }

        if (turn.MovementPoints == 0)
        {
            return _session.MoveDiceLeft > 0 || _session.AttackDiceLeft > 0
                ? "Choose dice, then Wend to move, click a red-outlined monster to attack, or Search the room."
                : "Out of dice. Use your action or belt item, or end your turn.";
        }

        return "Click a blue tile to move. Doing anything else ends this move.";
    }

    private string DescribeTile(GridPosition tile)
    {
        Room room = ShownRoom;
        if (room.MonsterAt(tile) is { } monster)
        {
            MonsterDefinition definition = monster.Definition;
            string power = monster.Power.Banked > 0 ? $", {PowerDiceText(monster.Power.Banked)} banked" : "";
            return $"{definition.Name}: {monster.Hearts}/{monster.MaxHearts} hearts{power}, move {definition.Move}, " +
                   $"attack {UiTheme.Dice(definition.AttackDice, definition.Weapon)}, defense {UiTheme.Dice(definition.DefenseDice, definition.Armor)}{Traits(monster)}";
        }

        if (_session.State.ActiveHeroesIn(room).FirstOrDefault(hero => hero.Position == tile) is { } standing)
        {
            string power = standing.Power.Banked > 0 ? $", {PowerDiceText(standing.Power.Banked)} banked" : "";
            string companion = standing.OwnerId is int ownerId ? $" ({NameOf(ownerId)}'s companion, {Describe(standing.Order)})" : "";
            return $"{standing.Name}{companion}: {standing.Hearts}/{standing.MaxHearts} hearts{power}";
        }

        if (_session.State.HeroesIn(room).FirstOrDefault(hero => hero.Position == tile && hero.IsKeelingOver) is { } fallen)
        {
            int waiting = fallen.DoomDiceOwed + fallen.KeelRolls;
            string doom = waiting > 0 ? $", {waiting} Doom {(waiting == 1 ? "Die" : "Dice")} on their next roll" : "";
            string rouse = _session.GetRousableAllies().Contains(fallen)
                ? " Click to rouse them with Od Hand: every 6 of the total is a mark of life, but each 1 rolled adds a Doom Die."
                : " An ally beside them can rouse them with Od Hand.";
            return $"{fallen.Name} is Keeling Over: life {fallen.LifeMarks}/3, death {fallen.DeathMarks}/3{doom}.{rouse}";
        }

        string grave = room.TombstoneAt(tile) is { } stone ? $"{stone.Inscription}. {stone.Epitaph}. " : "";
        if (room.HasLootAt(tile))
        {
            IEnumerable<string> things = room.LootAt(tile).Select(card => card.Definition.Name);
            if (room.KeysAt(tile) > 0)
            {
                things = things.Append(UiTheme.Count(room.KeysAt(tile), "key", "keys"));
            }

            if (room.BossKeyAt == tile)
            {
                things = things.Append("the Boss Key");
            }

            return grave + "Loot: " + string.Join(", ", things) + ". Stand on it and click to pick it up (your first loot each turn is free).";
        }

        if (room.TrapAt(tile) is { Revealed: true } trap)
        {
            return trap.Kind == TrapKind.TrapDoor
                ? $"A trap door to the floor below. Next to it: click to jump down safely (1 step), or disarm it ({trap.Difficulty}+). Walk onto it and you fall, losing a heart."
                : $"Spikes: whoever steps here loses a heart and stops. Next to it: click to disarm with Od Hand ({trap.Difficulty}+).";
        }

        bool bossAlive = room.Monsters.Any(other => other.Definition.Boss);
        string terrain = room.Layout[tile] switch
        {
            TileKind.Chest when room.IsChestOpened(tile) => "An empty chest.",
            TileKind.Chest => DescribeChest(room, tile),
            TileKind.CrackedWall when room.Breaches.ContainsKey(tile) => "A cracked wall in the edge of the room. A bomb's blast would break through into the room next door.",
            TileKind.CrackedWall => "A cracked wall. A bomb's blast would break it, and something may be sealed inside.",
            TileKind.Pot => "A clay pot. Stand next to it and click to smash it: coins, a heart, or nothing.",
            TileKind.Wall => "A stone pillar.",
            TileKind.Pit when room.BelowRoomId is not null =>
                "A pit: the room below shows far down. Fall in (or be shoved) and you drop to it, losing a heart. Leap over it with a Wend die.",
            TileKind.Pit => "A bottomless pit. Fall in and you lose a heart climbing out; a monster shoved in is gone. Leap over it with a Wend die.",
            TileKind.StairsDown => $"Stairs down to floor {room.Floor + 1}. Stand on them and click again to descend (1 step).",
            TileKind.StairsUp => $"Stairs up to floor {room.Floor - 1}. Stand on them and click again to climb (1 step).",
            TileKind.Rubble => "Rough ground: rubble. Each step onto it costs 2.",
            TileKind.Gate => $"A locked gate. Next to it: click it to open it with a key, or to pick the lock with Od Hand ({room.Gates.GetValueOrDefault(tile)}+).",
            _ when room.Breaches.ContainsKey(tile) => "A breach into the room next door: stand here and click again to go through (1 step).",
            TileKind.ShallowWater => "Shallow water: wade through at 2 steps a tile.",
            TileKind.DeepWater => "Deep water: swim at 2 steps a tile. End your turn here and a Rumble die keeps you afloat; with none left you drown back to shore and lose a heart. Monsters can't follow.",
            TileKind.Exit when bossAlive => "The way out, sealed until the boss here falls.",
            TileKind.Exit => "The way out! Stand on it and click again to escape with your treasure.",
            _ when DoorAt(room, tile) is { } side => DescribeDoor(room, side),
            _ => "",
        };

        terrain = grave + terrain;
        if (!_leaping && _session.GetReachableTiles().TryGetValue(tile, out int steps))
        {
            terrain = $"{UiTheme.Count(steps, "step", "steps")} away. {terrain}";
        }
        else if (_leaping && _session.GetLeapTargets().Contains(tile))
        {
            int distance = ActiveHero!.Position.ChebyshevDistanceTo(tile);
            terrain = $"Leap here: you need {distance * 2}+ on the die, or you come down short. {terrain}";
        }

        return terrain;
    }

    private static string PowerDiceText(int count) => $"{count} Power {(count == 1 ? "Die" : "Dice")}";

    private static string DescribeChest(Room room, GridPosition chest)
    {
        string trap = room.ChestTrapAt(chest) is { Revealed: true } needle ? $" A needle trap guards it (disarm {needle.Difficulty}+)." : "";
        if (room.HoldsBossKey(chest))
        {
            return "The vault chest, banded in crimson: the Boss Key lies inside. Stand next to it and click it to open it.";
        }

        return room.ChestLockAt(chest) is int difficulty
            ? $"A locked chest with better loot. Open it with a key, or pick the lock with Od Hand ({difficulty}+).{trap}"
            : $"A closed chest. Stand next to it and click it to open it.{trap}";
    }

    private string DescribeDoor(Room room, Direction side)
    {
        if (room.IsSealed)
        {
            return "A sealed door. It opens when every monster here is defeated.";
        }

        Passage? passage = room.PassageOn(side);
        string darts = passage?.Trap is { Revealed: true } trap ? $" Darts are rigged in its frame (disarm {trap.Difficulty}+)." : "";
        if (passage is { IsBlocked: true })
        {
            return "A doorway walled up with cracked stone. A bomb's blast would open it.";
        }

        if (passage is { IsLocked: true, NeedsBossKey: true })
        {
            return "The Boss Door. Only the Boss Key opens it: find it somewhere on this floor.";
        }

        if (passage is { IsLocked: true })
        {
            return $"A locked door. Stand in it and click to use a key, or to pick the lock with Od Hand ({passage.Difficulty}+).{darts}";
        }

        bool explored = _session.State.Rooms[room.Doors[side]].Explored;
        return $"A door to {(explored ? "a room you've seen" : "an unexplored room")}. Stand in it and click again to go through (1 step).{darts}";
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

        if (monster.Robbed)
        {
            traits.Add("robbed");
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
        if (Locked)
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
        _stealing = false;
        _shoving = false;
        _leaping = false;
        _selectedCardId = null;
        _targeting = false;
        _board.SetBlast([]);
        RefreshPanels();
    }

    private void OnCardAction(CardAction action)
    {
        if (Locked || _selectedCardId is not int cardId || ActiveHero is not { } hero || hero.FindCard(cardId) is not var (card, _))
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
