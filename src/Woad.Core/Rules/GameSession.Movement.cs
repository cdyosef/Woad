using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

public sealed partial class GameSession
{
    /// <summary>Tiles the active hero can walk to right now, with the steps each costs.</summary>
    public IReadOnlyDictionary<GridPosition, int> GetReachableTiles()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || CheckCanMove(Crawl) is not null)
        {
            return new Dictionary<GridPosition, int>();
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return HeroPathSearch(hero, room).Distances
            .Where(entry => entry.Value > 0 && CanHeroStop(room, hero, entry.Key))
            .ToDictionary();
    }

    /// <summary>Doors the active hero is standing in and could step (or fly) through right now.</summary>
    public IReadOnlyList<Direction> GetUsableDoors()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || CheckHeld(ActiveHero) is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        if (ActiveRoom.IsSealed)
        {
            return [];
        }

        return ActiveRoom.Doors
            .Where(door => AtDoor(ActiveRoom, door.Key, hero.Position)
                           && ActiveRoom.PassageOn(door.Key) is not { IsOpen: false }
                           && DoorLanding(State.Rooms[door.Value], door.Key) is not null)
            .Select(door => door.Key)
            .ToList();
    }

    /// <summary>
    /// Whether <paramref name="at"/> is at the door on <paramref name="side"/>: on a tile beside its doorway
    /// (where a hero stops, out of the way), or in the doorway itself.
    /// </summary>
    public static bool AtDoor(Room room, Direction side, GridPosition at) => at.ChebyshevDistanceTo(room.DoorTile(side)) <= 1;

    /// <summary>A doorway or the stairs: tiles to go through or down, not to stand in the way on.</summary>
    public static bool IsPassageTile(Room room, GridPosition tile) =>
        room.Layout[tile] is TileKind.StairsDown or TileKind.StairsUp || room.Doors.Keys.Any(side => room.DoorTile(side) == tile);

    /// <summary>
    /// Where a hero coming through the door on <paramref name="side"/> lands in <paramref name="target"/>:
    /// the nearest free tile just inside the facing doorway (never in a doorway or on the stairs).
    /// </summary>
    private GridPosition? DoorLanding(Room target, Direction side)
    {
        GridPosition doorway = target.DoorTile(side.Opposite());
        GridPosition inside = side switch
        {
            Direction.North => new GridPosition(doorway.X, doorway.Y - 1),
            Direction.East => new GridPosition(doorway.X + 1, doorway.Y),
            Direction.South => new GridPosition(doorway.X, doorway.Y + 1),
            _ => new GridPosition(doorway.X - 1, doorway.Y),
        };
        PathSearch search = PathSearch.Run(doorway, tile => target.Layout[tile].IsWalkable());
        return search.Distances
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key.ManhattanDistanceTo(inside))
            .ThenBy(entry => entry.Key.Y)
            .ThenBy(entry => entry.Key.X)
            .Select(entry => entry.Key)
            .Where(tile => target.Layout[tile].IsWalkable() && !IsOccupied(target, tile) && !IsPassageTile(target, tile))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    private string? Handle(RollMovement command, List<GameEvent> events)
    {
        if (ActiveHero.Shackled is not null)
        {
            return CheckHeld(ActiveHero);
        }

        int left = MoveDiceLeft;
        if (left < 1)
        {
            return "You've used all your Move dice this turn.";
        }

        int dice = command.Dice ?? left;
        if (dice < 1 || dice > left)
        {
            return $"Choose 1 to {left} Move dice.";
        }

        Hero hero = ActiveHero;
        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        int speed = SpeedBonusLeft;
        int bonus = command.UseSpeed || speed < 0 ? speed : 0;
        Turn.MoveDiceUsed += dice;
        Turn.SpeedUsed |= bonus != 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);

        // Steps left from an earlier move carry over into this one.
        DiceRoll roll = hero.FixedWendFace is int face ? new DiceRoll([.. Enumerable.Repeat(face, dice + command.PowerDice)]) : _dice.Roll(dice + command.PowerDice);
        Turn.CarriedSteps = Turn.MovementPoints;
        Turn.MovementRoll = roll;
        Turn.MovementBonus = bonus - RollPenalty(hero.Conditions) - Curse(hero, events);
        Turn.HasMoved = false;
        Turn.CountSteps();
        events.Add(new MovementRolled(command.HeroId, roll, bonus, Turn.MovementPoints, command.PowerDice));
        EarnPowerDice(hero, Sixes(roll.Faces), events);
        return null;
    }

    private string? Handle(MoveHero command, List<GameEvent> events)
    {
        if (CheckCanMove(Crawl) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        PathSearch search = HeroPathSearch(hero, room);
        if (command.Destination == hero.Position
            || !search.CanReach(command.Destination)
            || !CanHeroStop(room, hero, command.Destination))
        {
            return "You can't reach that tile.";
        }

        // A trap stops the walk where it goes off: a hidden one wherever it is, a found one only when
        // walked onto on purpose (paths never go past one). Walking into a pit is a fall.
        IReadOnlyList<GridPosition> path = search.PathTo(command.Destination);
        int sprung = path.ToList().FindIndex(tile => room.TrapAt(tile) is not null);

        // Ice (without Snowshoes) slides them a tile further, and that's the end of the move.
        GridPosition start = hero.Position;
        GridPosition? slide = null;
        if (hero.SnowshoesRounds == 0 && SlipOnIce(room, hero.Position, path, flying: false) is { } slip && (sprung < 0 || slip.Index < sprung))
        {
            sprung = -1;
            path = path.Take(slip.Index + 1).ToList();
            slide = slip.Slide;
        }

        if (sprung >= 0)
        {
            path = path.Take(sprung + 1).ToList();
        }

        Turn.MovementPoints -= path.Sum(tile => HeroStepCost(hero, room, tile)) * Crawl;
        if (slide is { } beyond)
        {
            path = [.. path, beyond];
        }

        hero.Position = path[^1];
        foreach (GridPosition tile in path.Where(tile => room.Layout[tile].IsWalkable()))
        {
            hero.Footing = tile;
        }

        Turn.HasMoved = true;
        events.Add(new HeroMoved(hero.Id, path, Turn.MovementPoints));
        if (path.Any(room.IsIcy) && hero.SnowshoesRounds == 0)
        {
            Turn.EndMove();
            if (slide is { } slidTo)
            {
                events.Add(new Slid(hero.Id, path.Count > 1 ? path[^2] : start, slidTo));
            }
        }
        DouseOnPath(room, hero, path, events);
        if (hero.FireDice == 0 && path.Any(tile => room.IsBurning(tile) && !InWater(room, tile)))
        {
            Ignite(hero, hero.Id, events);
        }

        if (sprung >= 0)
        {
            Turn.EndMove();
            SpringTrap(hero, room, room.TrapAt(hero.Position)!, hero.Position, events);
        }
        else if (room.Layout[hero.Position] == TileKind.Pit)
        {
            FallIntoPit(hero, room, hero.Position, events);
        }

        return null;
    }

    private string? Handle(GoThroughDoor command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.Doors.TryGetValue(command.Door, out string? targetId) || room.PassageOn(command.Door) is { IsHidden: true })
        {
            return "There's no door on that side.";
        }

        if (room.PassageOn(command.Door) is { IsBlocked: true })
        {
            return "A cracked wall blocks this doorway. A bomb could break it.";
        }

        if (room.PassageOn(command.Door) is { IsLocked: true } locked)
        {
            return locked.NeedsBossKey
                ? "The Boss Door opens only with the Boss Key."
                : $"The door is locked: use a key, or pick the lock with Od Hand ({locked.Difficulty}+).";
        }

        if (!AtDoor(room, command.Door, hero.Position))
        {
            return "Go up to the door first.";
        }

        if (room.IsSealed)
        {
            return "The doors are sealed until every monster here is defeated.";
        }

        if (CheckHeld(hero) is { } error)
        {
            return error;
        }

        Room target = State.Rooms[targetId];
        if (DoorLanding(target, command.Door) is not { } arrival)
        {
            return "There's no room on the other side of the door.";
        }

        // Going through is free: no steps, no flight.
        hero.RoomId = target.Id;
        hero.Position = arrival;
        bool firstVisit = !target.Explored;
        target.Explored = true;
        events.Add(new HeroEnteredRoom(hero.Id, room.Id, target.Id, arrival, firstVisit));
        if (room.PassageOn(command.Door) is { Trap: { } darts } trapped)
        {
            trapped.Trap = null;
            Turn.EndMove();
            SpringTrap(hero, target, darts, arrival, events);
        }

        return null;
    }

    /// <summary>The door whose doorway <paramref name="at"/> is (unless it's a secret nobody has found), or null.</summary>
    private static Direction? DoorwayAt(Room room, GridPosition at) =>
        room.Doors.Keys.Where(side => room.DoorTile(side) == at && room.PassageOn(side) is not { IsHidden: true })
            .Select(side => (Direction?)side).FirstOrDefault();

    /// <summary>Why the active hero can't push this ally through the doorway they stand in, or null.</summary>
    public string? CheckPushThrough(int allyId)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (State.ActiveHeroesIn(room).FirstOrDefault(ally => ally.Id == allyId && ally != hero) is not { } ally)
        {
            return "There's nobody here to push.";
        }

        if (DoorwayAt(room, ally.Position) is not { } door)
        {
            return "They aren't standing in a doorway.";
        }

        return hero.Position.ChebyshevDistanceTo(ally.Position) != 1 ? "Stand next to them to push them through."
            : room.PassageOn(door) is { IsOpen: false } ? "That door isn't open."
            : room.IsSealed ? "The doors are sealed until every monster here is defeated."
            : DoorLanding(State.Rooms[room.Doors[door]], door) is null ? "There's no room on the other side of the door."
            : null;
    }

    /// <summary>
    /// An ally in the doorway lets the hero push them through: free, no roll. (Resisting, they're shoved
    /// through only by a Shove that beats their defense.)
    /// </summary>
    private string? Handle(PushThroughDoor command, List<GameEvent> events)
    {
        if (CheckPushThrough(command.AllyId) is { } error)
        {
            return error;
        }

        PushThrough(ActiveRoom, State.GetHero(command.AllyId), events);
        return null;
    }

    /// <summary>Puts a hero standing in a doorway through it, just inside the next room; any darts there go off on them.</summary>
    private void PushThrough(Room room, Hero ally, List<GameEvent> events)
    {
        Direction door = DoorwayAt(room, ally.Position)!.Value;
        Room target = State.Rooms[room.Doors[door]];
        GridPosition arrival = DoorLanding(target, door)!.Value;
        ally.RoomId = target.Id;
        ally.Position = arrival;
        bool firstVisit = !target.Explored;
        target.Explored = true;
        events.Add(new HeroEnteredRoom(ally.Id, room.Id, target.Id, arrival, firstVisit));
        if (room.PassageOn(door) is { Trap: { } darts } trapped)
        {
            trapped.Trap = null;
            SpringTrap(ally, target, darts, arrival, events);
        }
    }

    private string? CheckCanMove(int cost)
    {
        if (ActiveHero.Aloft)
        {
            return "You're in the air: fly, or land first.";
        }

        if (CheckHeld(ActiveHero) is { } held)
        {
            return held;
        }

        if (Turn.MovementRoll is null)
        {
            return MoveDiceLeft > 0 ? "Roll to move first." : "You've used all your Move dice this turn.";
        }

        return Turn.MovementPoints < cost ? "Not enough movement left." : null;
    }

    /// <summary>
    /// Heroes walk through allies but not monsters, walls or chests, and swim deep water. Rough ground
    /// and water cost 2 steps a tile. They may step onto a trap they know of or into a pit, but that's
    /// where the walk ends: no path goes past one.
    /// </summary>
    private PathSearch HeroPathSearch(Hero hero, Room room) =>
        PathSearch.Run(
            hero.Position,
            position => (room.Layout[position].IsWalkable() || room.Layout[position] is TileKind.Pit or TileKind.DeepWater) && room.MonsterAt(position) is null,
            Turn.MovementPoints,
            canPass: position => room.Layout[position] != TileKind.Pit && room.TrapAt(position) is not { Revealed: true },
            stepCost: position => HeroStepCost(hero, room, position) * CrawlOf(hero));

    private bool CanHeroStop(Room room, Hero hero, GridPosition position) =>
        !State.HeroesIn(room).Any(other => other != hero && other.Position == position) && room.MerchantAt(position) is null;
}
