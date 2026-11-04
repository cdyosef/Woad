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
        // From beside the stairs (or on them), and free: no steps.
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (StairsAt(room, hero.Position) is not { } stairs)
        {
            return "Go up to the stairs first.";
        }

        if (CheckHeld(hero) is { } error)
        {
            return error;
        }

        if (room.Layout[stairs] == TileKind.StairsUp)
        {
            return ClimbStairs(hero, room, events);
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
        arrival.Explored = true;

        bool firstTime = arrival.Floor > hero.DeepestFloor;
        events.Add(new HeroDescended(hero.Id, room.Id, arrival.Id, arrival.Floor, landing, firstTime));
        if (firstTime)
        {
            ReachNewFloor(hero, arrival.Floor, events);
        }

        return null;
    }

    /// <summary>The stairs (up or down) a hero at <paramref name="at"/> stands on or beside, or null.</summary>
    public static GridPosition? StairsAt(Room room, GridPosition at) =>
        room.Positions.Where(tile => room.Layout[tile] is TileKind.StairsDown or TileKind.StairsUp && tile.ChebyshevDistanceTo(at) <= 1)
            .OrderBy(tile => tile.ChebyshevDistanceTo(at))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();

    /// <summary>Back up the stairs to the floor above: beside the stairs down there. Free.</summary>
    private string? ClimbStairs(Hero hero, Room room, List<GameEvent> events)
    {
        Room? above = State.Rooms.Values
            .Where(other => other.Floor == room.Floor - 1 && other.TilesOfKind(TileKind.StairsDown).Any())
            .OrderBy(other => other.MapPosition == room.MapPosition ? 0 : 1)
            .FirstOrDefault();
        if (above is null || LandingNear(above, above.TilesOfKind(TileKind.StairsDown).First()) is not { } landing)
        {
            return "The way up is blocked.";
        }

        hero.RoomId = above.Id;
        hero.Position = landing;
        events.Add(new HeroAscended(hero.Id, room.Id, above.Id, above.Floor, landing));
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

        // Out of a graveyard, the dead beside you get a swing at you as you go: drop, and you don't get away.
        if (Graveyard is not null)
        {
            foreach (Monster dead in room.Monsters.Where(monster => !monster.Asleep && FactionOf(monster) == Faction.Undead && monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) <= 1).OrderBy(monster => monster.Id).ToList())
            {
                events.Add(new ParthianSwing(dead.Id, hero.Id));
                dead.AttackDiceThisTurn = dead.Definition.AttackDice;
                _actingMonsterId = dead.Id;
                try
                {
                    MonsterAttacks(room, dead, hero, events);
                }
                finally
                {
                    _actingMonsterId = null;
                }

                if (!hero.IsActive || State.Phase != RunPhase.InProgress)
                {
                    return null;
                }
            }
        }

        // The hero's companions in the room leave with them, and the merchants following them.
        List<Hero> party = State.ActiveHeroesIn(room).Where(other => other == hero || other.OwnerId == hero.Id).ToList();
        RescueMerchants(party, room, events);
        foreach (Hero leaving in party)
        {
            leaving.Status = HeroStatus.Escaped;
            events.Add(new HeroEscaped(leaving.Id, leaving.TreasureValue));
            GrantXp(leaving, GameRules.XpForEscaping, events);
        }

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
        GridPosition center = room.Layout.Center;
        return room.ArrivalTiles
            .Concat(room.Positions.OrderBy(tile => tile.ManhattanDistanceTo(center)))
            .Where(tile => room.Layout[tile] == TileKind.Floor && !IsOccupied(room, tile) && !IsPassageTile(room, tile))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }
}
