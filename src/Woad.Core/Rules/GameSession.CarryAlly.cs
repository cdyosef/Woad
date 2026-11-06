using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Carrying a downed ally: a hero with empty arms picks up an ally Keeling Over beside them, in both carry
/// slots. The ally goes wherever the carrier goes (through doors, down stairs, out of the dungeon) and takes
/// up no tile of their own, out of the monsters' reach; the carrier's arms are full (no Rumble or Od Hand)
/// and each Wend roll gives them half its steps. The ally keeps rolling their Keel Die; standing back up,
/// or their carrier falling, sets them down beside them.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't pick up this ally now, or null.</summary>
    public string? CheckPickUpAlly(int allyId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (State.HeroesIn(room).FirstOrDefault(other => other.Id == allyId && other != hero) is not { } ally)
        {
            return "There's nobody here to carry.";
        }

        if (!ally.IsKeelingOver)
        {
            return $"{ally.Name} can walk.";
        }

        if (hero.Position.ChebyshevDistanceTo(ally.Position) != 1)
        {
            return "Stand next to them to pick them up.";
        }

        return hero.CarrySlotsUsed > 0 ? "Your arms are full: put down what you're carrying first." : null;
    }

    /// <summary>Why the active hero can't set down the ally they carry now, or null.</summary>
    public string? CheckPutDownAlly()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (hero.CarriedAlly is null)
        {
            return "You aren't carrying anyone.";
        }

        return LandingNear(ActiveRoom, hero.Position) is null ? "There's nowhere to set them down." : null;
    }

    private string? Handle(PickUpAlly command, List<GameEvent> events)
    {
        if (CheckPickUpAlly(command.AllyId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Hero ally = State.GetHero(command.AllyId);
        hero.CarriedAlly = ally;
        ally.Carrier = hero;
        events.Add(new AllyPickedUp(hero.Id, ally.Id));
        return null;
    }

    private string? Handle(PutDownAlly command, List<GameEvent> events)
    {
        if (CheckPutDownAlly() is { } error)
        {
            return error;
        }

        SetDownAlly(ActiveHero, events);
        return null;
    }

    /// <summary>Sets down whoever <paramref name="carrier"/> carries, on the nearest free tile beside them.</summary>
    private void SetDownAlly(Hero carrier, List<GameEvent> events)
    {
        if (carrier.CarriedAlly is not { } ally)
        {
            return;
        }

        Room room = State.RoomOf(carrier);
        GridPosition at = LandingNear(room, carrier.Position) ?? SafeTile(room, carrier.Position);
        ReleaseCarry(carrier);
        ally.Position = at;
        events.Add(new AllySetDown(carrier.Id, ally.Id, at));
    }

    /// <summary>Undoes a carry (from either side), the ally left where the carrier is.</summary>
    private static void ReleaseCarry(Hero hero)
    {
        Hero? carrier = hero.Carrier ?? (hero.CarriedAlly is null ? null : hero);
        if (carrier?.CarriedAlly is not { } ally)
        {
            return;
        }

        GridPosition at = carrier.Position;
        string roomId = carrier.RoomId;
        ally.Carrier = null;
        carrier.CarriedAlly = null;
        ally.Position = at;
        ally.RoomId = roomId;
    }
}
