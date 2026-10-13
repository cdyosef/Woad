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
        if (State.Turn is not { PendingAttack: null } || CheckCanMove(1) is not null)
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
        if (State.Turn is not { PendingAttack: null } || CheckCanMove(1) is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        if (ActiveRoom.IsSealed)
        {
            return [];
        }

        return ActiveRoom.Doors
            .Where(door => hero.Position == door.Key.DoorTile() && DoorLanding(State.Rooms[door.Value], door.Key) is not null)
            .Select(door => door.Key)
            .ToList();
    }

    /// <summary>
    /// Where a hero coming through the door on <paramref name="side"/> lands in <paramref name="target"/>:
    /// the facing doorway, or the nearest free tile to it when someone is standing there.
    /// </summary>
    private GridPosition? DoorLanding(Room target, Direction side)
    {
        GridPosition doorway = side.Opposite().DoorTile();
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
        Turn.EndMove();
        Turn.MoveDiceUsed += dice;
        Turn.SpeedUsed |= bonus != 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);

        DiceRoll roll = _dice.Roll(dice + command.PowerDice);
        Turn.MovementRoll = roll;
        Turn.MovementBonus = bonus;
        Turn.MovementPoints = Math.Max(0, roll.Total + bonus);
        events.Add(new MovementRolled(command.HeroId, roll, bonus, Turn.MovementPoints, command.PowerDice));
        EarnPowerDice(hero, Sixes(roll.Faces), events);
        return null;
    }

    private string? Handle(MoveHero command, List<GameEvent> events)
    {
        if (CheckCanMove(1) is { } error)
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

        IReadOnlyList<GridPosition> path = search.PathTo(command.Destination);
        hero.Position = command.Destination;
        Turn.MovementPoints -= path.Count;
        Turn.HasMoved = true;
        events.Add(new HeroMoved(hero.Id, path, Turn.MovementPoints));
        return null;
    }

    private string? Handle(GoThroughDoor command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.Doors.TryGetValue(command.Door, out string? targetId))
        {
            return "There's no door on that side.";
        }

        if (hero.Position != command.Door.DoorTile())
        {
            return "Stand in the doorway first.";
        }

        if (room.IsSealed)
        {
            return "The doors are sealed until every monster here is defeated.";
        }

        if (CheckCanMove(1) is { } error)
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
        Turn.MovementPoints -= 1;
        Turn.HasMoved = true;
        bool firstVisit = !target.Explored;
        target.Explored = true;
        events.Add(new HeroEnteredRoom(hero.Id, room.Id, target.Id, arrival, firstVisit));
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

    /// <summary>Heroes walk through allies but not monsters, walls, pits or chests.</summary>
    private PathSearch HeroPathSearch(Hero hero, Room room) =>
        PathSearch.Run(
            hero.Position,
            position => room.Layout[position].IsWalkable() && room.MonsterAt(position) is null,
            Turn.MovementPoints);

    private bool CanHeroStop(Room room, Hero hero, GridPosition position) =>
        !State.ActiveHeroesIn(room).Any(other => other != hero && other.Position == position);
}
