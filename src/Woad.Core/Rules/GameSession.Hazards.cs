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
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (AttackDiceLeft < 1)
        {
            return "You've used all your Rumble dice this turn.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.FindBomb(targetId) is { } bomb)
        {
            return hero.Position.ChebyshevDistanceTo(bomb.Position) != 1 ? "Stand next to the bomb to shove it."
                : PushDestination(room, hero.Position, bomb.Position, flying: false) is null ? "There's nowhere to shove it: something is in the way."
                : null;
        }

        Monster? monster = room.FindMonster(targetId);
        Hero? ally = State.FriendlyFire ? State.ActiveHeroesIn(room).FirstOrDefault(other => other.Id == targetId && other != hero) : null;
        if (monster is null && ally is null)
        {
            return "There's nobody here to shove.";
        }

        GridPosition at = monster?.NearestTile(hero.Position) ?? ally!.Position;
        if (!Targeting.CanHeroAttack(room.Layout, 1, hero.Position, at) || hero.Position.ChebyshevDistanceTo(at) != 1)
        {
            return "Stand next to them to shove them.";
        }

        if (monster is { Definition.Boss: true } or { Definition.Miniboss: true } or { Definition.Size: > 1 })
        {
            return $"The {monster.Definition.Name} is too mighty to shove.";
        }

        return ShoveDestination(room, hero, at, monster?.Flies ?? false) is null ? "There's nowhere to shove them: something is in the way." : null;
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
            .Concat(room.Bombs.Where(bomb => CheckShove(bomb.Id) is null).Select(bomb => bomb.Position))
            .Concat(State.ActiveHeroesIn(room).Where(ally => CheckShove(ally.Id) is null).Select(ally => ally.Position))
            .ToList();
    }

    /// <summary>Tiles the active hero could aim a leap at right now (see <see cref="Leap"/>).</summary>
    public IReadOnlyList<GridPosition> GetLeapTargets()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || MoveDiceLeft < 1 || ActiveHero.Prone || HeldBy(ActiveHero) is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        var targets = new List<GridPosition>();
        foreach ((int dx, int dy) in LeapDirections)
        {
            for (int distance = 1; distance <= GameRules.MaxLeap; distance++)
            {
                var tile = new GridPosition(hero.Position.X + dx * distance, hero.Position.Y + dy * distance);
                if (!room.Layout.Contains(tile) || BlocksLeap(room.Layout[tile]))
                {
                    break;
                }

                if (CanLandOn(room, tile))
                {
                    targets.Add(tile);
                }
            }
        }

        return targets;
    }

    private static readonly (int Dx, int Dy)[] LeapDirections = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>A leap sails over pits, water, traps and figures, but not over walls, chests or pots.</summary>
    private static bool BlocksLeap(TileKind tile) => tile is TileKind.Wall or TileKind.CrackedWall or TileKind.Chest or TileKind.Pot;

    /// <summary>Where a leaping hero can come down: standing ground or water, with nobody on it.</summary>
    private bool CanLandOn(Room room, GridPosition tile) =>
        (room.Layout[tile].IsWalkable() || room.Layout[tile] == TileKind.DeepWater) && !IsOccupied(room, tile);

    private string? Handle(Leap command, List<GameEvent> events)
    {
        if (ActiveHero.Shackled is not null)
        {
            return CheckHeld(ActiveHero);
        }

        if (MoveDiceLeft < 1)
        {
            return "Leaping takes a Wend die, and you have none left this turn.";
        }

        if (ActiveHero.Prone)
        {
            return "You can't leap lying down: get up first.";
        }

        if (CheckHeld(ActiveHero) is { } held)
        {
            return held;
        }

        if (!GetLeapTargets().Contains(command.Target))
        {
            return $"Leap in a straight line (diagonals too), up to {GameRules.MaxLeap} tiles, onto open ground or water.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        GridPosition from = hero.Position;
        int aimed = from.ChebyshevDistanceTo(command.Target);
        int dx = Math.Sign(command.Target.X - from.X);
        int dy = Math.Sign(command.Target.Y - from.Y);

        // Half the roll, rounded down, is how far the leap carries; it never overshoots the target.
        // Coming down on someone, it falls back to the last free tile before them.
        int roll = RollOneDie();
        int distance = Math.Min(aimed, roll / 2);
        GridPosition landing = from;
        for (int d = distance; d > 0; d--)
        {
            var tile = new GridPosition(from.X + dx * d, from.Y + dy * d);
            if (!IsOccupied(room, tile) || tile == from)
            {
                landing = tile;
                break;
            }
        }

        Turn.MoveDiceUsed++;
        events.Add(new Leaped(hero.Id, from, command.Target, roll, landing));
        hero.Position = landing;
        if (room.Layout[landing] == TileKind.Pit)
        {
            FallIntoPit(hero, room, landing, events);
        }
        else if (landing != from && room.TrapAt(landing) is { } trap)
        {
            Turn.EndMove();
            SpringTrap(hero, room, trap, landing, events);
        }

        return null;
    }

    /// <summary>
    /// At the end of a hero's turn in deep water, one of their unused Rumble dice keeps them afloat.
    /// With none left they drown: they wash back to their last dry footing and lose a heart.
    /// </summary>
    private void KeepAfloat(Hero hero, List<GameEvent> events)
    {
        Room room = State.RoomOf(hero);
        if (room.Layout[hero.Position] != TileKind.DeepWater)
        {
            return;
        }

        if (hero.Swims)
        {
            return;
        }

        if (AttackDiceLeft > 0)
        {
            Turn.AttackDiceUsed++;
            events.Add(new StayedAfloat(hero.Id));
            return;
        }

        GridPosition from = hero.Position;
        hero.Position = IsOccupied(room, hero.Footing) ? LandingNear(room, hero.Footing) ?? hero.Footing : hero.Footing;
        events.Add(new Drowned(hero.Id, from, hero.Position));
        HurtHero(room, hero, GameRules.DrownDamage, "Drowned", events);
    }

    private string? Handle(Shove command, List<GameEvent> events)
    {
        if (CheckShove(command.TargetId) is { } error)
        {
            return error;
        }

        // A bomb just rolls a tile away, for a single Rumble die.
        if (ActiveRoom.FindBomb(command.TargetId) is { } bomb)
        {
            Turn.AttackDiceUsed++;
            ShoveBomb(ActiveHero, ActiveRoom, bomb, events);
            return null;
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

        // A shove is right beside the target: whatever's in hand puts its weight behind it, but nothing's thrown.
        GridPosition at = ActiveRoom.FindMonster(command.TargetId)?.Position ?? State.Heroes.FirstOrDefault(other => other.Id == command.TargetId)?.Position ?? hero.Position;
        Wielding wielding = Wield(hero, ActiveRoom, at) is { Thrown: false } held ? held : Wielding.BareHanded;
        int bonus = command.UseWeapon && wielding.HasStrike ? wielding.Bonus : 0;
        Turn.AttackDiceUsed += dice;
        UseWeapon(hero, ActiveRoom, wielding, bonus > 0, at, events);
        SpendBankedPowerDice(hero, command.PowerDice, events);
        RollHeroAttack(hero, dice, bonus, [command.TargetId], DamageSource.Shove, events, command.PowerDice);
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
        HurtHero(room, hero, GameRules.FallDamage, "Fell into a pit", events);
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

    /// <summary>The tile of the room below that lies under <paramref name="hole"/>: as far across it, since the rooms may differ in size.</summary>
    private static GridPosition Beneath(Room from, GridPosition hole, Room below)
    {
        static int Scale(int at, int fromSize, int toSize) => fromSize <= 1 ? 0 : (int)Math.Round(at * (toSize - 1) / (double)(fromSize - 1));
        return new GridPosition(Scale(hole.X, from.Layout.Width, below.Layout.Width), Scale(hole.Y, from.Layout.Height, below.Layout.Height));
    }

    /// <summary>The free tile nearest <paramref name="near"/> (it first) to land or climb out on: walkable, empty and without a trap.</summary>
    private GridPosition? LandingNear(Room room, GridPosition near) =>
        room.Positions
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
    private GridPosition? ShoveDestination(Room room, Hero hero, GridPosition at, bool flying) =>
        PushDestination(room, hero.Position, at, flying);

    /// <summary>Where a push away from <paramref name="away"/> takes whoever stands at <paramref name="at"/> (see <see cref="ShoveDestination"/>).</summary>
    private GridPosition? PushDestination(Room room, GridPosition away, GridPosition at, bool flying)
    {
        var to = new GridPosition(at.X + Math.Sign(at.X - away.X), at.Y + Math.Sign(at.Y - away.Y));
        if (to == at || !room.Layout.Contains(to) || IsOccupied(room, to))
        {
            return null;
        }

        TileKind tile = room.Layout[to];
        return tile.IsWalkable(flying) || tile is TileKind.Pit or TileKind.DeepWater ? to : null;
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
        int armor = collapsed ? 0 : ArmorOf(monster);
        int push = shove.Total + monster.Stun.AttackBonus;
        DiceRoll defense = RollDefense(monster.Id, monster.Power, collapsed ? 0 : DefenseDiceOf(monster), armor, push, canSpend: !collapsed, events);
        bool beaten = push > defense.Total + armor;

        // A shelled monster the shove beats goes over onto its back, pushed or not.
        if (beaten && monster.Definition.Shell && !monster.Prone && !collapsed)
        {
            monster.Prone = true;
            events.Add(new Flipped(monster.Id));
        }

        if (!beaten || ShoveDestination(room, hero, monster.Position, monster.Flies) is not { } to)
        {
            if (!beaten || !monster.Definition.Shell)
            {
                events.Add(new ShoveResisted(hero.Id, monster.Id));
            }

            return;
        }

        PushMonster(hero, room, monster, to, events);
    }

    /// <summary>Moves a monster a tile (a shove or a blast); then deep water, a pit or a trap there takes it.</summary>
    private void PushMonster(Hero? pusher, Room room, Monster monster, GridPosition to, List<GameEvent> events)
    {
        Hero? hero = pusher;
        GridPosition from = monster.Position;
        monster.Position = to;
        events.Add(new Shoved(pusher?.Id ?? 0, monster.Id, from, to));
        if (room.Layout[to] == TileKind.DeepWater && !monster.Flies && !monster.Definition.Swimmer)
        {
            if (monster.FireDice > 0)
            {
                monster.FireDice = 0;
                events.Add(new FireOut(monster.Id, FireOutCause.Water));
            }

            // Monsters don't swim: it loses a heart and scrambles out onto the nearest footing.
            HurtMonster(hero, room, monster, events);
            if (room.FindMonster(monster.Id) is not null && LandingNear(room, to) is { } shore)
            {
                monster.Position = shore;
                events.Add(new ClimbedOut(monster.Id, shore));
            }
        }
        else if (monster.Definition.WaterOnly && !InWater(room, to))
        {
            // A fish out of water: a heart lost flopping back in.
            HurtMonster(hero, room, monster, events);
            if (room.FindMonster(monster.Id) is not null)
            {
                monster.Position = from;
                events.Add(new ClimbedOut(monster.Id, from));
            }
        }
        else if (room.Layout[to] == TileKind.Pit && !monster.Flies)
        {
            MonsterFalls(hero, room, monster, from, to, events);
        }
        else if (room.TrapAt(to) is { Kind: TrapKind.Spikes or TrapKind.TrapDoor or TrapKind.BombTrap } trap && !monster.Flies)
        {
            trap.Revealed = true;
            events.Add(new MonsterTrapped(monster.Id, trap.Kind, to));
            if (trap.Kind == TrapKind.BombTrap)
            {
                room.SetTrap(to, null);
                PlaceBomb(room, to, lit: true, monster.Id, events);
            }
            else if (trap.Kind == TrapKind.TrapDoor)
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
        total += ally.Stun.AttackBonus;
        int defense = HeroDefense(ally, total, events);
        if (total <= defense || ShoveDestination(room, hero, ally.Position, flying: false) is not { } to)
        {
            events.Add(new ShoveResisted(hero.Id, ally.Id));
            return;
        }

        PushHero(hero, room, ally, to, events);
    }

    /// <summary>Moves a hero (a shove, a blast or a pull); then a pit or a trap there takes them.</summary>
    /// <param name="announce">Report it as a shove (a pull reports itself).</param>
    private void PushHero(Hero? pusher, Room room, Hero ally, GridPosition to, List<GameEvent> events, bool announce = true)
    {
        GridPosition from = ally.Position;
        ally.Position = to;
        if (announce)
        {
            events.Add(new Shoved(pusher?.Id ?? 0, ally.Id, from, to));
        }

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
    private void MonsterFalls(Hero? pusher, Room room, Monster monster, GridPosition edge, GridPosition hole, List<GameEvent> events)
    {
        Room? below = room.BelowRoomId is { } belowId ? State.Rooms[belowId] : null;
        bool trapDoor = room.Layout[hole] != TileKind.Pit;
        if (!trapDoor)
        {
            events.Add(new FellIntoPit(monster.Id, room.Id, hole, Bottomless: below is null));
        }

        if (below is null || LandingNear(below, Beneath(room, hole, below)) is not { } landing)
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
    private void HurtMonster(Hero? pusher, Room room, Monster monster, List<GameEvent> events)
    {
        monster.Hearts = Math.Max(0, monster.Hearts - GameRules.FallDamage);
        events.Add(new Damaged(monster.Id, GameRules.FallDamage, monster.Hearts));
        if (monster.Hearts == 0)
        {
            DefeatMonster(pusher, room, monster, DamageSource.Fall, events);
        }
    }
}
