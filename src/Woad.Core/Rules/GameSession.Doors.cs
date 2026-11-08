using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Doors beyond a single lock: some have two locks, each opened by its own key or picking; some two traps, the
/// second set once the first is gone, switched off by hand (no roll) from the side they aren't armed against;
/// and some are barred on one side, where the bar lifts free, while from the other the door must be smashed in.
/// A roll at a door or chest pictures it: its locks, how heavily bolted, and any trap on it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What an Od Hand check is at, to picture beside the roll; null for a search, a steal and the like.</summary>
    private static RollTarget? RollTargetOf(Room room, OdHandCheck check)
    {
        if (room.Passages.FirstOrDefault(door => room.DoorTile(door.Key) == check.Target && !door.Value.IsHidden).Value is { } door
            && check.Kind is OdHandCheckKind.PickDoor or OdHandCheckKind.Disarm)
        {
            return new RollTarget("door", door.Difficulty, door.IsLocked ? door.Locks : 0, door.Trap?.Kind, door.Trap?.Revealed ?? false, door.IsBarred);
        }

        return check.Kind switch
        {
            OdHandCheckKind.PickChest or OdHandCheckKind.Disarm when room.Layout.Contains(check.Target) && room.Layout[check.Target] == TileKind.Chest =>
                new RollTarget("chest", room.ChestLockAt(check.Target) ?? check.Difficulty, room.ChestLockAt(check.Target) is null ? 0 : 1, room.ChestTrapAt(check.Target)?.Kind, room.ChestTrapAt(check.Target)?.Revealed ?? false),
            OdHandCheckKind.PickGate => new RollTarget("gate", check.Difficulty, 1, null, false),
            OdHandCheckKind.Disarm when room.TrapAt(check.Target) is { } trap => new RollTarget("trap", trap.Difficulty, 0, trap.Kind, true),
            _ => null,
        };
    }

    /// <summary>Why the active hero can't lift the bar on that door now, or null.</summary>
    public string? CheckLiftBar(Direction side)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Room room = ActiveRoom;
        if (room.PassageOn(side) is not { IsBarred: true } door)
        {
            return "That door isn't barred.";
        }

        if (door.BarredFrom != room.Id)
        {
            return "It's barred from the other side: smash it in, or find another way round.";
        }

        return ActiveHero.Position.ChebyshevDistanceTo(room.DoorTile(side)) <= 1 ? null : "Go up to the door first.";
    }

    private string? Handle(LiftBar command, List<GameEvent> events)
    {
        if (CheckLiftBar(command.Door) is { } error)
        {
            return error;
        }

        ActiveRoom.PassageOn(command.Door)!.BarredFrom = null;
        events.Add(new BarLifted(ActiveHero.Id, ActiveRoom.Id, command.Door));
        return null;
    }
}
