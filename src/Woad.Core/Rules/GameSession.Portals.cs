using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Portals: a standing ring of stone in a dungeon's entrance, its floor-1 stairs room and its boss room.
/// Each is dark until a hero beside it channels a banked Power Die or a Magic Die into it; then it's lit,
/// and from beside any lit portal a hero steps through, free, to any other lit one, coming out beside it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The portal a hero at <paramref name="at"/> stands beside, or null.</summary>
    public static GridPosition? PortalAt(Room room, GridPosition at) =>
        room.TilesOfKind(TileKind.Portal).Where(tile => tile.ChebyshevDistanceTo(at) <= 1).Select(tile => (GridPosition?)tile).FirstOrDefault();

    /// <summary>Why the active hero can't light the portal beside them (with a Magic Die, or else a Power Die) now, or null.</summary>
    public string? CheckLightPortal(bool magic)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (PortalAt(room, hero.Position) is null)
        {
            return "Stand beside the portal first.";
        }

        if (room.PortalLit)
        {
            return "The portal is already lit.";
        }

        return magic
            ? hero.MagicCharged < 1 ? "Lighting it with magic takes a Magic Die, and you've none charged." : null
            : hero.Power.Banked < 1 ? "Lighting it takes a banked Power Die, and you've none." : null;
    }

    private string? Handle(LightPortal command, List<GameEvent> events)
    {
        if (CheckLightPortal(command.Magic) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        if (command.Magic)
        {
            hero.MagicCharged--;
            events.Add(new MagicDiceSpent(hero.Id, 1, hero.MagicCharged));
        }
        else
        {
            hero.Power.Banked--;
            events.Add(new PowerDiceSpent(hero.Id, 1, hero.Power.Banked));
        }

        ActiveRoom.PortalLit = true;
        events.Add(new PortalLit(hero.Id, ActiveRoom.Id));
        return null;
    }

    /// <summary>Where the active hero could step through to from the portal beside them: every other lit portal.</summary>
    public IReadOnlyList<Room> GetPortalDestinations()
    {
        if (State.Turn is null || CheckTakePortal(null) is not null)
        {
            return [];
        }

        Room room = ActiveRoom;
        return [.. State.Rooms.Values.Where(other => other != room && other.PortalLit).OrderBy(other => other.Storey).ThenBy(other => other.Id, StringComparer.Ordinal)];
    }

    /// <summary>Why the active hero can't step through the portal beside them (to <paramref name="toRoomId"/>, if given), or null.</summary>
    public string? CheckTakePortal(string? toRoomId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (PortalAt(room, hero.Position) is null)
        {
            return "Stand beside the portal first.";
        }

        if (!room.PortalLit)
        {
            return "The portal is dark: channel a Power Die or a Magic Die into it to light it.";
        }

        if (CheckHeld(hero) is { } held)
        {
            return held;
        }

        if (toRoomId is null)
        {
            return State.Rooms.Values.Any(other => other != room && other.PortalLit) ? null : "No other portal is lit yet.";
        }

        return State.Rooms.TryGetValue(toRoomId, out Room? target) && target != room && target.PortalLit && target.TilesOfKind(TileKind.Portal).Any()
            ? null
            : "That portal isn't lit.";
    }

    private string? Handle(TakePortal command, List<GameEvent> events)
    {
        if (CheckTakePortal(command.ToRoomId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room from = ActiveRoom;
        Room target = State.Rooms[command.ToRoomId];
        if (LandingNear(target, target.TilesOfKind(TileKind.Portal).First()) is not { } landing)
        {
            return "Something blocks the far portal.";
        }

        hero.RoomId = target.Id;
        hero.Position = landing;
        target.Explored = true;
        events.Add(new PortalTaken(hero.Id, from.Id, target.Id, landing));
        if (target.Floor > hero.DeepestFloor)
        {
            ReachNewFloor(hero, target.Floor, events);
        }

        return null;
    }
}
