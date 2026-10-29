using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The Icarus Wings, worn as an accessory: flight of their own, apart from Wend. Beating them rolls a
/// d6 of flight (a 6 rolls a Power Die straight in, and so on). Aloft, a hero flies over pits, water,
/// traps and figures (not walls), a step a tile; only once the flight is used up do they choose: land
/// where they are, or beat the wings again, for as long as they dare: a 1 breaks them. The end of the
/// turn brings them down where they are. Broken wings drop them where they are (into the pit, if that's where), prone and stunned, with
/// the rest of the turn's Wend gone.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The Icarus Wings the active hero wears, if any.</summary>
    private static CardInstance? WingsOf(Hero hero) => hero.EquippedCards.FirstOrDefault(card => card.Definition.Wings);

    /// <summary>Why the active hero can't beat their wings now, or null.</summary>
    public string? CheckBeatWings()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (WingsOf(hero) is null)
        {
            return "You've no wings on.";
        }

        if (hero.Prone)
        {
            return "You can't take off lying down: get up first.";
        }

        if (hero.Aloft && CanStillFly())
        {
            return "Use up your flight first: then beat your wings again, or land.";
        }

        return CheckHeld(hero);
    }

    /// <summary>Tiles the active hero, aloft, could fly to now, with the flight each costs.</summary>
    public IReadOnlyDictionary<GridPosition, int> GetFlightTiles()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, FlightPoints: > 0 } || !ActiveHero.Aloft)
        {
            return new Dictionary<GridPosition, int>();
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return FlightSearch(hero, room).Distances
            .Where(entry => entry.Value > 0 && CanHeroStop(room, hero, entry.Key) && room.MonsterAt(entry.Key) is null)
            .ToDictionary();
    }

    /// <summary>Why the active hero can't land now, or null: only once their flight is used up.</summary>
    public string? CheckLand()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || !ActiveHero.Aloft)
        {
            return "You're not in the air.";
        }

        return CanStillFly() ? "Use up your flight first: then land, or beat your wings again." : null;
    }

    /// <summary>Flight left, and somewhere to fly with it (boxed in, a hero may come down).</summary>
    private bool CanStillFly() => Turn.FlightPoints > 0 && GetFlightTiles().Count > 0;

    private PathSearch FlightSearch(Hero hero, Room room) =>
        PathSearch.Run(
            hero.Position,
            tile => room.Layout[tile].IsWalkable(flying: true) && room.MonsterAt(tile) is null,
            Turn.FlightPoints);

    private string? Handle(BeatWings command, List<GameEvent> events)
    {
        if (CheckBeatWings() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Reveal(hero, events);
        int face = RollOneDie();
        if (face == 1)
        {
            BreakWings(hero, WingsOf(hero)!, events);
            return null;
        }

        // A 6 rolls a Power Die straight in, and its 6 another; their 1s are only 1s.
        List<int> faces = [face];
        while (faces[^1] == GameRules.PowerDieFace)
        {
            faces.Add(RollOneDie());
        }

        hero.Aloft = true;
        Turn.FlightPoints += faces.Sum();
        events.Add(new WingsBeat(hero.Id, faces, Turn.FlightPoints));
        return null;
    }

    private string? Handle(Fly command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (!hero.Aloft)
        {
            return "Beat your wings to take off first.";
        }

        if (!GetFlightTiles().TryGetValue(command.Destination, out int cost))
        {
            return "You can't fly there.";
        }

        Room room = ActiveRoom;
        IReadOnlyList<GridPosition> path = FlightSearch(hero, room).PathTo(command.Destination);
        Turn.FlightPoints -= cost;
        hero.Position = command.Destination;
        events.Add(new Flew(hero.Id, path, Turn.FlightPoints));
        return null;
    }

    private string? Handle(Land command, List<GameEvent> events)
    {
        if (CheckLand() is { } error)
        {
            return error;
        }

        Touchdown(ActiveHero, events);
        return null;
    }

    /// <summary>Back on the ground, wherever they are: a pit drops them in, a trap goes off.</summary>
    private void Touchdown(Hero hero, List<GameEvent> events)
    {
        Room room = State.RoomOf(hero);
        hero.Aloft = false;
        Turn.FlightPoints = 0;
        events.Add(new Landed(hero.Id, hero.Position));
        if (room.Layout[hero.Position] == TileKind.Pit)
        {
            FallIntoPit(hero, room, hero.Position, events);
        }
        else if (room.TrapAt(hero.Position) is { } trap)
        {
            SpringTrap(hero, room, trap, hero.Position, events);
        }
    }

    /// <summary>The wings break: down they come, prone and stunned, their Wend gone for the turn.</summary>
    private void BreakWings(Hero hero, CardInstance wings, List<GameEvent> events)
    {
        hero.Remove(wings);
        events.Add(new WingsBroke(hero.Id, hero.Position));
        events.Add(new CardConsumed(hero.Id, wings.Id));
        if (hero.Aloft)
        {
            Touchdown(hero, events);
        }

        Turn.EndMove();
        Turn.MoveDiceUsed += MoveDiceLeft;
        if (!hero.IsActive)
        {
            return;
        }

        if (!hero.Prone)
        {
            hero.Prone = true;
            events.Add(new WentProne(hero.Id, IsDiving(hero)));
        }

        Stun(hero, powerful: false, 1, events);
    }
}
