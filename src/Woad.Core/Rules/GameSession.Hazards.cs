using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Pits and found traps as hazards to risk or use: heroes may walk onto them (and take the hit), jump
/// pits, and shove monsters (with friendly fire on, each other too) onto them. A pit drops whoever
/// falls in to the room underneath; with no room below it's bottomless: a hero climbs back out, a
/// monster is lost.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Why the active hero can't shove this monster now (or, with friendly fire on, this hero or
    /// companion), or null if they can.
    /// </summary>
    public string? CheckShove(int targetId)
    {
        if (State.Turn is not { PendingRoll: null })
        {
            return "Finish the roll first.";
        }

        if (AttackDiceLeft < 1)
        {
            return "You've used all your Rumble dice this turn.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        Monster? monster = room.FindMonster(targetId);
        Hero? ally = State.FriendlyFire ? State.ActiveHeroesIn(room).FirstOrDefault(other => other.Id == targetId && other != hero) : null;
        if (monster is null && ally is null)
        {
            return "There's nobody here to shove.";
        }

        GridPosition at = monster?.Position ?? ally!.Position;
        if (!Targeting.CanHeroAttack(room.Layout, 1, hero.Position, at) || hero.Position.ChebyshevDistanceTo(at) != 1)
        {
            return "Stand next to them to shove them.";
        }

        if (monster is { Definition.Boss: true })
        {
            return $"The {monster.Definition.Name} is too mighty to shove.";
        }

        return ShoveDestination(room, hero, at, monster?.Definition.Flying ?? false) is null ? "There's nowhere to shove them: something is in the way." : null;
    }

    /// <summary>Where the monsters (and, with friendly fire on, heroes) the active hero could shove right now stand.</summary>
    public IReadOnlyList<GridPosition> GetShoveTargets()
    {
        if (State.Turn is null)
        {
            return [];
        }

        Room room = ActiveRoom;
        return room.Monsters.Where(monster => CheckShove(monster.Id) is null).Select(monster => monster.Position)
            .Concat(State.ActiveHeroesIn(room).Where(ally => CheckShove(ally.Id) is null).Select(ally => ally.Position))
            .ToList();
    }

    /// <summary>Tiles the active hero could jump to across a pit right now.</summary>
    public IReadOnlyList<GridPosition> GetJumpLandings() =>
        State.Turn is null ? [] : Enum.GetValues<Direction>()
            .Select(side => ActiveHero.Position.Step(side).Step(side))
            .Where(landing => CheckJump(landing) is null)
            .ToList();

    /// <summary>Why the active hero can't jump to <paramref name="landing"/> now, or null if they can.</summary>
    public string? CheckJump(GridPosition landing)
    {
        if (State.Turn is not { PendingRoll: null })
        {
            return "Finish the roll first.";
        }

        if (CheckCanMove(2) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        int dx = landing.X - hero.Position.X;
        int dy = landing.Y - hero.Position.Y;
        bool straight = (Math.Abs(dx) == 2 && dy == 0) || (Math.Abs(dy) == 2 && dx == 0);
        var pit = new GridPosition(hero.Position.X + dx / 2, hero.Position.Y + dy / 2);
        if (!straight || !landing.IsInRoom || room.Layout[pit] != TileKind.Pit)
        {
            return "Jump straight over a pit to the tile beyond it.";
        }

        if (!room.Layout[landing].IsWalkable() || IsOccupied(room, landing) || room.TrapAt(landing) is { Revealed: true })
        {
            return "There's nowhere to land on the far side.";
        }

        return null;
    }

    private string? Handle(Shove command, List<GameEvent> events)
    {
        if (CheckShove(command.TargetId) is { } error)
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

        int bonus = command.UseWeapon ? WeaponBonusLeft : 0;
        Turn.AttackDiceUsed += dice;
        Turn.WeaponUsed |= bonus > 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);
        RollHeroAttack(hero, dice, bonus, [command.TargetId], DamageSource.Shove, events, command.PowerDice);
        return null;
    }

    private string? Handle(JumpPit command, List<GameEvent> events)
    {
        if (CheckJump(command.Landing) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        GridPosition from = hero.Position;
        var pit = new GridPosition((from.X + command.Landing.X) / 2, (from.Y + command.Landing.Y) / 2);
        int roll = RollOneDie();
        bool cleared = roll >= GameRules.JumpDifficulty;
        Turn.MovementPoints -= 2;
        Turn.HasMoved = true;
        events.Add(new PitJumped(hero.Id, from, command.Landing, roll, GameRules.JumpDifficulty, cleared));
        if (!cleared)
        {
            hero.Position = pit;
            FallIntoPit(hero, room, pit, events);
            return null;
        }

        hero.Position = command.Landing;
        if (room.TrapAt(command.Landing) is { } hidden)
        {
            Turn.EndMove();
            SpringTrap(hero, room, hidden, command.Landing, events);
        }

        return null;
    }

    /// <summary>
    /// A hero in a pit loses a heart and falls to the room below, or, with nothing below, climbs out
    /// onto the nearest free tile. Either way, if it's their turn, their movement ends.
    /// </summary>
    private void FallIntoPit(Hero hero, Room room, GridPosition pit, List<GameEvent> events)
    {
        if (hero.Id == Turn.HeroId)
        {
            Turn.EndMove();
        }

        events.Add(new FellIntoPit(hero.Id, room.Id, pit, Bottomless: room.BelowRoomId is null));
        HurtHero(room, hero, GameRules.FallDamage, events);
        if (!hero.IsActive)
        {
            hero.Position = SafeTile(room, pit);
            return;
        }

        if (room.BelowRoomId is not null)
        {
            Fall(hero, room, pit, jumped: false, events);
            return;
        }

        hero.Position = LandingNear(room, pit) ?? hero.Position;
        events.Add(new ClimbedOut(hero.Id, hero.Position));
    }

    /// <summary>The free tile nearest <paramref name="near"/> (it first) to land or climb out on: walkable, empty and without a trap.</summary>
    private GridPosition? LandingNear(Room room, GridPosition near) =>
        RoomGrid.AllPositions()
            .Where(tile => room.Layout[tile].IsWalkable() && !IsOccupied(room, tile) && room.TrapAt(tile) is null)
            .OrderBy(tile => tile.ManhattanDistanceTo(near))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();

    /// <summary>
    /// Where a shove pushes whoever stands at <paramref name="at"/>: one tile straight away from the
    /// hero (diagonally too). It has to be open floor (traps included) or a pit, with nobody on it.
    /// Null when there's no room.
    /// </summary>
    private GridPosition? ShoveDestination(Room room, Hero hero, GridPosition at, bool flying)
    {
        var to = new GridPosition(at.X + Math.Sign(at.X - hero.Position.X), at.Y + Math.Sign(at.Y - hero.Position.Y));
        if (!to.IsInRoom || IsOccupied(room, to))
        {
            return null;
        }

        TileKind tile = room.Layout[to];
        return tile.IsWalkable(flying) || tile == TileKind.Pit ? to : null;
    }

    /// <summary>A shove roll that beats the target's defense pushes it; then any trap or pit there takes it.</summary>
    private void ResolveShove(PendingRoll shove, List<GameEvent> events)
    {
        Hero hero = State.GetHero(shove.HeroId);
        Room room = State.RoomOf(hero);
        if (State.ActiveHeroesIn(room).FirstOrDefault(ally => ally.Id == shove.TargetIds[0]) is { } shovedAlly)
        {
            ShoveAlly(hero, room, shovedAlly, shove.Total, events);
            return;
        }

        if (room.FindMonster(shove.TargetIds[0]) is not { } monster)
        {
            return;
        }

        bool collapsed = monster.Collapsed;
        int armor = collapsed ? 0 : monster.Definition.Armor;
        DiceRoll defense = RollDefense(monster.Id, monster.Power, collapsed ? 0 : monster.Definition.DefenseDice, armor, shove.Total, canSpend: !collapsed, events);
        if (shove.Total <= defense.Total + armor || ShoveDestination(room, hero, monster.Position, monster.Definition.Flying) is not { } to)
        {
            events.Add(new ShoveResisted(hero.Id, monster.Id));
            return;
        }

        GridPosition from = monster.Position;
        monster.Position = to;
        events.Add(new Shoved(hero.Id, monster.Id, from, to));
        if (room.Layout[to] == TileKind.Pit && !monster.Definition.Flying)
        {
            MonsterFalls(hero, room, monster, from, to, events);
        }
        else if (room.TrapAt(to) is { Kind: TrapKind.Spikes or TrapKind.TrapDoor } trap && !monster.Definition.Flying)
        {
            trap.Revealed = true;
            events.Add(new MonsterTrapped(monster.Id, trap.Kind, to));
            if (trap.Kind == TrapKind.TrapDoor)
            {
                MonsterFalls(hero, room, monster, from, to, events);
            }
            else
            {
                HurtMonster(hero, room, monster, events);
            }
        }
    }

    /// <summary>
    /// With friendly fire on, a shove at another hero: they defend like against an attack, and if
    /// pushed onto a trap it goes off under them, or into a pit they fall.
    /// </summary>
    private void ShoveAlly(Hero hero, Room room, Hero ally, int total, List<GameEvent> events)
    {
        Stats stats = ally.EffectiveStats;
        int armor = Math.Max(0, stats.Armor);
        DiceRoll defense = RollDefense(ally.Id, ally.Power, Math.Max(0, stats.DefenseDice), armor, total, canSpend: true, events);
        if (total <= defense.Total + armor || ShoveDestination(room, hero, ally.Position, flying: false) is not { } to)
        {
            events.Add(new ShoveResisted(hero.Id, ally.Id));
            return;
        }

        GridPosition from = ally.Position;
        ally.Position = to;
        events.Add(new Shoved(hero.Id, ally.Id, from, to));
        if (room.Layout[to] == TileKind.Pit)
        {
            FallIntoPit(ally, room, to, events);
        }
        else if (room.TrapAt(to) is { } trap)
        {
            SpringTrap(ally, room, trap, to, events);
        }
    }

    /// <summary>
    /// A monster drops through the hole at <paramref name="hole"/>: into the room below, losing a
    /// heart, or to its doom when nothing is below. What it leaves behind lands where it stood before
    /// the shove (<paramref name="edge"/>).
    /// </summary>
    private void MonsterFalls(Hero pusher, Room room, Monster monster, GridPosition edge, GridPosition hole, List<GameEvent> events)
    {
        Room? below = room.BelowRoomId is { } belowId ? State.Rooms[belowId] : null;
        bool trapDoor = room.Layout[hole] != TileKind.Pit;
        if (!trapDoor)
        {
            events.Add(new FellIntoPit(monster.Id, room.Id, hole, Bottomless: below is null));
        }

        if (below is null || LandingNear(below, hole) is not { } landing)
        {
            monster.Position = edge;
            monster.Hearts = 0;
            DefeatMonster(pusher, room, monster, DamageSource.Fall, events);
            return;
        }

        room.RemoveMonster(monster);
        monster.Position = landing;
        below.AddMonster(monster);
        events.Add(new MonsterFell(monster.Id, room.Id, below.Id, landing));
        ClearRoomIfEmpty(room, events);
        HurtMonster(pusher, below, monster, events);
    }

    /// <summary>A fall or a trap costs a monster a heart; bones broken this way stay down.</summary>
    private void HurtMonster(Hero pusher, Room room, Monster monster, List<GameEvent> events)
    {
        monster.Hearts = Math.Max(0, monster.Hearts - GameRules.FallDamage);
        events.Add(new Damaged(monster.Id, GameRules.FallDamage, monster.Hearts));
        if (monster.Hearts == 0)
        {
            DefeatMonster(pusher, room, monster, DamageSource.Fall, events);
        }
    }
}
