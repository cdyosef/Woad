using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Od Hand: dice for picking locks, searching rooms, disarming traps, stealing and rousing allies Keeling Over. Like Rumble and
/// Wend, each roll uses one or more of the turn's Od Hand dice (plus the tools' value once a turn),
/// sixes earn Power Dice, and Luck can reroll them. None of it takes the turn's action. A check
/// succeeds when the total reaches its difficulty; a steal must beat the target's own roll.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Od Hand dice the active hero hasn't used this turn.</summary>
    public int OdHandDiceLeft => State.Turn is { } turn ? Math.Max(0, ActiveHero.EffectiveStats.OdHandDice - turn.OdHandDiceUsed) : 0;

    /// <summary>The tools' value the active hero can still add to an Od Hand roll this turn (0 once used).</summary>
    public int ToolsBonusLeft => State.Turn is { ToolsUsed: false } ? Math.Max(0, ActiveHero.EffectiveStats.Tools) : 0;

    /// <summary>Why the active hero can't pick a lock on the tile (their own doorway, or an adjacent chest), or null.</summary>
    public string? CheckPickLock(GridPosition target) => CheckOdHandStart() ?? FindLock(target, out _);

    /// <summary>Why the active hero can't search the room now, or null.</summary>
    public string? CheckSearch() => CheckOdHandStart();

    /// <summary>Why the active hero can't disarm a trap on the tile (their own doorway, or an adjacent tile or chest), or null.</summary>
    public string? CheckDisarm(GridPosition target) => CheckOdHandStart() ?? FindTrapToDisarm(target, out _);

    /// <summary>Why the active hero can't steal from the monster or hero, or null.</summary>
    public string? CheckSteal(int targetId) => CheckOdHandStart() ?? FindVictim(targetId, out _);

    private string? Handle(PickLock command, List<GameEvent> events)
    {
        OdHandCheck? check = null;
        if ((CheckOdHandCommand(command, out int dice) ?? FindLock(command.Target, out check)) is { } error)
        {
            return error;
        }

        RollOdHand(command, dice, check!, events);
        return null;
    }

    private string? Handle(Search command, List<GameEvent> events)
    {
        if (CheckOdHandCommand(command, out int dice) is { } error)
        {
            return error;
        }

        RollOdHand(command, dice, new OdHandCheck(OdHandCheckKind.Search, 0, ActiveHero.Position), events);
        return null;
    }

    private string? Handle(Disarm command, List<GameEvent> events)
    {
        OdHandCheck? check = null;
        if ((CheckOdHandCommand(command, out int dice) ?? FindTrapToDisarm(command.Target, out check)) is { } error)
        {
            return error;
        }

        RollOdHand(command, dice, check!, events);
        return null;
    }

    private string? Handle(Steal command, List<GameEvent> events)
    {
        OdHandCheck? check = null;
        if ((CheckOdHandCommand(command, out int dice) ?? FindVictim(command.TargetId, out check)) is { } error)
        {
            return error;
        }

        Turn.HasStolen = true;
        RollOdHand(command, dice, check!, events);
        return null;
    }

    /// <summary>A roll needs a turn with nothing pending and Od Hand dice left.</summary>
    private string? CheckOdHandStart()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        return OdHandDiceLeft < 1 ? "You've used all your Od Hand dice this turn." : null;
    }

    private string? CheckOdHandCommand(OdHandCommand command, out int dice)
    {
        dice = 0;
        int left = OdHandDiceLeft;
        if (left < 1)
        {
            return "You've used all your Od Hand dice this turn.";
        }

        dice = command.Dice ?? left;
        if (dice < 1 || dice > left)
        {
            return $"Choose 1 to {left} Od Hand dice.";
        }

        return CheckBankedPowerDice(ActiveHero, command.PowerDice);
    }

    /// <summary>The lock on the hero's own doorway, or on an adjacent unopened chest.</summary>
    private string? FindLock(GridPosition target, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (target == hero.Position && DoorAt(room, target) is { } passage)
        {
            if (!passage.IsLocked)
            {
                return "That door isn't locked.";
            }

            if (passage.NeedsBossKey)
            {
                return "The Boss Door can't be picked: it opens only with the Boss Key.";
            }

            check = new OdHandCheck(OdHandCheckKind.PickDoor, passage.Difficulty, target);
            return null;
        }

        if (room.Gates.TryGetValue(target, out int gateLock))
        {
            if (hero.Position.ManhattanDistanceTo(target) != 1)
            {
                return "Stand next to the gate to pick its lock.";
            }

            check = new OdHandCheck(OdHandCheckKind.PickGate, gateLock, target);
            return null;
        }

        if (room.Layout[target] == TileKind.Chest && !room.IsChestOpened(target))
        {
            if (hero.Position.ManhattanDistanceTo(target) != 1)
            {
                return "Stand next to the chest to pick its lock.";
            }

            if (room.ChestLockAt(target) is not int difficulty)
            {
                return "That chest isn't locked.";
            }

            check = new OdHandCheck(OdHandCheckKind.PickChest, difficulty, target);
            return null;
        }

        return "There's no lock there. Stand in a locked doorway, or next to a locked chest.";
    }

    /// <summary>A trap the hero knows of: on their own doorway, or on an adjacent tile or chest.</summary>
    private string? FindTrapToDisarm(GridPosition target, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        Trap? trap = target == hero.Position
            ? DoorAt(room, target)?.Trap
            : hero.Position.ManhattanDistanceTo(target) == 1
                ? room.TrapAt(target) ?? room.ChestTrapAt(target)
                : null;
        if (trap is not { Revealed: true })
        {
            return "There's no trap you know of there. Search to find traps, then disarm one next to you.";
        }

        check = new OdHandCheck(OdHandCheckKind.Disarm, trap.Difficulty, target);
        return null;
    }

    /// <summary>An adjacent monster not yet robbed, or an adjacent hero with something to take, once per turn.</summary>
    private string? FindVictim(int targetId, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (Turn.HasStolen)
        {
            return "You've already tried to steal this turn.";
        }

        if (room.FindMonster(targetId) is { } monster)
        {
            if (monster.Position.ManhattanDistanceTo(hero.Position) != 1)
            {
                return "Stand next to it to steal from it.";
            }

            if (monster.Robbed)
            {
                return $"The {monster.Definition.Name} has nothing left worth stealing.";
            }

            check = new OdHandCheck(OdHandCheckKind.Steal, 0, monster.Position, targetId);
            return null;
        }

        if (State.ActiveHeroesIn(room).FirstOrDefault(other => other.Id == targetId && other != hero) is { } victim)
        {
            if (victim.Position.ManhattanDistanceTo(hero.Position) != 1)
            {
                return "Stand next to them to steal from them.";
            }

            if (victim.Keys == 0 && victim.Backpack.Count == 0)
            {
                return $"{victim.Name} has nothing you could take (only keys and backpack cards can be stolen).";
            }

            check = new OdHandCheck(OdHandCheckKind.Steal, 0, victim.Position, targetId);
            return null;
        }

        return "There's nobody there to steal from.";
    }

    private static Passage? DoorAt(Room room, GridPosition tile) =>
        room.Passages.Where(door => room.DoorTile(door.Key) == tile && !door.Value.IsHidden).Select(door => door.Value).FirstOrDefault();

    /// <summary>Rolls the chosen dice (plus tools and banked Power Dice); it resolves once nothing is left to decide.</summary>
    private void RollOdHand(OdHandCommand command, int dice, OdHandCheck check, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        int bonus = command.UseTools ? ToolsBonusLeft : 0;
        Turn.OdHandDiceUsed += dice;
        Turn.ToolsUsed |= bonus > 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);

        DiceRoll roll = _dice.Roll(dice + command.PowerDice);
        events.Add(new OdHandRolled(hero.Id, check.Kind, roll, bonus, roll.Total + bonus, check.Difficulty, command.PowerDice));
        Turn.PendingRoll = new PendingRoll(hero.Id, roll, bonus, [], DamageSource.Weapon, check);
        EarnPowerDice(hero, Sixes(roll.Faces), events);
    }

    /// <summary>An attack or an Od Hand check, once its dice are final.</summary>
    private void ResolvePendingRoll(PendingRoll roll, List<GameEvent> events)
    {
        if (roll.Check is { } check)
        {
            ResolveCheck(roll, check, events);
        }
        else if (roll.Source == DamageSource.Shove)
        {
            ResolveShove(roll, events);
        }
        else
        {
            ResolveHeroAttack(roll, events);
        }
    }

    private void ResolveCheck(PendingRoll roll, OdHandCheck check, List<GameEvent> events)
    {
        Hero hero = State.GetHero(roll.HeroId);
        Room room = State.RoomOf(hero);
        int total = roll.Total;
        bool success = total >= check.Difficulty;
        switch (check.Kind)
        {
            case OdHandCheckKind.PickDoor when success && DoorAt(room, check.Target) is { } passage:
                passage.IsLocked = false;
                Direction side = room.Passages.First(door => door.Value == passage).Key;
                events.Add(new DoorUnlocked(hero.Id, room.Id, side, Picked: true, BossDoor: false));
                break;

            case OdHandCheckKind.PickChest when success:
                room.UnlockChest(check.Target);
                events.Add(new ChestUnlocked(hero.Id, check.Target, Picked: true));
                break;

            case OdHandCheckKind.PickGate when success:
                room.OpenGate(check.Target);
                events.Add(new GateOpened(hero.Id, room.Id, check.Target, Picked: true));
                break;

            case OdHandCheckKind.Disarm when success:
                if (check.Target == hero.Position && DoorAt(room, check.Target) is { } trapped)
                {
                    trapped.Trap = null;
                }
                else if (room.TrapAt(check.Target) is not null)
                {
                    room.SetTrap(check.Target, null);
                }
                else
                {
                    room.SetChestTrap(check.Target, null);
                }

                events.Add(new TrapDisarmed(hero.Id, check.Target));
                break;

            case OdHandCheckKind.Search:
                Reveal(hero, room, total, events);
                break;

            case OdHandCheckKind.Rouse:
                ResolveRouse(hero, roll, check, events);
                break;

            case OdHandCheckKind.Steal:
                ResolveSteal(hero, room, check.TargetId, total, events);
                break;

            default:
                events.Add(new CheckFailed(hero.Id, check.Kind));
                break;
        }
    }

    /// <summary>A search finds every secret in the room whose difficulty the total reaches.</summary>
    private void Reveal(Hero hero, Room room, int total, List<GameEvent> events)
    {
        var doors = new List<Direction>();
        var traps = new List<GridPosition>();
        foreach ((Direction side, Passage passage) in room.Passages)
        {
            if (passage.IsHidden && passage.Difficulty <= total)
            {
                passage.IsHidden = false;
                doors.Add(side);
            }

            if (!passage.IsHidden && passage.Trap is { Revealed: false } darts && darts.Difficulty <= total)
            {
                darts.Revealed = true;
                traps.Add(room.DoorTile(side));
            }
        }

        foreach ((GridPosition tile, Trap trap) in room.Traps.Concat(room.TilesOfKind(TileKind.Chest)
                     .Where(chest => room.ChestTrapAt(chest) is not null)
                     .Select(chest => new KeyValuePair<GridPosition, Trap>(chest, room.ChestTrapAt(chest)!))))
        {
            if (!trap.Revealed && trap.Difficulty <= total)
            {
                trap.Revealed = true;
                traps.Add(tile);
            }
        }

        List<GridPosition> gates = room.HiddenGates.Where(gate => gate.Value <= total).Select(gate => gate.Key).ToList();
        foreach (GridPosition gate in gates)
        {
            room.OpenGate(gate);
        }

        List<KeyValuePair<GridPosition, Cache>> stashes = room.Caches.Where(cache => !cache.Value.BehindWall && cache.Value.Difficulty <= total).ToList();
        events.Add(new SecretsFound(hero.Id, room.Id, doors, traps, stashes.Select(stash => stash.Key).ToList(), gates));
        foreach ((GridPosition tile, Cache cache) in stashes)
        {
            RevealCache(room, tile, cache, events);
        }
    }

    /// <summary>
    /// A steal is a contest: a monster rolls its defense dice, a hero their Od Hand dice plus tools.
    /// Beat it to take something: from a monster a key or a card (once per monster), from a hero
    /// one of their keys or backpack cards at random.
    /// </summary>
    private void ResolveSteal(Hero thief, Room room, int targetId, int total, List<GameEvent> events)
    {
        if (room.FindMonster(targetId) is { } monster)
        {
            DiceRoll guard = _dice.Roll(monster.Definition.DefenseDice);
            events.Add(new DefenseRolled(monster.Id, guard, 0, guard.Total));
            if (total <= guard.Total)
            {
                events.Add(new CheckFailed(thief.Id, OdHandCheckKind.Steal));
                return;
            }

            monster.Robbed = true;
            if (Chance(Content.GetTheme(State.ThemeId).Keys.Steal))
            {
                events.Add(new Stole(thief.Id, monster.Id, null, Key: true));
                GiveKeys(thief, 1, events);
                return;
            }

            var card = new CardInstance(State.NextId(), State.LootDeck.Draw(room.Floor), broughtInByHeroId: null);
            GiveCards(thief, room, [card], events);
            events.Add(new Stole(thief.Id, monster.Id, card.Id, Key: false));
            return;
        }

        Hero victim = State.GetHero(targetId);
        Stats stats = victim.EffectiveStats;
        DiceRoll hold = _dice.Roll(Math.Max(0, stats.OdHandDice));
        int tools = Math.Max(0, stats.Tools);
        events.Add(new DefenseRolled(victim.Id, hold, tools, hold.Total + tools));
        if (total <= hold.Total + tools || (victim.Keys == 0 && victim.Backpack.Count == 0))
        {
            events.Add(new CheckFailed(thief.Id, OdHandCheckKind.Steal));
            return;
        }

        // Each key counts as one choice, alongside each backpack card.
        int pick = _random.Loot.NextInt(0, victim.Keys + victim.Backpack.Count);
        if (pick < victim.Keys)
        {
            victim.Keys--;
            events.Add(new Stole(thief.Id, victim.Id, null, Key: true));
            GiveKeys(thief, 1, events);
            return;
        }

        CardInstance taken = victim.Backpack[pick - victim.Keys];
        victim.Remove(taken);
        GiveCards(thief, room, [taken], events);
        events.Add(new Stole(thief.Id, victim.Id, taken.Id, Key: false));
    }
}
