using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Keeling Over: a hero or companion at 0 hearts falls where they stand, keeping everything they
/// carry. Each of their turns is one Keel roll: a 6 is a mark of life and rolls a Power Die (another
/// mark on a 6); a 1 is a mark of death and rolls a Doom Die (another mark on a 1). Banked Power Dice
/// can be added, earning marks of life on sixes only. Three marks of life stand them up with a heart;
/// three marks of death kill them, leaving a tombstone that holds what they found. An adjacent ally
/// can rouse them with Od Hand dice, and monsters with nobody standing in reach strike at them.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't rouse the hero Keeling Over, or null.</summary>
    public string? CheckRouse(int targetId) => CheckOdHandStart() ?? FindKeeler(targetId, out _);

    /// <summary>Heroes Keeling Over the active hero could rouse right now.</summary>
    public IReadOnlyList<Hero> GetRousableAllies() =>
        State.Turn is null || State.ActiveHero is not { IsActive: true }
            ? []
            : State.HeroesIn(ActiveRoom).Where(other => other.IsKeelingOver && CheckRouse(other.Id) is null).ToList();

    private string? Handle(Rouse command, List<GameEvent> events)
    {
        OdHandCheck? check = null;
        if ((CheckOdHandCommand(command, out int dice) ?? FindKeeler(command.TargetId, out check)) is { } error)
        {
            return error;
        }

        RollOdHand(command, dice, check!, events);
        return null;
    }

    private string? FindKeeler(int targetId, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        if (State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId && other.IsKeelingOver) is not { } keeler)
        {
            return "There's nobody Keeling Over there.";
        }

        if (keeler.Position.ChebyshevDistanceTo(hero.Position) != 1)
        {
            return "Kneel beside them to rouse them.";
        }

        check = new OdHandCheck(OdHandCheckKind.Rouse, 0, keeler.Position, targetId);
        return null;
    }

    /// <summary>Every 6 of the total is a mark of life; every die showing 1 is a Doom Die on their next Keel roll.</summary>
    private void ResolveRouse(Hero hero, PendingRoll roll, OdHandCheck check, List<GameEvent> events)
    {
        Hero keeler = State.GetHero(check.TargetId);
        if (!keeler.IsKeelingOver)
        {
            return;
        }

        int life = Math.Min(roll.Total / GameRules.RousePerMark, GameRules.MarksToRise - keeler.LifeMarks);
        int doom = roll.Roll.Faces.Count(face => face == GameRules.DeathFace);
        keeler.LifeMarks += life;
        keeler.DoomDiceOwed += doom;
        events.Add(new HeroRoused(keeler.Id, hero.Id, life, doom, keeler.LifeMarks, keeler.DoomDiceOwed));
        if (keeler.LifeMarks >= GameRules.MarksToRise)
        {
            StandUp(keeler, events);
        }
    }

    private string? Handle(RollKeel command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (!hero.IsKeelingOver)
        {
            return "You're on your feet: only heroes Keeling Over roll the Keel Die.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        SpendBankedPowerDice(hero, command.PowerDice, events);
        int keel = RollOneDie();
        int life = 0, death = 0;
        int? bonus = null;
        if (keel == GameRules.PowerDieFace)
        {
            life++;
            bonus = RollOneDie();
            life += bonus == GameRules.PowerDieFace ? 1 : 0;
        }
        else if (keel == GameRules.DeathFace)
        {
            death++;
            bonus = RollOneDie();
            death += bonus == GameRules.DeathFace ? 1 : 0;
        }

        List<int> power = Enumerable.Range(0, command.PowerDice).Select(_ => RollOneDie()).ToList();
        List<int> doom = Enumerable.Range(0, hero.DoomDiceOwed).Select(_ => RollOneDie()).ToList();
        life += power.Count(face => face == GameRules.PowerDieFace);
        death += doom.Count(face => face == GameRules.DeathFace);
        hero.DoomDiceOwed = 0;
        hero.LifeMarks = Math.Min(GameRules.MarksToRise, hero.LifeMarks + life);
        hero.DeathMarks = Math.Min(GameRules.MarksToDie, hero.DeathMarks + death);
        events.Add(new KeelRolled(hero.Id, keel, bonus, power, doom, life, death, hero.LifeMarks, hero.DeathMarks));

        // Reaching both at once, life wins.
        if (hero.LifeMarks >= GameRules.MarksToRise)
        {
            StandUp(hero, events);
        }
        else if (hero.DeathMarks >= GameRules.MarksToDie)
        {
            Die(State.RoomOf(hero), hero, events);
        }

        if (State.Phase == RunPhase.InProgress)
        {
            events.Add(new TurnEnded(hero.Id));
            AdvanceTurn(hero.Seat, events);
        }

        return null;
    }

    /// <summary>A hero at 0 hearts falls where they are (or the nearest safe tile) and starts Keeling Over.</summary>
    private void KeelOver(Room room, Hero hero, string bane, List<GameEvent> events)
    {
        hero.Status = HeroStatus.KeelingOver;
        hero.LifeMarks = 0;
        hero.DeathMarks = 0;
        hero.DoomDiceOwed = 0;
        hero.Bane = bane;
        if (!room.Layout[hero.Position].IsWalkable())
        {
            hero.Position = LandingNear(room, hero.Position) ?? SafeTile(room, hero.Position);
        }

        events.Add(new HeroKeeledOver(hero.Id, hero.Position, bane));
    }

    private static void StandUp(Hero hero, List<GameEvent> events)
    {
        hero.Status = HeroStatus.Active;
        hero.LifeMarks = 0;
        hero.DeathMarks = 0;
        hero.DoomDiceOwed = 0;
        hero.Bane = null;
        hero.Hearts = Math.Min(hero.MaxHearts, GameRules.HeartsOnRising);
        events.Add(new HeroStoodUp(hero.Id, hero.Hearts));
    }

    /// <summary>
    /// A monster's turn against a hero Keeling Over, when nobody standing is in reach: one attack with
    /// every die, defended as usual. A hit is a mark of death.
    /// </summary>
    private void StrikeKeeler(Room room, Monster monster, Hero keeler, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        (DiceRoll roll, int powerDice) = RollMonsterDice(definition.AttackDice);
        int total = roll.Total + definition.Weapon;
        events.Add(new AttackRolled(monster.Id, roll, definition.Weapon, total, [keeler.Id], powerDice));
        DamageHero(room, keeler, total, MonsterBane(monster), monster.Id, events);
    }

    private void MarkForDeath(Room room, Hero keeler, string bane, int attackerId, List<GameEvent> events)
    {
        keeler.DeathMarks = Math.Min(GameRules.MarksToDie, keeler.DeathMarks + 1);
        keeler.Bane = bane;
        events.Add(new KeelerStruck(keeler.Id, attackerId, keeler.DeathMarks));
        if (keeler.DeathMarks >= GameRules.MarksToDie)
        {
            Die(room, keeler, events);
        }
    }

    /// <summary>
    /// Three marks of death: the hero is out of the run. A tombstone marks the spot, holding everything
    /// they found this run (and their keys); what they brought in goes home with them.
    /// </summary>
    private void Die(Room room, Hero hero, List<GameEvent> events)
    {
        hero.Status = HeroStatus.Dead;
        List<CardInstance> dropped = hero.AllCards.Where(card => card.BroughtInByHeroId != hero.Id).ToList();
        foreach (CardInstance card in dropped)
        {
            hero.Remove(card);
        }

        GridPosition spot = SafeTile(room, hero.Position);
        room.AddLoot(spot, dropped);
        var tombstone = new Tombstone(spot, hero.Id, hero.Name, hero.Bane ?? "Lost in the dungeon", HereLies: _random.Behavior.CoinFlip());
        room.AddTombstone(tombstone);
        events.Add(new HeroDied(hero.Id, tombstone, dropped.Select(card => card.Id).ToList()));
        DropHeroKeys(room, hero, events);
        EndRunIfOver(events);
    }

    /// <summary>"Slain by a Goblin", "Slain by the Goblin King".</summary>
    private static string MonsterBane(Monster monster)
    {
        MonsterDefinition definition = monster.Definition;
        string article = definition.Boss || definition.Miniboss ? "the" : "AEIOU".Contains(definition.Name[0]) ? "an" : "a";
        return $"Slain by {article} {definition.Name}";
    }

    private static string AllyBane(Hero attacker, DamageSource source) => source switch
    {
        DamageSource.Bomb => $"Blown up by {attacker.Name}",
        DamageSource.Spell => $"Blasted by {attacker.Name}'s magic",
        _ => $"Struck down by {attacker.Name}",
    };

    private static string TrapBane(TrapKind kind) => kind switch
    {
        TrapKind.Spikes => "Impaled on spikes",
        TrapKind.TrapDoor => "Fell through a trap door",
        TrapKind.Needle => "Pricked by a poisoned needle",
        _ => "Struck by darts",
    };
}
