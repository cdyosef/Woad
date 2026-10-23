using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Bombs (see <see cref="Bomb"/>). A thrown bomb lands lit and goes off at the end of its thrower's next
/// turn; a bomb trap pops one out under whoever springs it. The blast rolls <see cref="GameRules.BlastDice"/>
/// dice (sixes roll more at once) and catches everyone within a tile, friend or foe: each creature
/// defends, and every die over its defense is a hit. 1 hit costs a heart, 2 also shove it away from the
/// bomb, 3 also knock it prone, and 4 or more also Powerful Stun it (a stack for each hit past 3). The
/// creature on the bomb's own tile is shoved in a random direction and takes a second roll. Blasts break
/// cracked walls and set off other bombs. Fire sets a bomb off; so may a blow (each Rumble die 12%, each
/// point of weapon bonus 2%), and a failed defusing (Od Hand 10). A shove only rolls it a tile away;
/// a bomb in water goes out.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The monster taking its turn, if any: a bomb it sets loose burns from the end of its next turn.</summary>
    private int? _actingMonsterId;

    /// <summary>Bombs going off right now, so a chain doesn't set one off twice.</summary>
    private readonly HashSet<int> _exploding = [];

    /// <summary>Why the active hero can't strike the bomb, or null.</summary>
    public string? CheckAttackBomb(int bombId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish the roll first.";
        }

        if (AttackDiceLeft < 1)
        {
            return "You've used all your Rumble dice this turn.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.FindBomb(bombId) is not { } bomb)
        {
            return "There's no bomb there.";
        }

        return CanHeroReach(hero, room.Layout, hero.Position, bomb.Position) ? null : "That bomb is out of reach.";
    }

    private string? Handle(AttackBomb command, List<GameEvent> events)
    {
        if (CheckAttackBomb(command.BombId) is { } error)
        {
            return error;
        }

        int left = AttackDiceLeft;
        int dice = command.Dice ?? left;
        if (dice < 1 || dice > left)
        {
            return $"Choose 1 to {left} Rumble dice.";
        }

        Hero hero = ActiveHero;
        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        Room room = ActiveRoom;
        Bomb bomb = room.FindBomb(command.BombId)!;
        Wielding wielding = Wield(hero, room, bomb.Position);
        int bonus = command.UseWeapon && wielding.HasStrike ? wielding.Bonus : 0;
        Turn.AttackDiceUsed += dice;
        UseWeapon(hero, room, wielding, bonus > 0, bomb.Position, events);
        SpendBankedPowerDice(hero, command.PowerDice, events);
        StrikeBomb(hero, room, bomb, dice + command.PowerDice, bonus, events);
        return null;
    }

    /// <summary>A blow to a bomb: a chance it goes off. True if it did.</summary>
    private bool StrikeBomb(Hero hero, Room room, Bomb bomb, int dice, int bonus, List<GameEvent> events)
    {
        int chance = Math.Min(100, GameRules.BombChancePerDie * dice + GameRules.BombChancePerBonus * bonus);
        bool exploded = _random.Dice.NextInt(0, 100) < chance;
        events.Add(new BombStruck(hero.Id, bomb.Id, chance, exploded));
        if (exploded)
        {
            Explode(room, bomb, events, setOffBy: hero);
        }

        return exploded;
    }

    /// <summary>A shove at a bomb: it never sets it off, just rolls it a tile away (down a pit, or out in water).</summary>
    private void ShoveBomb(Hero hero, Room room, Bomb bomb, List<GameEvent> events)
    {
        if (PushDestination(room, hero.Position, bomb.Position, flying: false) is not { } to)
        {
            return;
        }

        GridPosition from = bomb.Position;
        bomb.Position = to;
        events.Add(new BombMoved(room.Id, bomb.Id, from, to));
        if (room.Layout[to] == TileKind.Pit)
        {
            room.RemoveBomb(bomb);
            Room? below = room.BelowRoomId is { } belowId ? State.Rooms[belowId] : null;
            events.Add(new BombFell(room.Id, bomb.Id, below?.Id));
            if (below is not null && LandingNear(below, Beneath(room, to, below)) is { } landing)
            {
                bomb.Position = landing;
                below.AddBomb(bomb);
            }
        }
        else if (InWater(room, to) && bomb.Lit)
        {
            Defuse(room, bomb, doused: true, events);
        }
    }

    /// <summary>Puts a bomb down: lit, its fuse runs to the end of its owner's next turn; in water, it goes out at once.</summary>
    private void PlaceBomb(Room room, GridPosition tile, bool lit, int? ownerId, List<GameEvent> events)
    {
        bool ownTurn = ownerId is int owner && (owner == State.Turn?.HeroId || owner == _actingMonsterId);
        var bomb = new Bomb(State.NextId(), tile, lit, lit ? ownerId : null, lit ? (ownTurn ? 2 : 1) : 0);
        room.AddBomb(bomb);
        events.Add(new BombPlaced(room.Id, bomb.Id, tile, lit, bomb.OwnerId));
        if (lit && InWater(room, tile))
        {
            Defuse(room, bomb, doused: true, events);
        }
    }

    private static void Defuse(Room room, Bomb bomb, bool doused, List<GameEvent> events)
    {
        bomb.Lit = false;
        bomb.OwnerId = null;
        bomb.FuseTurns = 0;
        events.Add(new BombDefused(room.Id, bomb.Id, doused));
    }

    /// <summary>
    /// The end of a hero's or monster's turn: the fuses of its lit bombs burn down, and those that run
    /// out go off. A bomb whose owner is gone (out of the run, or dead) burns at the end of every hero's turn.
    /// </summary>
    private void BurnFuses(int ownerId, bool heroTurn, List<GameEvent> events)
    {
        foreach (Room room in State.Rooms.Values.ToList())
        {
            foreach (Bomb bomb in room.Bombs.Where(bomb => bomb.Lit).ToList())
            {
                bool burns = bomb.OwnerId == ownerId || (heroTurn && bomb.OwnerId is int owner && !OwnerStillHere(owner));
                if (!burns || !room.Bombs.Contains(bomb))
                {
                    continue;
                }

                bomb.FuseTurns--;
                if (bomb.FuseTurns > 0)
                {
                    events.Add(new FuseBurned(room.Id, bomb.Id, bomb.FuseTurns));
                    continue;
                }

                Explode(room, bomb, events);
                if (State.Phase != RunPhase.InProgress)
                {
                    return;
                }
            }
        }
    }

    private bool OwnerStillHere(int ownerId) =>
        State.Heroes.Any(hero => hero.Id == ownerId && hero.IsInPlay) || State.Rooms.Values.Any(room => room.FindMonster(ownerId) is not null);

    /// <summary>A bomb goes off (see the class remarks). <paramref name="setOffBy"/> gets the kills, else its owner if a hero.</summary>
    private void Explode(Room room, Bomb bomb, List<GameEvent> events, Hero? setOffBy = null)
    {
        if (!_exploding.Add(bomb.Id) || !room.Bombs.Contains(bomb))
        {
            return;
        }

        try
        {
            room.RemoveBomb(bomb);
            GridPosition center = bomb.Position;
            Hero? credit = setOffBy ?? (bomb.OwnerId is int owner ? State.Heroes.FirstOrDefault(hero => hero.Id == owner) : null);
            events.Add(new BombExploded(room.Id, bomb.Id, center));
            BreakWalls(room, CrackedWallsIn(room, center, 1), events);

            List<Monster> monsters = room.Monsters.Where(monster => monster.Position.ChebyshevDistanceTo(center) <= 1 && !Submerged(room, monster)).ToList();
            List<Hero> heroes = State.HeroesIn(room).Where(hero => hero.Position.ChebyshevDistanceTo(center) <= 1 && !Diving(room, hero)).ToList();
            DiceRoll roll = RollBlast(room, center, events);
            foreach (Monster monster in monsters)
            {
                bool epicenter = monster.Position == center;
                BlastMonster(room, monster, roll, center, epicenter, credit, events);
                if (epicenter && room.FindMonster(monster.Id) is not null)
                {
                    BlastMonster(room, monster, RollBlast(room, center, events), center, epicenter: true, credit, events);
                }
            }

            foreach (Hero hero in heroes)
            {
                bool epicenter = hero.Position == center;
                BlastHero(room, hero, roll, center, epicenter, credit, events);
                if (epicenter && hero.IsInPlay && hero.RoomId == room.Id)
                {
                    BlastHero(room, hero, RollBlast(room, center, events), center, epicenter: true, credit, events);
                }
            }

            foreach (Bomb other in room.Bombs.Where(other => other.Position.ChebyshevDistanceTo(center) <= 1).ToList())
            {
                Explode(room, other, events, credit);
            }
        }
        finally
        {
            _exploding.Remove(bomb.Id);
        }
    }

    /// <summary>A blast's dice: each 6 rolls another at once (and so on).</summary>
    private DiceRoll RollBlast(Room room, GridPosition center, List<GameEvent> events)
    {
        (DiceRoll roll, int powerDice) = RollMonsterDice(GameRules.BlastDice);
        events.Add(new BlastRolled(room.Id, center, roll, powerDice));
        return roll;
    }

    /// <summary>Every die of the blast over the creature's defense (its defense roll plus armor) is a hit.</summary>
    private static int Hits(DiceRoll blast, int defense) => blast.Faces.Count(face => face > defense);

    private void BlastMonster(Room room, Monster monster, DiceRoll blast, GridPosition center, bool epicenter, Hero? credit, List<GameEvent> events)
    {
        if (room.FindMonster(monster.Id) is null)
        {
            return;
        }

        bool collapsed = monster.Collapsed;
        int armor = collapsed ? 0 : monster.Definition.Armor;
        DiceRoll defense = RollDefense(monster.Id, monster.Power, collapsed ? 0 : DefenseDiceOf(monster), armor, blast.Total, canSpend: !collapsed, events);
        int hits = Hits(blast, defense.Total + armor);
        events.Add(new BlastHit(monster.Id, hits));
        monster.Hearts = Math.Max(0, monster.Hearts - (hits > 0 ? GameRules.DamagePerHit : 0));
        events.Add(new Damaged(monster.Id, hits > 0 ? GameRules.DamagePerHit : 0, monster.Hearts));
        if (hits > 0 && monster.Hearts == 0)
        {
            DefeatMonster(credit, room, monster, DamageSource.Bomb, events);
            return;
        }

        bool mighty = monster.Definition.Boss || monster.Definition.Miniboss;
        if (hits >= 2 && !mighty && BlastPush(room, monster.Position, center, epicenter, monster.Flies) is { } to)
        {
            PushMonster(credit, room, monster, to, events);
        }

        if (hits >= 3 && room.FindMonster(monster.Id) is not null && !monster.Prone && !mighty)
        {
            monster.Prone = true;
            events.Add(new WentProne(monster.Id, Diving: false));
        }

        if (hits >= 4)
        {
            Stun(room, monster, powerful: true, hits - 3, credit, events);
        }
    }

    private void BlastHero(Room room, Hero hero, DiceRoll blast, GridPosition center, bool epicenter, Hero? credit, List<GameEvent> events)
    {
        if (!hero.IsInPlay || hero.RoomId != room.Id)
        {
            return;
        }

        // To take no hits, the defense has to reach the blast's highest die.
        int defense = HeroDefense(hero, blast.Faces.DefaultIfEmpty(0).Max(), events);
        int hits = Hits(blast, defense);
        events.Add(new BlastHit(hero.Id, hits));
        string bane = credit is null ? "Blown up" : credit == hero ? "Blown up by their own bomb" : $"Blown up by {credit.Name}";
        if (hits == 0)
        {
            events.Add(new Damaged(hero.Id, 0, hero.Hearts));
            return;
        }

        if (hero.IsKeelingOver)
        {
            events.Add(new Damaged(hero.Id, GameRules.DamagePerHit, 0));
            MarkForDeath(room, hero, bane, credit?.Id ?? 0, events);
        }
        else
        {
            hero.Hearts = Math.Max(0, hero.Hearts - GameRules.DamagePerHit);
            events.Add(new Damaged(hero.Id, GameRules.DamagePerHit, hero.Hearts));
            if (hero.Hearts == 0)
            {
                KeelOver(room, hero, bane, events);
            }
        }

        if (hits >= 2 && hero.IsInPlay && BlastPush(room, hero.Position, center, epicenter, flying: false) is { } to)
        {
            PushHero(credit, room, hero, to, events);
        }

        if (hits >= 3 && hero.IsActive && !hero.Prone)
        {
            hero.Prone = true;
            events.Add(new WentProne(hero.Id, Diving(room, hero)));
        }

        if (hits >= 4)
        {
            Stun(hero, powerful: true, hits - 3, events);
        }
    }

    /// <summary>Where a blast throws someone: a tile straight away from the bomb, or from its own tile, any open way at random.</summary>
    private GridPosition? BlastPush(Room room, GridPosition at, GridPosition center, bool epicenter, bool flying)
    {
        if (!epicenter)
        {
            return PushDestination(room, center, at, flying);
        }

        List<GridPosition> ways = at.SurroundingTiles()
            .Select(tile => PushDestination(room, new GridPosition(2 * at.X - tile.X, 2 * at.Y - tile.Y), at, flying))
            .OfType<GridPosition>()
            .Distinct()
            .ToList();
        return ways.Count == 0 ? null : _random.Behavior.Pick(ways);
    }
}
