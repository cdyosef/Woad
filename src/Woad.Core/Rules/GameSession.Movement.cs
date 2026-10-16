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
        if (State.Turn is not { PendingRoll: null } || CheckCanMove(Crawl) is not null)
        {
            return new Dictionary<GridPosition, int>();
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return HeroPathSearch(hero, room).Distances
            .Where(entry => entry.Value > 0 && CanHeroStop(room, hero, entry.Key))
            .ToDictionary();
    }

    /// <summary>Doors the active hero is standing in and could step through right now.</summary>
    public IReadOnlyList<Direction> GetUsableDoors()
    {
        if (State.Turn is not { PendingRoll: null } || CheckCanMove(Crawl) is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        if (ActiveRoom.IsSealed)
        {
            return [];
        }

        return ActiveRoom.Doors
            .Where(door => hero.Position == ActiveRoom.DoorTile(door.Key)
                           && ActiveRoom.PassageOn(door.Key) is not { IsOpen: false }
                           && DoorLanding(State.Rooms[door.Value], door.Key) is not null)
            .Select(door => door.Key)
            .ToList();
    }

    /// <summary>
    /// Where a hero coming through the door on <paramref name="side"/> lands in <paramref name="target"/>:
    /// the facing doorway, or the nearest free tile to it when someone is standing there.
    /// </summary>
    private GridPosition? DoorLanding(Room target, Direction side)
    {
        GridPosition doorway = target.DoorTile(side.Opposite());
        PathSearch search = PathSearch.Run(doorway, tile => target.Layout[tile].IsWalkable());
        return search.Distances
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key.Y)
            .ThenBy(entry => entry.Key.X)
            .Select(entry => entry.Key)
            .Where(tile => target.Layout[tile].IsWalkable() && !IsOccupied(target, tile))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    private string? Handle(RollMovement command, List<GameEvent> events)
    {
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
        DiceRoll roll = _dice.Roll(dice + command.PowerDice);
        Turn.CarriedSteps = Turn.MovementPoints;
        Turn.MovementRoll = roll;
        Turn.MovementBonus = bonus;
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
        if (sprung >= 0)
        {
            path = path.Take(sprung + 1).ToList();
        }

        hero.Position = path[^1];
        Turn.MovementPoints -= path.Sum(tile => room.Layout[tile].MoveCost()) * Crawl;
        foreach (GridPosition tile in path.Where(tile => room.Layout[tile].IsWalkable()))
        {
            hero.Footing = tile;
        }

        Turn.HasMoved = true;
        events.Add(new HeroMoved(hero.Id, path, Turn.MovementPoints));
        DouseOnPath(room, hero, path, events);
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

        if (hero.Position != room.DoorTile(command.Door))
        {
            return "Stand in the doorway first.";
        }

        if (room.IsSealed)
        {
            return "The doors are sealed until every monster here is defeated.";
        }

        if (CheckCanMove(Crawl) is { } error)
        {
            return error;
        }

        Room target = State.Rooms[targetId];
        if (DoorLanding(target, command.Door) is not { } arrival)
        {
            return "There's no room on the other side of the door.";
        }

        hero.RoomId = target.Id;
        hero.Position = arrival;
        Turn.MovementPoints -= Crawl;
        Turn.HasMoved = true;
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

    private string? CheckCanMove(int cost)
    {
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
            stepCost: position => room.Layout[position].MoveCost() * (hero.Prone ? GameRules.CrawlFactor : 1));

    private bool CanHeroStop(Room room, Hero hero, GridPosition position) =>
        !State.HeroesIn(room).Any(other => other != hero && other.Position == position);
}
