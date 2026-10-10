using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

public sealed partial class GameSession
{
    private string? Handle(TakeStairs command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.Layout[hero.Position] != TileKind.StairsDown)
        {
            return "Stand on the stairs first.";
        }

        if (CheckCanMove(1) is { } error)
        {
            return error;
        }

        Room? arrival = State.Rooms.Values.FirstOrDefault(other => other.Floor == room.Floor + 1 && other.Role == RoomRole.Arrival);
        if (arrival is null)
        {
            return "These stairs lead nowhere.";
        }

        if (Landing(arrival) is not { } landing)
        {
            return "The way down is blocked.";
        }

        hero.RoomId = arrival.Id;
        hero.Position = landing;
        Turn.MovementPoints -= 1;
        Turn.HasMoved = true;
        arrival.Explored = true;

        bool firstTime = arrival.Floor > hero.DeepestFloor;
        if (firstTime)
        {
            hero.DeepestFloor = arrival.Floor;
            hero.LuckRemaining = Math.Max(0, hero.EffectiveStats.Luck);
            foreach (CardInstance card in hero.AllCards.Where(card => card.Definition.Usage == CardUsage.Charges))
            {
                card.ChargesRemaining = card.Definition.Charges;
            }
        }

        events.Add(new HeroDescended(hero.Id, room.Id, arrival.Id, arrival.Floor, landing, firstTime));
        return null;
    }

    private string? Handle(Escape command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.Layout[hero.Position] != TileKind.Exit)
        {
            return "Stand on the exit first.";
        }

        if (room.Monsters.Any(monster => monster.Definition.Boss))
        {
            return "The exit is sealed until the boss here is defeated.";
        }

        hero.Status = HeroStatus.Escaped;
        events.Add(new HeroEscaped(hero.Id, hero.TreasureValue));
        EndRunIfOver(events);
        if (State.Phase == RunPhase.InProgress)
        {
            AdvanceTurn(hero.Seat, events);
        }

        return null;
    }

    /// <summary>Where a hero coming down the stairs lands: the first free arrival tile, else near the center.</summary>
    private GridPosition? Landing(Room room)
    {
        var center = new GridPosition(RoomGrid.Center, RoomGrid.Center);
        return room.ArrivalTiles
            .Concat(RoomGrid.AllPositions().OrderBy(tile => tile.ManhattanDistanceTo(center)))
            .Where(tile => room.Layout[tile] == TileKind.Floor && !IsOccupied(room, tile))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }
}
