using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Things thrown: an ogre's Rocks and Boulders, and a Boulder in a hero's hands. Some ogres carry a few Rocks, a
/// few a Boulder; with nobody in its club's reach but someone in throwing range (twice as far as anyone else
/// throws), it throws instead of closing in. A Rock lands whole on a 6, else shatters. A Boulder rolls on 1d6
/// tiles past where it was thrown, the way it was going, striking whoever it meets with the same roll: past them
/// if it hits, or if they only just turn it aside; stopped dead by a defense of twice its roll, or by a wall. Two
/// 1s on its weapon and Earth dice break it, into Rocks, maybe coins or a gem; on a critical roll (a 6) it bursts,
/// flinging them onto the tiles around, each Rock striking whoever's there. With nothing left to throw, an ogre
/// beside what it threw picks it back up, in place of moving.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The chance (%) a broken Boulder leaves coins, and a gem.</summary>
    public const int BoulderCoinChance = 25;

    public const int BoulderGemChance = 8;

    /// <summary>A monster's roll at a hero: what came up, so a thrown Boulder can roll on with it.</summary>
    private sealed record MonsterAttackRoll(DiceRoll Roll, int Total, IReadOnlyList<int> WeaponFaces, IReadOnlyList<int> ExtraFaces);

    /// <summary>Hands out what each monster carries to throw (an ogre's Rocks or Boulder), on its own dice.</summary>
    private static void GiveThrowables(Monster monster, IRandomSource gear, ContentCatalog content, GameState state)
    {
        if (monster.Definition.Throws.Count == 0)
        {
            return;
        }

        int roll = gear.NextInt(0, 100);
        foreach (ThrowableGear carried in monster.Definition.Throws)
        {
            if (roll < carried.Chance)
            {
                int count = gear.NextInt(carried.Min, carried.Max + 1);
                monster.Holding = new CardInstance(state.NextId(), content.GetCard(carried.Card), broughtInByHeroId: null, count);
                return;
            }

            roll -= carried.Chance;
        }
    }

    /// <summary>How far a monster throws <paramref name="missile"/>: twice as far for an ogre.</summary>
    public static int ThrowRange(Monster monster, CardDefinition missile) => missile.ThrownRange * (monster.Definition.ThrowsFar ? 2 : 1);

    /// <summary>Whether two 1s on a weapon's own dice break it: any two for a Big Club, the weapon die and the Earth die for a Boulder.</summary>
    private static bool OwnDiceBreak(CardDefinition weapon, IReadOnlyList<int> weaponFaces, IReadOnlyList<int> extraFaces) =>
        weapon.BreaksOnDoubleOne
        && (weapon.ExtraDie is null ? weaponFaces.Count(face => face == 1) >= 2 : weaponFaces.FirstOrDefault() == 1 && extraFaces.FirstOrDefault() == 1);

    /// <summary>
    /// With nobody in reach of its blows, a monster carrying something to throw throws it at whoever it can,
    /// the weakest first; that's its turn. False when it doesn't.
    /// </summary>
    private bool ThrowInstead(Room room, Monster monster, IReadOnlyList<Hero> standing, List<GameEvent> events)
    {
        if (monster.Holding is not { } held || IsBlind(monster.Conditions) || ChooseTarget(room, monster, monster.Position, standing) is not null)
        {
            return false;
        }

        CardDefinition kind = held.Definition;
        int range = ThrowRange(monster, kind);
        if (standing
                .Where(hero => !Diving(room, hero) && Targeting.CanAttack(room.Layout, AttackPattern.Ranged, range, monster.NearestTile(hero.Position), hero.Position))
                .OrderBy(hero => hero.Hearts)
                .ThenBy(hero => hero.Seat)
                .FirstOrDefault() is not { } target)
        {
            return false;
        }

        CardInstance missile = held;
        if (held.Count > 1)
        {
            missile = held.Split(State.NextId(), 1);
        }
        else
        {
            monster.Holding = null;
        }

        GridPosition from = monster.NearestTile(target.Position);
        GridPosition at = target.Position;
        events.Add(new MonsterThrew(monster.Id, target.Id, kind.Name, from, at));
        int bonus = monster.Definition.Weapon + kind.Modifiers.Weapon + kind.AttackBonus;
        if (RollMonsterAttack(room, monster, monster.AttackDiceThisTurn, bonus, [target], events, weaponDice: kind.Modifiers.WeaponDice, thrown: kind) is not { } thrown)
        {
            room.AddLoot(at, [missile]);
            return true;
        }

        bool critical = thrown.Roll.Faces.Contains(GameRules.PowerDieFace);
        if (kind.Rolls)
        {
            BoulderLands(room, null, monster, from, at, missile, thrown.Total, critical, thrown.WeaponFaces, thrown.ExtraFaces, events);
        }
        else if (critical)
        {
            room.AddLoot(at, [missile]);
            events.Add(new StoneLanded(monster.Id, missile.Id, room.Id, at));
        }
        else
        {
            events.Add(new ThrownShattered(monster.Id, kind.Name));
        }

        return true;
    }

    /// <summary>
    /// With nothing to throw and nobody in reach, a monster that throws picks back up something it could throw
    /// lying under it or beside it: that's its move. False when it doesn't.
    /// </summary>
    private bool Regrab(Room room, Monster monster, IReadOnlyList<Hero> standing, List<GameEvent> events)
    {
        if (monster.Holding is not null || monster.Definition.Throws.Count == 0 || ChooseTarget(room, monster, monster.Position, standing) is not null)
        {
            return false;
        }

        HashSet<string> throwables = [.. monster.Definition.Throws.Select(carried => carried.Card)];
        foreach (GridPosition tile in monster.Tiles.SelectMany(tile => tile.SurroundingTiles().Prepend(tile)).Distinct().Where(room.Layout.Contains))
        {
            if (room.LootAt(tile).FirstOrDefault(card => throwables.Contains(card.Definition.Id)) is { } found && room.TakeCard(tile, found.Id) is { } taken)
            {
                monster.Holding = taken;
                events.Add(new MonsterPickedUp(monster.Id, room.Id, tile, taken.Definition.Name, taken.Count));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A thrown Boulder, after the blow: broken by a double 1 on its own dice (bursting on a critical), or rolling
    /// on 1d6 tiles the way it was thrown, striking whoever of the thrower's foes it meets with the same roll.
    /// </summary>
    private void BoulderLands(Room room, Hero? heroThrower, Monster? monsterThrower, GridPosition from, GridPosition at, CardInstance boulder, int total, bool critical, IReadOnlyList<int> weaponFaces, IReadOnlyList<int> extraFaces, List<GameEvent> events)
    {
        if (OwnDiceBreak(boulder.Definition, weaponFaces, extraFaces))
        {
            BreakBoulder(room, heroThrower, monsterThrower, at, critical, events);
            return;
        }

        int dx = Math.Sign(at.X - from.X), dy = Math.Sign(at.Y - from.Y);
        List<GridPosition> path = [];
        GridPosition position = at;
        int tiles = dx == 0 && dy == 0 ? 0 : RollOneDie();
        for (int i = 0; i < tiles && State.Phase == RunPhase.InProgress; i++)
        {
            var next = new GridPosition(position.X + dx, position.Y + dy);
            if (!room.Layout.Contains(next) || !room.Layout[next].IsWalkable())
            {
                break;
            }

            Hero? hero = State.HeroesIn(room).FirstOrDefault(found => found.IsInPlay && found.Position == next);
            Monster? monster = room.Monsters.FirstOrDefault(found => found.Tiles.Contains(next));
            if ((heroThrower is not null && hero is not null) || (monsterThrower is not null && monster is not null))
            {
                // One of the thrower's own side: it fetches up against them.
                break;
            }

            if (Strike(hero, monster) is var (hit, defense) && !hit && defense >= 2 * total)
            {
                // Turned aside hard: it stops dead.
                break;
            }

            path.Add(next);
            position = next;
        }

        room.AddLoot(position, [boulder]);
        events.Add(new BoulderRolled(room.Id, at, path, position));

        // Its roll at whoever it meets: (hit, their defense), or null with nobody there.
        (bool Hit, int Defense)? Strike(Hero? hero, Monster? monster)
        {
            if (monster is not null && heroThrower is not null)
            {
                int defended = DefendMonster(room, monster, total, ranged: true, events);
                return (DamageMonster(heroThrower, room, monster, total, DamageSource.Weapon, events, ranged: true, defended: defended) > 0, defended);
            }

            if (hero is not null && monsterThrower is not null)
            {
                int mark = events.Count;
                int dealt = DamageHero(room, hero, total, MonsterBane(monsterThrower), monsterThrower.Id, events, ranged: true);
                int defense = events.Skip(mark).OfType<DefenseRolled>().LastOrDefault(rolled => rolled.DefenderId == hero.Id)?.Total ?? 0;
                return (dealt > 0, defense);
            }

            return null;
        }
    }

    /// <summary>
    /// A Boulder breaks: 1-3 Rocks, maybe coins, now and then a gem. On a critical roll it bursts, flinging them
    /// onto the tiles around, each Rock striking whoever of the thrower's foes is there: a die, the Rock, and an Earth die.
    /// </summary>
    private void BreakBoulder(Room room, Hero? heroThrower, Monster? monsterThrower, GridPosition at, bool critical, List<GameEvent> events)
    {
        events.Add(new BoulderBroke(room.Id, at, critical));
        List<CardInstance> pieces = [];
        if (Content.FindCard("rock") is { } rock)
        {
            int rocks = 1 + _random.Loot.NextInt(0, 3);
            pieces.AddRange(Enumerable.Range(0, rocks).Select(_ => new CardInstance(State.NextId(), rock, broughtInByHeroId: null)));
        }

        if (_random.Loot.NextInt(0, 100) < BoulderCoinChance && Content.FindCard("loose_coins") is { } coins)
        {
            pieces.Add(new CardInstance(State.NextId(), coins, broughtInByHeroId: null));
        }

        if (_random.Loot.NextInt(0, 100) < BoulderGemChance && Content.FindCard("emerald") is { } gem)
        {
            pieces.Add(new CardInstance(State.NextId(), gem, broughtInByHeroId: null));
        }

        List<GridPosition> around = [.. at.SurroundingTiles().Where(tile => room.Layout.Contains(tile) && room.Layout[tile].IsWalkable())];
        foreach (CardInstance piece in pieces)
        {
            GridPosition spot = critical && around.Count > 0 ? around[_random.Loot.NextInt(0, around.Count)] : at;
            if (critical && piece.Definition.Id == "rock" && State.Phase == RunPhase.InProgress)
            {
                Shrapnel(room, heroThrower, monsterThrower, spot, piece.Definition, events);
            }

            DropLoot(room, spot, piece, events);
        }
    }

    /// <summary>A Rock flung from a bursting Boulder strikes whoever of the thrower's foes is where it lands.</summary>
    private void Shrapnel(Room room, Hero? heroThrower, Monster? monsterThrower, GridPosition spot, CardDefinition rock, List<GameEvent> events)
    {
        Hero? hero = monsterThrower is null ? null : State.HeroesIn(room).FirstOrDefault(found => found.IsActive && found.Position == spot);
        Monster? monster = heroThrower is null ? null : room.Monsters.FirstOrDefault(found => found.Tiles.Contains(spot));
        if (hero is null && monster is null)
        {
            return;
        }

        (int[] faces, DieType[] types) = RollTypedDice([(DieType.Plain, 1), (DieType.Earth, 1)]);
        var roll = new DiceRoll(faces);
        int bonus = rock.AttackBonus + rock.Modifiers.Weapon;
        int total = roll.Total + bonus;
        int attackerId = heroThrower?.Id ?? monsterThrower!.Id;
        events.Add(new AttackRolled(attackerId, roll, bonus, total, [hero?.Id ?? monster!.Id], 0) { DieTypes = types });
        Dictionary<DieType, int> sixes = TypedSixes(roll, types);
        if (hero is not null && DamageHero(room, hero, total, MonsterBane(monsterThrower!), monsterThrower!.Id, events, ranged: true) > 0 && hero.IsInPlay)
        {
            LayTypedDice(room, hero, sixes, monsterThrower.Id, spot, events);
        }
        else if (monster is not null && DamageMonster(heroThrower, room, monster, total, DamageSource.Weapon, events, ranged: true) > 0 && room.FindMonster(monster.Id) is not null)
        {
            LayTypedDice(room, monster, sixes, heroThrower!.Id, spot, heroThrower, events);
        }
    }
}
