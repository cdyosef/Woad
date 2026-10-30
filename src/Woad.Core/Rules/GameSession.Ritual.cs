using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The cult's ritual (in a graveyard, with Father Ambrose's second quest): the cabal of necromancers round
/// the circle in the temple and the cult leader at the pulpit chant, and the party has so many rounds to
/// kill them all. Fail, and the portal to the realm of death opens on the circle: the graveyard is
/// cursed, an ambient purple glow lights it, every one of the dead knows where the heroes are, and they
/// come a d6 faster every turn.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A round passes at the ritual; when the last runs out, the portal opens.</summary>
    private void TickRitual(List<GameEvent> events)
    {
        if (State.Round == 0 || State.RitualRoundsLeft == 0 || State.RitualStopped || State.PortalOpen)
        {
            return;
        }

        State.RitualRoundsLeft--;
        events.Add(new RitualChanted(State.RitualRoundsLeft));
        if (State.RitualRoundsLeft == 0)
        {
            OpenPortal(events);
        }
    }

    /// <summary>The ritual's done: the portal opens, and the graveyard's dead all rise and come.</summary>
    private void OpenPortal(List<GameEvent> events)
    {
        State.PortalOpen = true;
        foreach (Room room in State.Rooms.Values.Where(room => room.RitualCircle is not null))
        {
            room.Cursed = true;
            room.HordeAlert = 3;
            events.Add(new PortalOpened(room.Id, room.RitualCircle!.Value));
            foreach (Monster dead in room.Monsters.Where(monster => monster.Asleep && FactionOf(monster) == Faction.Undead).OrderBy(monster => monster.Id))
            {
                Wake(dead, events);
            }
        }
    }

    /// <summary>A ritualist fell: with none of them left, the ritual is broken.</summary>
    private void BreakRitual(Room room, List<GameEvent> events)
    {
        if (State.RitualRoundsLeft == 0 || State.RitualStopped || State.PortalOpen || room.Monsters.Any(monster => monster.Definition.Ritualist))
        {
            return;
        }

        State.RitualStopped = true;
        State.RitualRoundsLeft = 0;
        events.Add(new RitualBroken(room.Id));
    }
}
