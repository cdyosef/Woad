using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Carrying a downed ally: both carry slots, half speed, and they go wherever you go.</summary>
public class CarryAllyTests
{
    private const string Side = """
        .........
        .........
        .........
        .........
        ...12....
        .........
        .........
        .........
        .........
        """;

    private static (GameSession Session, Hero Hero, Hero Ally) Downed(ScriptedDice? dice = null)
    {
        GameSession session = TestRun.Start(Side, dice, heroes: 2);
        Hero hero = session.HeroInSeat(0), ally = session.HeroInSeat(1);
        ally.Status = HeroStatus.KeelingOver;
        return (session, hero, ally);
    }

    [Fact]
    public void ADownedAllyBesideYouCanBePickedUpIntoBothCarrySlots()
    {
        (GameSession session, Hero hero, Hero ally) = Downed();

        Assert.Contains(new AllyPickedUp(hero.Id, ally.Id), session.Do(new PickUpAlly(hero.Id, ally.Id)));

        Assert.Same(ally, hero.CarriedAlly);
        Assert.Equal(GameRules.CarrySlots, hero.CarrySlotsUsed);
        Assert.True(hero.IsCarrying);
        Assert.Equal(hero.Position, ally.Position);
        Assert.DoesNotContain(ally, session.State.HeroesIn(session.RoomById()));
    }

    [Fact]
    public void OnlyADownedAllyWithEmptyArms()
    {
        GameSession session = TestRun.Start(Side, heroes: 2);
        Hero hero = session.HeroInSeat(0), ally = session.HeroInSeat(1);
        Assert.Contains("can walk", session.CheckPickUpAlly(ally.Id));
    }

    [Fact]
    public void ACarrierMovesAtHalfSpeedAndTheAllyComesAlong()
    {
        // A Wend roll of 5 and the +2 Wend bonus: four steps (half of 7, rounded up) while carrying.
        (GameSession session, Hero hero, Hero ally) = Downed(new ScriptedDice(5));
        session.Do(new PickUpAlly(hero.Id, ally.Id));
        session.Do(new RollMovement(hero.Id));
        Assert.Equal(4, session.State.Turn!.MovementPoints);

        session.Do(new MoveHero(hero.Id, new GridPosition(3, 2)));

        Assert.Equal(new GridPosition(3, 2), ally.Position);
        Assert.Single(session.Do(new PutDownAlly(hero.Id)).OfType<AllySetDown>());
        Assert.Null(hero.CarriedAlly);
        Assert.Equal(1, ally.Position.ChebyshevDistanceTo(hero.Position));
        Assert.Contains(ally, session.State.HeroesIn(session.RoomById()));
    }
}
