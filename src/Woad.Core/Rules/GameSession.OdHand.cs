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

    /// <summary>The tools' value the active hero can still add to a lock-picking roll this turn (0 once used).</summary>
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

        // Fiddling with a mimic's "lock" wakes it.
        if (check!.Kind == OdHandCheckKind.PickChest && ActiveRoom.MimicAt(check.Target) is not null)
        {
            WakeMimic(ActiveRoom, check.Target, ActiveHero, events);
            return null;
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

        ActiveHero.SearchedRoomIds.Add(ActiveRoom.Id);
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

        if (ShackledAt(room, target) is { Shackled: int shackles } prisoner)
        {
            if (prisoner.Position.ChebyshevDistanceTo(hero.Position) > 1)
            {
                return $"Stand next to {prisoner.Name} to pick the lock of their shackles.";
            }

            check = new OdHandCheck(OdHandCheckKind.PickShackles, shackles, target, prisoner.Id);
            return null;
        }

        if (room.MerchantAt(target) is { Chained: true } chained)
        {
            if (hero.Position.ChebyshevDistanceTo(target) != 1)
            {
                return $"Stand next to {chained.Name} to pick the lock of their chains.";
            }

            check = new OdHandCheck(OdHandCheckKind.PickChains, chained.LockDifficulty, target, chained.Id);
            return null;
        }

        return "There's no lock there. Stand in a locked doorway, or next to a locked chest.";
    }

    /// <summary>A lit bomb beside them (or under them) to defuse, or a trap they know of: on their own doorway, or on an adjacent tile or chest.</summary>
    private string? FindTrapToDisarm(GridPosition target, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.BombAt(target) is { Lit: true } && hero.Position.ChebyshevDistanceTo(target) <= 1)
        {
            check = new OdHandCheck(OdHandCheckKind.DefuseBomb, GameRules.DefuseDifficulty, target);
            return null;
        }

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

        if (room.FindMerchant(targetId) is { } merchant)
        {
            if (merchant.Position.ChebyshevDistanceTo(hero.Position) != 1)
            {
                return $"Stand next to {merchant.Name} to steal from them.";
            }

            if (merchant.Robbed || merchant.Stock.Count == 0)
            {
                return $"{merchant.Name} is watching their wares too closely now.";
            }

            check = new OdHandCheck(OdHandCheckKind.Steal, 0, merchant.Position, targetId);
            return null;
        }

        if (room.FindMonster(targetId) is { } monster)
        {
            if (monster.NearestTile(hero.Position).ManhattanDistanceTo(hero.Position) != 1)
            {
                return "Stand next to it to steal from it.";
            }

            if (monster.Robbed)
            {
                return $"The {monster.Definition.Name} has nothing left worth stealing.";
            }

            check = new OdHandCheck(OdHandCheckKind.Steal, 0, monster.NearestTile(hero.Position), targetId);
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

    /// <summary>Locks: doors, chests and gates. Only these take tools.</summary>
    private static bool IsLock(OdHandCheckKind kind) => kind is OdHandCheckKind.PickDoor or OdHandCheckKind.PickChest or OdHandCheckKind.PickGate or OdHandCheckKind.PickChains;

    /// <summary>
    /// Rolls the chosen dice, plus on a lock the tools (their value and their dice, whose sixes roll
    /// straight in), plus banked Power Dice; it resolves once nothing is left to decide.
    /// </summary>
    private void RollOdHand(OdHandCommand command, int dice, OdHandCheck check, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        int tools = command.UseTools && IsLock(check.Kind) ? ToolsBonusLeft : 0;
        int bonus = tools - RollPenalty(hero.Conditions) + (check.Kind == OdHandCheckKind.Learn ? Math.Max(0, hero.EffectiveStats.Magic) : 0);
        // Lockpicks' own dice on a lock; held torches' on a search.
        int toolDice = (tools > 0 ? Math.Max(0, hero.EffectiveStats.ToolDice) : 0) + (check.Kind == OdHandCheckKind.Search ? hero.TorchesHeld : 0);
        Turn.OdHandDiceUsed += dice;
        Turn.ToolsUsed |= tools > 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);

        DiceRoll rolled = _dice.Roll(dice + command.PowerDice);
        (int[] tool, int crits) = RollBonusDice(toolDice);
        var roll = new DiceRoll([.. rolled.Faces.Take(dice), .. tool[..toolDice], .. rolled.Faces.Skip(dice), .. tool[toolDice..]]);
        events.Add(new OdHandRolled(hero.Id, check.Kind, roll, bonus, roll.Total + bonus, check.Difficulty, command.PowerDice + crits) { ToolDice = toolDice });
        if (crits > 0)
        {
            events.Add(new CritRolled(hero.Id, crits));
        }

        Turn.PendingRoll = new PendingRoll(hero.Id, roll, bonus, [], DamageSource.Weapon, check) { NonPowerDice = dice + toolDice, UsedTools = tools > 0 };
        EarnPowerDice(hero, Sixes(rolled.Faces), events);
    }

    /// <summary>A tool's or weapon's own dice: every 6 rolls one more die straight in (which can come up 6 again).</summary>
    private (int[] Faces, int Crits) RollBonusDice(int dice)
    {
        var faces = new List<int>(_dice.Roll(dice).Faces);
        int crits = 0;
        for (int pending = faces.Count(face => face == GameRules.PowerDieFace); pending > 0; pending--)
        {
            int face = RollOneDie();
            faces.Add(face);
            crits++;
            if (face == GameRules.PowerDieFace)
            {
                pending++;
            }
        }

        return ([.. faces], crits);
    }

    /// <summary>A failed lock-picking roll with a 1 among its non-Power dice snaps one of the lockpicks used.</summary>
    private void SnapPick(Hero hero, PendingRoll roll, bool success, List<GameEvent> events)
    {
        if (success || !roll.UsedTools || !roll.Roll.Faces.Take(roll.NonPowerDice).Contains(1)
            || hero.EquippedCards.FirstOrDefault(card => card.Definition.Modifiers.Tools > 0) is not { } picks)
        {
            return;
        }

        if (picks.Count > 1)
        {
            picks.Count--;
            picks.BroughtInCount = Math.Min(picks.BroughtInCount, picks.Count);
        }
        else
        {
            hero.Remove(picks);
        }

        events.Add(new LockpickSnapped(hero.Id, picks.Id, hero.FindCard(picks.Id) is null ? 0 : picks.Count));
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
        SnapPick(hero, roll, success, events);
        switch (check.Kind)
        {
            case OdHandCheckKind.PickDoor when success && DoorAt(room, check.Target) is { } passage:
                passage.IsLocked = false;
                Direction side = room.Passages.First(door => door.Value == passage).Key;
                events.Add(new DoorUnlocked(hero.Id, room.Id, side, Picked: true, BossDoor: false));
                break;

            case OdHandCheckKind.PickChains when success && room.FindMerchant(check.TargetId) is { Chained: true } freed:
                Free(hero, freed, picked: true, events);
                break;

            case OdHandCheckKind.PickShackles when success && State.GetHero(check.TargetId) is { Shackled: not null } prisoner:
                prisoner.Shackled = null;
                events.Add(new HeroUnshackled(prisoner.Id, hero.Id, Picked: true));
                break;

            case OdHandCheckKind.PickChest when success:
                room.UnlockChest(check.Target);
                events.Add(new ChestUnlocked(hero.Id, check.Target, Picked: true));
                break;

            case OdHandCheckKind.PickGate when success:
                room.OpenGate(check.Target);
                events.Add(new GateOpened(hero.Id, room.Id, check.Target, Picked: true));
                break;

            case OdHandCheckKind.DefuseBomb when room.BombAt(check.Target) is { Lit: true } bomb:
                if (success)
                {
                    Defuse(room, bomb, doused: false, events);
                }
                else
                {
                    events.Add(new CheckFailed(hero.Id, check.Kind));
                    Explode(room, bomb, events, setOffBy: hero);
                }

                break;

            case OdHandCheckKind.Disarm when success:
                if (check.Target == hero.Position && DoorAt(room, check.Target) is { } trapped)
                {
                    trapped.Trap = null;
                }
                else if (room.TrapAt(check.Target) is { } floorTrap)
                {
                    room.SetTrap(check.Target, null);
                    if (floorTrap.Kind == TrapKind.BombTrap)
                    {
                        // Disarmed, a bomb trap gives up its bomb unlit.
                        events.Add(new TrapDisarmed(hero.Id, check.Target));
                        PlaceBomb(room, check.Target, lit: false, null, events);
                        break;
                    }
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

            case OdHandCheckKind.Tend:
                ResolveTend(hero, State.GetHero(check.TargetId), success, events);
                break;

            case OdHandCheckKind.Learn:
                ResolveLearning(hero, room, check.SpellId!, success, events);
                break;

            case OdHandCheckKind.Steal:
                ResolveSteal(hero, room, check.TargetId, total, events);
                break;

            case OdHandCheckKind.Retreat:
                ResolveRetreat(hero, room, total, events);
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

        // A sharp eye sees the teeth: the mimic gives up its disguise without getting a bite in.
        foreach (GridPosition mimic in room.Mimics.Where(mimic => mimic.Value <= total).Select(mimic => mimic.Key).ToList())
        {
            WakeMimic(room, mimic, null, events);
        }
    }

    /// <summary>
    /// A steal is a contest: a monster rolls its defense dice, a hero their Od Hand dice plus tools.
    /// Beat it to take something: from a monster a key or a card (once per monster), from a hero
    /// one of their keys or backpack cards at random.
    /// </summary>
    private void ResolveSteal(Hero thief, Room room, int targetId, int total, List<GameEvent> events)
    {
        if (room.FindMerchant(targetId) is { } merchant)
        {
            StealFromMerchant(thief, room, merchant, total, events);
            return;
        }

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
        events.Add(new DefenseRolled(victim.Id, hold, 0, hold.Total));
        if (total <= hold.Total || (victim.Keys == 0 && victim.Backpack.Count == 0))
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

        CardInstance taken = TakeFromHero(victim, victim.Backpack[pick - victim.Keys], 1);
        GiveCards(thief, room, [taken], events);
        events.Add(new Stole(thief.Id, victim.Id, taken.Id, Key: false));
    }
}
