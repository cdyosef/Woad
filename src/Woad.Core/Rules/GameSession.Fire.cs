using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Fire and lying down. Fire dice (the Flame Scroll, Cinder Imps, Fire Beetles) set a target alight
/// on any 6, hit or miss. At the start of its turn a burning hero or monster is attacked by its fire
/// dice (defended as usual); each 6 then adds a fire die and spreads a die to a random neighbor, and
/// each 1 snuffs one out. Water puts fire out, and so does stopping, dropping and rolling, which takes
/// a hero's whole turn and leaves them prone. Prone (1 step to lie down, 2 to get up) every step costs
/// double; melee attacks on a prone hero roll an extra die, ranged ones face an extra defense die, and
/// prone in deep water is diving: out of reach of ranged attacks altogether.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Lying in deep water: nothing can reach the hero from a distance.</summary>
    public bool IsDiving(Hero hero) => Diving(State.RoomOf(hero), hero);

    /// <summary>Why the active hero can't lie down now, or null.</summary>
    public string? CheckGoProne()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish the roll first.";
        }

        return ActiveHero.Prone ? "You're already lying down." : CheckCanMove(GameRules.ProneCost);
    }

    /// <summary>Why the active hero can't get up now, or null.</summary>
    public string? CheckGetUp()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish the roll first.";
        }

        return !ActiveHero.Prone ? "You're already on your feet." : CheckCanMove(GameRules.GetUpCost) is { } error
            ? $"Getting up takes {GameRules.GetUpCost} steps. {error}"
            : null;
    }

    /// <summary>Why the active hero can't stop, drop and roll now, or null.</summary>
    public string? CheckStopDropRoll()
    {
        if (State.Turn is not { KeelingOver: false } turn)
        {
            return "It isn't your turn.";
        }

        if (ActiveHero.FireDice == 0)
        {
            return "You're not on fire.";
        }

        bool fresh = turn is { MovementRoll: null, MoveDiceUsed: 0, AttackDiceUsed: 0, OdHandDiceUsed: 0, HasActed: false, UsedBeltItem: false, UsedFreeLoot: false, HasStolen: false, PendingRoll: null };
        return fresh ? null : "Stopping, dropping and rolling takes your whole turn: do it before anything else.";
    }

    /// <summary>Each step costs double while the active hero is prone.</summary>
    private int Crawl => ActiveHero.Prone ? GameRules.CrawlFactor : 1;

    /// <summary>A melee attack (from right beside them) on a prone hero rolls an extra die.</summary>
    private static int ProneBonus(Hero target, GridPosition from) =>
        target.IsProne && target.Position.ChebyshevDistanceTo(from) <= 1 ? 1 : 0;

    /// <summary>A monster's fire attack: any 6 on its roll sets each of its targets alight, hit or miss.</summary>
    private void IgniteFromMonster(Monster monster, IReadOnlyList<Hero> targets, DiceRoll roll, List<GameEvent> events)
    {
        if (!monster.Definition.FireAttack || !roll.Faces.Contains(GameRules.PowerDieFace))
        {
            return;
        }

        foreach (Hero target in targets)
        {
            Ignite(target, monster.Id, events);
        }
    }

    private static bool Diving(Room room, Hero hero) => hero.IsProne && room.Layout[hero.Position] == TileKind.DeepWater;

    private static bool InWater(Room room, GridPosition tile) => room.Layout[tile] is TileKind.ShallowWater or TileKind.DeepWater;

    private string? Handle(GoProne command, List<GameEvent> events)
    {
        if (CheckGoProne() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Turn.MovementPoints -= GameRules.ProneCost;
        Turn.HasMoved = true;
        hero.Prone = true;
        events.Add(new WentProne(hero.Id, IsDiving(hero)));
        return null;
    }

    private string? Handle(GetUp command, List<GameEvent> events)
    {
        if (CheckGetUp() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Turn.MovementPoints -= GameRules.GetUpCost;
        Turn.HasMoved = true;
        hero.Prone = false;
        events.Add(new GotUp(hero.Id));
        return null;
    }

    private string? Handle(StopDropRoll command, List<GameEvent> events)
    {
        if (CheckStopDropRoll() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        hero.FireDice = 0;
        events.Add(new FireOut(hero.Id, FireOutCause.Rolled));
        if (!hero.Prone)
        {
            hero.Prone = true;
            events.Add(new WentProne(hero.Id, IsDiving(hero)));
        }

        return Handle(new EndTurn(hero.Id), events);
    }

    /// <summary>Sets a hero alight, or adds a die to their fire. Not in water.</summary>
    private void Ignite(Hero hero, int byId, List<GameEvent> events)
    {
        if (!hero.IsInPlay || InWater(State.RoomOf(hero), hero.Position))
        {
            return;
        }

        hero.FireDice++;
        events.Add(new Ignited(hero.Id, hero.FireDice, byId));
    }

    /// <summary>Sets a monster alight, or adds a die to its fire. Not fire-immune ones, nor in water.</summary>
    /// <param name="creditId">The hero who gets the kill if it burns to death.</param>
    private static void Ignite(Room room, Monster monster, int byId, int? creditId, List<GameEvent> events)
    {
        if (monster.Definition.FireImmune || room.FindMonster(monster.Id) is null || InWater(room, monster.Position))
        {
            return;
        }

        monster.FireDice++;
        monster.IgnitedBy = creditId ?? monster.IgnitedBy;
        events.Add(new Ignited(monster.Id, monster.FireDice, byId));
    }

    /// <summary>A hero's fire attack: with any 6 among its dice, every target still standing catches fire, hit or miss.</summary>
    private void IgniteTargets(Hero attacker, Room room, PendingRoll attack, List<GameEvent> events)
    {
        if (!attack.Fire || !attack.Roll.Faces.Contains(GameRules.PowerDieFace))
        {
            return;
        }

        foreach (int targetId in attack.TargetIds)
        {
            if (room.FindMonster(targetId) is { } monster)
            {
                Ignite(room, monster, attacker.Id, attacker.Id, events);
            }
            else if (State.HeroesIn(room).FirstOrDefault(ally => ally.Id == targetId) is { } ally)
            {
                Ignite(ally, attacker.Id, events);
            }
        }
    }

    /// <summary>
    /// The start of a burning hero's turn: their fire attacks them. Returns false if that ended their
    /// turn (they keeled over or died), in which case play has moved on.
    /// </summary>
    private bool BurnAtTurnStart(Hero hero, List<GameEvent> events)
    {
        Room room = State.RoomOf(hero);
        if (hero.FireDice == 0 || DouseInWater(room, hero, events))
        {
            return true;
        }

        // Burning on top of a bomb sets it off.
        if (room.BombAt(hero.Position) is { } underHero)
        {
            Explode(room, underHero, events);
            if (!hero.IsInPlay || State.Phase != RunPhase.InProgress)
            {
                return false;
            }
        }

        bool keeling = hero.IsKeelingOver;
        DiceRoll roll = _dice.Roll(hero.FireDice);
        hero.FireDice = FireLeft(hero.FireDice, roll);
        events.Add(new FireBurned(hero.Id, roll, hero.FireDice));
        DamageHero(room, hero, roll.Total, "Burned to death", 0, events);
        Spread(room, hero.Position, hero.Id, hero.Id, roll, events);
        if (hero.IsInPlay && hero.FireDice == 0)
        {
            events.Add(new FireOut(hero.Id, FireOutCause.Snuffed));
        }

        return hero.IsInPlay && hero.IsKeelingOver == keeling;
    }

    /// <summary>The start of a burning monster's turn. Returns false if it burned to death.</summary>
    private bool BurnAtTurnStart(Room room, Monster monster, List<GameEvent> events)
    {
        if (monster.FireDice == 0)
        {
            return true;
        }

        if (InWater(room, monster.Position))
        {
            monster.FireDice = 0;
            events.Add(new FireOut(monster.Id, FireOutCause.Water));
            return true;
        }

        if (room.BombAt(monster.Position) is { } underMonster)
        {
            Explode(room, underMonster, events);
            if (room.FindMonster(monster.Id) is null || State.Phase != RunPhase.InProgress)
            {
                return false;
            }
        }

        DiceRoll roll = _dice.Roll(monster.FireDice);
        monster.FireDice = FireLeft(monster.FireDice, roll);
        events.Add(new FireBurned(monster.Id, roll, monster.FireDice));
        Hero? credit = monster.IgnitedBy is int heroId && State.GetHero(heroId) is { IsInPlay: true } setter ? setter : null;
        DamageMonster(credit, room, monster, roll.Total, DamageSource.Fire, events);
        Spread(room, monster.Position, monster.Id, monster.IgnitedBy, roll, events);
        bool alive = room.FindMonster(monster.Id) is not null;
        if (alive && monster.FireDice == 0)
        {
            events.Add(new FireOut(monster.Id, FireOutCause.Snuffed));
        }

        return alive;
    }

    /// <summary>Each 6 on the burn roll adds a fire die, each 1 takes one away.</summary>
    private static int FireLeft(int dice, DiceRoll roll) =>
        Math.Max(0, dice + roll.Faces.Count(face => face == GameRules.PowerDieFace) - roll.Faces.Count(face => face == GameRules.DeathFace));

    /// <summary>Each 6 on a burn roll also sets a random neighbor alight (diagonals too): hero, companion or monster, whatever side.</summary>
    private void Spread(Room room, GridPosition from, int fromId, int? creditId, DiceRoll roll, List<GameEvent> events)
    {
        for (int i = 0; i < roll.Faces.Count(face => face == GameRules.PowerDieFace); i++)
        {
            var neighbors = room.Monsters
                .Where(monster => monster.Id != fromId && !monster.Definition.FireImmune && monster.Position.ChebyshevDistanceTo(from) == 1 && !InWater(room, monster.Position))
                .Select(monster => (monster.Id, Monster: (Monster?)monster, Hero: (Hero?)null))
                .Concat(State.HeroesIn(room)
                    .Where(hero => hero.Id != fromId && hero.Position.ChebyshevDistanceTo(from) == 1 && !InWater(room, hero.Position))
                    .Select(hero => (hero.Id, Monster: (Monster?)null, Hero: (Hero?)hero)))
                .OrderBy(neighbor => neighbor.Id)
                .ToList();
            if (neighbors.Count == 0)
            {
                return;
            }

            var catches = _random.Behavior.Pick(neighbors);
            if (catches.Monster is { } monster)
            {
                monster.FireDice++;
                monster.IgnitedBy = creditId ?? monster.IgnitedBy;
                events.Add(new FireSpread(fromId, monster.Id, monster.FireDice));
            }
            else if (catches.Hero is { } hero)
            {
                hero.FireDice++;
                events.Add(new FireSpread(fromId, hero.Id, hero.FireDice));
            }
        }
    }

    /// <summary>A burning hero in water is put out. True if it was.</summary>
    private static bool DouseInWater(Room room, Hero hero, List<GameEvent> events)
    {
        if (hero.FireDice == 0 || !InWater(room, hero.Position))
        {
            return false;
        }

        hero.FireDice = 0;
        events.Add(new FireOut(hero.Id, FireOutCause.Water));
        return true;
    }

    /// <summary>Walking through water puts a hero's fire out, even without stopping there.</summary>
    private static void DouseOnPath(Room room, Hero hero, IReadOnlyList<GridPosition> path, List<GameEvent> events)
    {
        if (hero.FireDice > 0 && path.Any(tile => InWater(room, tile)))
        {
            hero.FireDice = 0;
            events.Add(new FireOut(hero.Id, FireOutCause.Water));
        }
    }

    /// <summary>After every command: anyone burning who ended up in water (shoved, leaping, falling) is put out.</summary>
    private void DouseEveryoneInWater(List<GameEvent> events)
    {
        foreach (Hero hero in State.Heroes.Where(hero => hero.IsInPlay))
        {
            DouseInWater(State.RoomOf(hero), hero, events);
        }

        foreach (Room room in State.Rooms.Values)
        {
            foreach (Monster monster in room.Monsters.Where(monster => monster.FireDice > 0 && InWater(room, monster.Position)))
            {
                monster.FireDice = 0;
                events.Add(new FireOut(monster.Id, FireOutCause.Water));
            }
        }
    }
}
