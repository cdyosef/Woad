using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The Iron Warden's ways. Its swing goes through a 3-tile arc toward its target (it can strike
/// diagonally): straight on, the tile ahead and the two beside it; on a diagonal, that corner and the
/// two tiles beside it. Everyone in the arc, monsters too, faces the same roll, and each 6 cleaves on
/// into one more creature beside it. Before moving it flings a chain at a hero up to 6 tiles off: a hit
/// drags them to its side, shackled, and a shackled hero can't move (no Wend) until a picked lock or a
/// key frees them (their own, or an ally's beside them).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Whether a monster that swings in an arc can strike a hero from a tile: beside it, diagonals too.</summary>
    private static bool ArcReaches(Monster monster, GridPosition from, Hero hero) =>
        monster.Definition.Arc && from.ChebyshevDistanceTo(hero.Position) == 1;

    /// <summary>The 3 tiles of an arc swung from <paramref name="from"/> toward <paramref name="toward"/>.</summary>
    internal static IReadOnlyList<GridPosition> ArcTiles(GridPosition from, GridPosition toward)
    {
        int dx = Math.Sign(toward.X - from.X), dy = Math.Sign(toward.Y - from.Y);
        if (dx != 0 && dy != 0)
        {
            return [new(from.X + dx, from.Y + dy), new(from.X + dx, from.Y), new(from.X, from.Y + dy)];
        }

        return dx != 0
            ? [new(from.X + dx, from.Y), new(from.X + dx, from.Y - 1), new(from.X + dx, from.Y + 1)]
            : [new(from.X, from.Y + dy), new(from.X - 1, from.Y + dy), new(from.X + 1, from.Y + dy)];
    }

    /// <summary>One swing through the arc toward <paramref name="target"/>, every hero and monster in it struck by the same roll.</summary>
    private void SwingArc(Room room, Monster monster, Hero target, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        IReadOnlyList<GridPosition> arc = ArcTiles(monster.Position, target.Position);
        List<Hero> heroes = [.. State.ActiveHeroesIn(room).Where(hero => arc.Contains(hero.Position))];
        List<Monster> monsters = [.. room.Monsters.Where(other => other != monster && other.Tiles.Any(arc.Contains))];
        int dice = monster.AttackDiceThisTurn + ProneBonus(target, monster.Position);
        if (dice < 1)
        {
            return;
        }

        (DiceRoll roll, IReadOnlyList<DieType> types, int powerDice) = RollMonsterDice(monster, dice);
        int bonus = definition.Weapon - AttackPenalty(monster.Conditions);
        int total = roll.Total + bonus;
        events.Add(new AttackRolled(monster.Id, roll, bonus, total, [.. heroes.Select(hero => hero.Id), .. monsters.Select(other => other.Id)], powerDice) { DieTypes = types });
        foreach (Hero hero in heroes.Where(hero => hero.IsActive))
        {
            DamageHero(room, hero, total, MonsterBane(monster), monster.Id, events);
        }

        foreach (Monster other in monsters.Where(other => room.FindMonster(other.Id) is not null))
        {
            DamageMonster(null, room, other, total, DamageSource.Monster, events);
        }

        // Each 6 carries the blow on into one more creature beside it that the arc missed.
        int sixes = roll.Faces.Count(face => face == GameRules.PowerDieFace);
        for (int i = 0; i < sixes; i++)
        {
            Hero? nextHero = State.ActiveHeroesIn(room)
                .Where(hero => !heroes.Contains(hero) && hero.Position.ChebyshevDistanceTo(monster.Position) == 1)
                .OrderBy(hero => hero.Seat).FirstOrDefault();
            if (nextHero is not null)
            {
                heroes.Add(nextHero);
                events.Add(new Cleaved(monster.Id, nextHero.Id));
                DamageHero(room, nextHero, total, MonsterBane(monster), monster.Id, events);
                continue;
            }

            Monster? nextMonster = room.Monsters
                .Where(other => other != monster && !monsters.Contains(other) && other.NearestTile(monster.Position).ChebyshevDistanceTo(monster.Position) == 1)
                .OrderBy(other => other.Id).FirstOrDefault();
            if (nextMonster is null)
            {
                break;
            }

            monsters.Add(nextMonster);
            events.Add(new Cleaved(monster.Id, nextMonster.Id));
            DamageMonster(null, room, nextMonster, total, DamageSource.Monster, events);
        }
    }

    /// <summary>
    /// The chain: at the nearest hero in sight within its range who isn't beside it or shackled already.
    /// A hit drags them to the free tile beside it nearest them, and shackles them there.
    /// </summary>
    private void FlingChain(Room room, Monster monster, List<GameEvent> events)
    {
        if (monster.Definition.Chain is not { } chain)
        {
            return;
        }

        Hero? target = State.ActiveHeroesIn(room)
            .Where(hero => hero.Shackled is null && hero.Position.ChebyshevDistanceTo(monster.Position) > 1
                           && Targeting.InRangeAndSight(room.Layout, monster.Position, hero.Position, chain.Range))
            .OrderBy(hero => hero.Position.ManhattanDistanceTo(monster.Position))
            .ThenBy(hero => hero.Seat)
            .FirstOrDefault();
        if (target is null)
        {
            return;
        }

        (DiceRoll roll, _, int powerDice) = RollMonsterDice(monster, chain.Dice, plain: true);
        events.Add(new AttackRolled(monster.Id, roll, 0, roll.Total, [target.Id], powerDice) { Chain = true });
        int dealt = DamageHero(room, target, roll.Total, MonsterBane(monster), monster.Id, events, ranged: true);
        if (dealt == 0 || !target.IsActive)
        {
            return;
        }

        GridPosition? beside = monster.Position.SurroundingTiles()
            .Where(tile => room.Layout.Contains(tile) && room.Layout[tile].IsWalkable() && !IsOccupied(room, tile))
            .OrderBy(tile => tile.ManhattanDistanceTo(target.Position))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
        if (beside is { } to)
        {
            events.Add(new Pulled(monster.Id, target.Id, target.Position, to));
            PushHero(null, room, target, to, events, announce: false);
        }

        if (target.IsActive)
        {
            target.Shackled = chain.Lock;
            events.Add(new HeroShackled(monster.Id, target.Id, chain.Lock));
        }
    }

    /// <summary>The hero on <paramref name="tile"/> (the active hero's own, or beside them) whose shackles could be freed, or null.</summary>
    private Hero? ShackledAt(Room room, GridPosition tile) =>
        State.HeroesIn(room).FirstOrDefault(hero => hero.Position == tile && hero.Shackled is not null && hero.IsInPlay);

    /// <summary>Why the active hero can't free <paramref name="targetId"/>'s shackles with a key now, or null.</summary>
    public string? CheckUnshackle(int targetId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId) is not { Shackled: int lockDifficulty } shackled)
        {
            return "There's nobody in shackles there.";
        }

        if (shackled.Position.ChebyshevDistanceTo(hero.Position) > 1)
        {
            return $"Stand next to {shackled.Name} to free them.";
        }

        return hero.Keys < 1 ? $"The shackles are locked: use a key, or pick the lock with Od Hand ({lockDifficulty}+)." : null;
    }

    private string? Handle(Unshackle command, List<GameEvent> events)
    {
        if (CheckUnshackle(command.TargetId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        hero.Keys--;
        events.Add(new KeyUsed(hero.Id, hero.Keys));
        Hero freed = State.GetHero(command.TargetId);
        freed.Shackled = null;
        events.Add(new HeroUnshackled(freed.Id, hero.Id, Picked: false));
        return null;
    }
}
