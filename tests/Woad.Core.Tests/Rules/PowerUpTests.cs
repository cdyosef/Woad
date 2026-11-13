using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Powering up: every die left rolled at once, each 6 banked as a Power Die, and the turn over.</summary>
public sealed class PowerUpTests
{
    private const string Quiet = """
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

    [Fact]
    public void EveryDieLeftRollsAndEachSixBanks()
    {
        // HeroStats: 1 Rumble, 1 Wend and 1 Od Hand die.
        GameSession session = TestRun.Start(Quiet, new ScriptedDice(6, 3, 6), heroes: 2);
        Hero hero = session.HeroInSeat(0);
        Assert.Equal(3, session.PowerUpDice);

        IReadOnlyList<GameEvent> events = session.Do(new PowerUp(hero.Id));

        PoweredUp powered = events.SingleOf<PoweredUp>();
        Assert.Equal([6, 3, 6], powered.Roll.Faces);
        Assert.Equal(2, powered.Gained);
        Assert.Equal(2, hero.Power.Banked);
        Assert.Equal(session.HeroInSeat(1).Id, session.State.Turn!.HeroId);
    }

    [Fact]
    public void OnlyTheDiceLeftRollAndTheBankHoldsWhatItHolds()
    {
        GameSession session = TestRun.Start(Quiet, new ScriptedDice(2, 6, 6), heroes: 2);
        Hero hero = session.HeroInSeat(0);
        session.Do(new RollMovement(hero.Id));
        hero.Power.Banked = hero.Power.Capacity - 1;

        PoweredUp powered = session.Do(new PowerUp(hero.Id)).SingleOf<PoweredUp>();

        Assert.Equal((2, 1), (powered.Roll.Faces.Count, powered.Gained));
        Assert.True(hero.Power.IsFull);
    }

    [Fact]
    public void WithNothingLeftOrABankFullThereIsNothingToPowerUp()
    {
        GameSession session = TestRun.Start(Quiet, new ScriptedDice(), heroes: 2);
        Hero hero = session.HeroInSeat(0);
        hero.Power.Banked = hero.Power.Capacity;

        Assert.Contains("full", session.Refuse(new PowerUp(hero.Id)));
    }
}
