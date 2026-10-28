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
    /// <summary>Rumble: attack whatever is on the tile, if it's in reach (a monster, a bomb, or with friendly fire on, an ally); else put Rumble away.</summary>
    private void RumbleAt(Hero hero, Room room, GridPosition tile)
    {
        if (!RumbleTargets(room).Contains(tile))
        {
            _rumbling = false;
            RefreshPanels();
            return;
        }

        if (SelectedAttackDice < 1)
        {
            Log("Select at least one Rumble die.", Palette.TextDim);
            return;
        }

        if (room.MonsterAt(tile) is { } monster)
        {
            Execute(new AttackMonster(hero.Id, monster.Id, SelectedAttackDice, !_weaponHeld, SelectedPowerDice, ReadiedWeaponId));
        }
        else if (_session.GetAttackableAllies().FirstOrDefault(ally => ally.Position == tile) is { } ally)
        {
            Execute(new AttackAlly(hero.Id, ally.Id, SelectedAttackDice, !_weaponHeld, SelectedPowerDice, ReadiedWeaponId));
        }
        else if (room.BombAt(tile) is { } bomb)
        {
            Execute(new AttackBomb(hero.Id, bomb.Id, SelectedAttackDice, !_weaponHeld, SelectedPowerDice));
        }
        else if (room.MerchantAt(tile) is { } merchant)
        {
            Execute(new AttackMerchant(hero.Id, merchant.Id, SelectedAttackDice, !_weaponHeld, SelectedPowerDice));
        }
    }

    /// <summary>The enemy a double-click rolled Wend toward: clicking it again walks up to it.</summary>
    private GridPosition? _goal;

    /// <summary>
    /// Double-clicks, for quick play. On an enemy you can hit from where you stand: Rumble aims at it, and
    /// one more click attacks. On a space, or an enemy you can't hit yet: if you haven't rolled Wend this
    /// turn, your selected Wend dice roll, and clicking it again goes there (beside the enemy, after which
    /// Rumble aims at it). Already rolled, it goes there straight away.
    /// </summary>
    private async void OnTileDoubleClicked(GridPosition tile)
    {
        if (Locked || ActiveHero is not { } hero || _session.State.Turn is not { } turn || hero.RoomId != _shownRoomId
            || tile == hero.Position || _targeting || _leaping || _shoving || _stealing)
        {
            return;
        }

        Room room = ShownRoom;
        if (room.MonsterAt(tile) is { } monster && RumbleTargets(room).Contains(tile))
        {
            AimAt(monster);
            return;
        }

        bool enemy = room.MonsterAt(tile) is not null;
        if (!enemy && !(room.Layout[tile].IsWalkable() || room.Layout[tile] == TileKind.DeepWater))
        {
            return;
        }

        if (turn.MovementPoints > 0)
        {
            await GoToward(hero, room, tile);
            return;
        }

        if (_session.MoveDiceLeft < 1 || turn.PendingRoll is not null)
        {
            return;
        }

        if (SelectedMoveDice < 1)
        {
            Log("Select at least one Wend die.", Palette.TextDim);
            return;
        }

        if (!await ExecuteAsync(new RollMovement(hero.Id, SelectedMoveDice, !_speedHeld, SelectedPowerDice)))
        {
            return;
        }

        _goal = enemy ? tile : null;
        bool reachable = enemy ? SpotBeside(room, tile) is not null : _session.GetReachableTiles().ContainsKey(tile);
        Log(reachable
            ? (enemy ? "Click it again to go up to it." : "Click it again to go there.")
            : "Out of reach this turn: click somewhere closer.", reachable ? Palette.Heal : Palette.TextDim);
    }

    /// <summary>Rumble aims at a monster: one more click on it attacks.</summary>
    private void AimAt(Monster monster)
    {
        _rumbling = true;
        _stealing = _shoving = _leaping = false;
        _goal = null;
        Log($"Click the {monster.Definition.Name} again to attack it. Right-click to cancel.", Palette.Danger);
        RefreshPanels();
    }

    /// <summary>
    /// Goes to a space you can reach, or up to an enemy: the reachable tile it can be hit from with the
    /// shortest walk. Arriving where you can hit it, Rumble aims at it.
    /// </summary>
    private async Task GoToward(Hero hero, Room room, GridPosition tile)
    {
        _goal = null;
        if (room.MonsterAt(tile) is not { } monster)
        {
            Execute(new MoveHero(hero.Id, tile));
            return;
        }

        if (SpotBeside(room, tile) is not { } spot)
        {
            Log($"You can't get to the {monster.Definition.Name} this turn.", Palette.TextDim);
            return;
        }

        if (spot != hero.Position && !await ExecuteAsync(new MoveHero(hero.Id, spot)))
        {
            return;
        }

        if (ActiveHero == hero && room.FindMonster(monster.Id) is not null && RumbleTargets(room).Contains(monster.Position))
        {
            AimAt(monster);
        }
    }

    /// <summary>The tile to walk to for a swing at whatever stands on <paramref name="target"/>: reachable, closest first.</summary>
    private GridPosition? SpotBeside(Room room, GridPosition target)
    {
        Hero hero = ActiveHero!;
        IReadOnlyDictionary<GridPosition, int> reachable = _session.GetReachableTiles();
        return reachable.Keys.Append(hero.Position)
            .Where(tile => GameSession.CanHeroReach(hero, room.Layout, tile, target, ReadiedWeapon))
            .OrderBy(tile => tile == hero.Position ? 0 : reachable[tile])
            .ThenBy(tile => tile.ManhattanDistanceTo(target))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

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

        if (_casting is not null)
        {
            CastAt(hero, tile);
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
            int? victim = room.MonsterAt(tile)?.Id ?? room.MerchantAt(tile)?.Id ?? _session.State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == tile && other != hero)?.Id;
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

        // A lit bomb next to you: try to defuse it (Od Hand 10, or it goes off).
        if (room.BombAt(tile) is { Lit: true } && _session.CheckDisarm(tile) is null)
        {
            ExecuteOdHand((heroId, dice, tools, power) => new Disarm(heroId, tile, dice, tools, power));
            return;
        }

        if (_session.GetRousableAllies().FirstOrDefault(fallen => fallen.Position == tile) is { } fallen)
        {
            ExecuteOdHand((heroId, dice, tools, power) => new Rouse(heroId, fallen.Id, dice, tools, power));
            return;
        }

        if (_rumbling)
        {
            RumbleAt(hero, room, tile);
            return;
        }

        // A merchant: free them, or trade with them.
        if (ClickMerchant(hero, room, tile))
        {
            return;
        }

        // Shackles on an ally beside you: a key, or pick the lock.
        if (tile != hero.Position && _session.State.HeroesIn(room).FirstOrDefault(ally => ally.Position == tile && ally.Shackled is not null) is { } prisoner
            && prisoner.Position.ChebyshevDistanceTo(hero.Position) == 1)
        {
            FreeFromShackles(hero, prisoner);
            return;
        }

        // An ally beside you: trade with them.
        if (tile != hero.Position && _session.GetTradePartners().FirstOrDefault(ally => ally.Position == tile && !ally.IsKeelingOver) is { } partner)
        {
            OpenTrade(hero, partner);
            return;
        }

        // Loot beside you: take it, or open the bag.
        if (tile != hero.Position && room.MonsterAt(tile) is null && room.Layout[tile] != TileKind.Chest
            && room.HasLootAt(tile) && hero.Position.ChebyshevDistanceTo(tile) == 1)
        {
            Rummage(hero, room, tile);
            return;
        }

        if (room.MonsterAt(tile) is not null && _goal == tile && turn.MovementPoints > 0)
        {
            _ = GoToward(hero, room, tile);
        }
        else if (room.MonsterAt(tile) is not null)
        {
            Log("Press Rumble (or Space), or double-click it, to attack.", Palette.TextDim);
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
            Log(_session.MoveDiceLeft > 0 ? "Roll to move first (or double-click where you want to go)." : "No Move dice left this turn.", Palette.TextDim);
        }
        else
        {
            Execute(new MoveHero(hero.Id, tile));
        }
    }

    /// <summary>
    /// A chest: an opened one is rummaged through; otherwise disarm a trap you found, pick the lock if
    /// you've no key, or open it (using a key if locked) and rummage through what's inside.
    /// </summary>
    private async void ClickChest(Hero hero, Room room, GridPosition chest)
    {
        if (room.IsChestOpened(chest))
        {
            Rummage(hero, room, chest);
            return;
        }

        if (room.ChestTrapAt(chest) is { Revealed: true })
        {
            ExecuteOdHand((heroId, dice, tools, power) => new Disarm(heroId, chest, dice, tools, power));
        }
        else if (room.ChestLockAt(chest) is not null && hero.Keys < 1)
        {
            ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, chest, dice, tools, power));
        }
        else if (await ExecuteAsync(new OpenChest(hero.Id, chest)) && ActiveHero == hero && room.IsChestOpened(chest) && room.LootAt(chest).Count > 0)
        {
            Rummage(hero, room, chest);
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

    /// <summary>Unlocks shackles (your own, or an ally's beside you): with a key if you have one, else by picking the lock.</summary>
    private void FreeFromShackles(Hero hero, Hero prisoner)
    {
        if (hero.Keys > 0)
        {
            Execute(new Unshackle(hero.Id, prisoner.Id));
        }
        else
        {
            ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, prisoner.Position, dice, tools, power));
        }
    }

    /// <summary>
    /// Clicking the tile you stand on: pick up loot, take the stairs, escape, or use the door: disarm
    /// its trap, unlock it (a key, or the Boss Key), pick its lock, or go through.
    /// </summary>
    private void UseOwnTile(Hero hero, Room room)
    {
        if (hero.Shackled is not null)
        {
            FreeFromShackles(hero, hero);
        }
        else if (room.HasLootAt(hero.Position))
        {
            Rummage(hero, room, hero.Position);
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
        room.Doors.Keys.Where(side => room.DoorTile(side) == tile && room.PassageOn(side) is not { IsHidden: true })
            .Select(side => (Direction?)side).FirstOrDefault();

    private void OnTileHovered(GridPosition? tile)
    {
        _hovered = tile;
        _hoverInfo.Text = tile is { } hovered ? DescribeTile(hovered) : Hint();

        if (_targeting && tile is { } target && _selectedCardId is int cardId
            && ActiveHero?.FindCard(cardId)?.Card.Definition.Effect is { AreaRadius: > 0 } effect
            && _session.GetCardTargets(cardId).Contains(target))
        {
            _board.SetBlast(ShownRoom.Positions.Where(position => position.ChebyshevDistanceTo(target) <= effect.AreaRadius));
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

        if (hero.FireDice > 0 && _session.CheckStopDropRoll() is null)
        {
            return $"You're on fire ({hero.FireDice} {(hero.FireDice == 1 ? "die" : "dice")})! Stop, drop and roll to put it out (your whole turn), or get into water.";
        }

        if (turn.PendingRoll is null && _session.HeldBy(hero) is { } holder)
        {
            return $"The {holder.Definition.Name} has hold of you: you can't move until you hurt it, shove it or stun it.";
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

        if (_rumbling)
        {
            return "Rumble: click a target in red to attack it with your selected dice. Right-click (or Space) to put it away.";
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
                ? "Choose dice, then Wend to move, Rumble (Space) to attack, or Search the room."
                : "Out of dice. Use your action or belt item, or end your turn.";
        }

        return "Click a blue tile to move. Doing anything else ends this move.";
    }

    private string DescribeTile(GridPosition tile)
    {
        Room room = ShownRoom;
        if (room.MerchantAt(tile) is { } merchant)
        {
            return merchant.Chained
                ? $"{merchant.Name}, a traveling merchant, chained to the wall. Free them with a key, or pick the lock with Od Hand ({merchant.LockDifficulty}+); bring them out of the dungeon alive and the Merchant Guild will remember it."
                : $"{merchant.Name}, a traveling merchant ({merchant.Stock.Count} wares). They follow the party; click beside them to trade. Bring them out alive to rescue them.";
        }

        if (room.MonsterAt(tile) is { } monster)
        {
            MonsterDefinition definition = monster.Definition;
            string power = monster.Power.Banked > 0 ? $", {PowerDiceText(monster.Power.Banked)} banked" : "";
            string burning = monster.FireDice > 0 ? $" ON FIRE ({monster.FireDice} {(monster.FireDice == 1 ? "die" : "dice")})." : "";
            string plants = room.Plants.TryGetValue(tile, out bool heroes) ? heroes ? " In the party's plants (+1 defense to them, rough going for it)." : " In its side's plants (+1 defense)." : "";
            return $"{definition.Name}: {monster.Hearts}/{monster.MaxHearts} hearts{power}, move {definition.Move}, " +
                   $"attack {UiTheme.Dice(definition.AttackDice, definition.Weapon)}, defense {UiTheme.Dice(definition.DefenseDice, definition.Armor)}{Traits(monster)}{burning}{plants}{HitWith(monster)}";
        }

        if (_session.State.ActiveHeroesIn(room).FirstOrDefault(hero => hero.Position == tile) is { } standing)
        {
            string power = standing.Power.Banked > 0 ? $", {PowerDiceText(standing.Power.Banked)} banked" : "";
            string companion = standing.OwnerId is int ownerId ? $" ({NameOf(ownerId)}'s companion, {Describe(standing.Order)})" : "";
            string onFire = standing.FireDice > 0 ? $", ON FIRE ({standing.FireDice} {(standing.FireDice == 1 ? "die" : "dice")})" : "";
            string lying = (_session.IsDiving(standing) ? ", diving" : standing.Prone ? ", prone" : "")
                + (standing.Stun.IsStunned ? $", stunned (+{standing.Stun.AttackBonus} to hit)" : "")
                + (_session.HeldBy(standing) is { } holder ? $", held by the {holder.Definition.Name}" : "")
                + string.Concat(standing.Conditions.All.OrderBy(entry => entry.Key).Select(entry => $", {DieTypes.Describe(entry.Key, entry.Value)}"));
            return $"{standing.Name}{companion}: {standing.Hearts}/{standing.MaxHearts} hearts{power}{onFire}{lying}";
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

        if (room.BombAt(tile) is { Lit: true } lit)
        {
            string owner = lit.OwnerId is int ownerId ? NameOf(ownerId) : "someone";
            return $"A lit bomb: it goes off at the end of {owner}'s {(lit.FuseTurns == 1 ? "current or next" : "next")} turn, blasting everyone within a tile. "
                   + $"Next to it: click to defuse it (Od Hand {GameRules.DefuseDifficulty}+, or it goes off), Shove it away, or strike it ({GameRules.BombChancePerDie}% a Rumble die to set it off). Fire sets it off.";
        }

        if (room.BombAt(tile) is { Lit: false } && room.LootAt(tile).Count == 0 && room.GoldAt(tile) == 0)
        {
            return "An unlit bomb. Stand on it and click to pick it up as a Bomb card. Fire or a blow could still set it off.";
        }

        string grave = room.TombstoneAt(tile) is { } stone ? $"{stone.Inscription}. {stone.Epitaph}. " : "";
        if (room.HasLootAt(tile))
        {
            IEnumerable<string> things = room.LootAt(tile).Select(card => card.Definition.Name);
            if (room.GoldAt(tile) > 0)
            {
                things = things.Prepend($"{room.GoldAt(tile)} gold");
            }

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
            return trap.Kind == TrapKind.BombTrap
                ? $"A bomb trap: step on it and it pops out a lit bomb. Next to it: click to disarm it ({trap.Difficulty}+), and it gives up the bomb unlit."
                : trap.Kind == TrapKind.TrapDoor
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

        if (room.IsGrandChest(chest) && room.ChestLockAt(chest) is int grandLock)
        {
            return $"A grand chest, gilded and jeweled: three treasures from deep below, and gold. Open it with a key, or pick the lock with Od Hand ({grandLock}+).";
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

        if (definition.Thief)
        {
            traits.Add("steals, then runs");
        }

        if (definition.Mugger)
        {
            traits.Add("goes for your gold");
        }

        if (monster.HasLoot)
        {
            traits.Add($"carrying {monster.StolenGold} gold{(monster.StolenCards.Count > 0 ? $" and {UiTheme.Count(monster.StolenCards.Count, "card", "cards")}" : "")}");
        }

        if (definition.DieType is { } type)
        {
            traits.Add($"{DieTypes.Name(type)} attacks: {DieTypes.SixOnHit(type)}");
        }

        if (definition.FireImmune)
        {
            traits.Add("can't burn");
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

        if (definition.Grabs)
        {
            traits.Add("grabs: no moving away");
        }

        if (definition.KnocksBack)
        {
            traits.Add("knocks you back");
        }

        if (definition.Hexes)
        {
            traits.Add("hexes away Luck");
        }

        if (definition.Raises is not null)
        {
            traits.Add($"raises the dead ({monster.Raised} of {definition.RaiseTotal})");
        }

        if (definition.Engulfs)
        {
            traits.Add("swallows loot");
        }

        if (monster.RaisedBy is not null)
        {
            traits.Add("raised: crumbles with its master");
        }

        if (monster.FrozenTurns > 0)
        {
            traits.Add("frozen");
        }

        if (monster.Stun.IsStunned)
        {
            traits.Add($"stunned (+{monster.Stun.AttackBonus} to hit)");
        }

        if (monster.Prone)
        {
            traits.Add("prone");
        }

        traits.AddRange(monster.Conditions.All.OrderBy(entry => entry.Key).Select(entry => DieTypes.Describe(entry.Key, entry.Value)));
        return traits.Count == 0 ? "" : $" ({string.Join(", ", traits)})";
    }

    /// <summary>
    /// The weapon each hero holding two (a Spear and a Boomerang) has readied for their attacks, by a click on
    /// it in hand or on the Rumble row's toggle. Not there: the rules pick (the main hand if it reaches, else the off hand).
    /// </summary>
    private readonly Dictionary<int, int> _readied = [];

    private CardInstance? ReadiedWeapon =>
        ActiveHero is { } hero && _readied.TryGetValue(hero.Id, out int id) && GameSession.HandWeapons(hero) is { Count: > 1 } weapons
            ? weapons.FirstOrDefault(card => card.Id == id)
            : null;

    private int? ReadiedWeaponId => ReadiedWeapon?.Id;

    /// <summary>The Rumble row's toggle: Auto, then each weapon in hand in turn.</summary>
    private void SwapWeapon()
    {
        if (Locked || ActiveHero is not { } hero || GameSession.HandWeapons(hero) is not { Count: > 1 } weapons)
        {
            return;
        }

        int next = ReadiedWeapon is { } current ? weapons.ToList().IndexOf(current) + 1 : 0;
        if (next < weapons.Count)
        {
            ReadyWeapon(hero, weapons[next]);
        }
        else
        {
            _readied.Remove(hero.Id);
            Log($"{hero.Name} attacks with whichever weapon reaches: the main hand first.", Palette.TrimBright);
        }

        RefreshPanels();
    }

    private void ReadyWeapon(Hero hero, CardInstance weapon)
    {
        _readied[hero.Id] = weapon.Id;
        Log($"{hero.Name} readies the {weapon.Definition.Name}: their attacks use it.", Palette.TrimBright);
    }

    /// <summary>For the hover text: what the active hero would hit the monster with from where they stand.</summary>
    private string HitWith(Monster monster) =>
        !Locked && ActiveHero is { IsCompanion: false } hero && hero.RoomId == _shownRoomId
        && _session.WeaponAgainst(monster, ReadiedWeaponId) is { Weapon: { } weapon, Bonus: var bonus }
            ? $" You'd hit it with your {weapon.Definition.Name}{(bonus > 0 ? $" (+{bonus})" : "")}."
            : "";

    private void SelectCard(int cardId)
    {
        if (Locked)
        {
            return;
        }

        // One of two weapons in hand: clicking it readies it for attacks.
        if (_selectedCardId != cardId && ActiveHero is { } holder && GameSession.HandWeapons(holder) is { Count: > 1 } weapons
            && weapons.FirstOrDefault(card => card.Id == cardId) is { } weapon)
        {
            ReadyWeapon(holder, weapon);
        }

        _selectedCardId = _selectedCardId == cardId ? null : cardId;
        _targeting = false;
        _board.SetBlast([]);
        RefreshPanels();
    }

    private void CancelSelection()
    {
        CloseRummage();
        CloseTrade();
        _stealing = false;
        _shoving = false;
        _leaping = false;
        _rumbling = false;
        _selectedCardId = null;
        _targeting = false;
        StopCasting();
        _board.SetBlast([]);
        RefreshPanels();
    }

    /// <summary>Offers both hands for a card that goes in either (the Boomerang), saying what each would stow.</summary>
    private void AskHand(Hero hero, CardInstance card)
    {
        (EquipSlot Slot, string Text, string? Blocked) Choice(EquipSlot slot, string name)
        {
            string stows = hero.CardsDisplacedBy(card.Definition, slot) is [var held, ..] ? $" (stows {held.Definition.Name})" : "";
            return (slot, name + stows, _session.CheckEquip(card.Id, slot));
        }

        _inventory.AskHand(card.Id, card.Definition.Name, [Choice(EquipSlot.MainHand, "Main hand"), Choice(EquipSlot.OffHand, "Off hand")]);
    }

    private void OnCardAction(CardAction action)
    {
        if (Locked || _selectedCardId is not int cardId || ActiveHero is not { } hero || hero.FindCard(cardId) is not var (card, _))
        {
            return;
        }

        switch (action)
        {
            case CardAction.Use when card.Definition.Reload is not null:
                Execute(new ReloadWeapon(hero.Id));
                break;
            case CardAction.Use when card.Definition.Effect?.Kind is CardEffectKind.Heal or CardEffectKind.CowardsFlight:
                Execute(new UseCard(hero.Id, cardId));
                break;
            case CardAction.Use:
                _targeting = true;
                Log($"Choose a target for the {card.Definition.Name}. Right-click to cancel.", Palette.Target with { A = 1 });
                RefreshPanels();
                break;
            case CardAction.Equip when card.Definition.Teaches is not null:
                ExecuteOdHand((heroId, dice, _, power) => new LearnSpell(heroId, cardId, dice, power));
                break;
            case CardAction.Equip when card.Definition.AltSlot is not null && hero.SlotOf(card) is null:
                AskHand(hero, card);
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
                Drop(card);
                break;
        }
    }
}
