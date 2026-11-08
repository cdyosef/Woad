using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>
/// Drowning by Blue Hearts: each turn ended under the water a heart turns blue (breath held); with all of them blue,
/// drowning costs a heart; out of the deep, one turns back each turn.
/// </summary>
public sealed class BlueHeartTests
{
    private const string Pond = """
        .........
        .........
        .........
        ....=....
        ....1....
        .........
        .........
        .........
        .........
        """;

    private static (GameSession Session, Hero Hero) Under()
    {
        GameSession session = TestRun.Start(Pond, new ScriptedDice(1, 5, 5));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));
        hero.Prone = true;
        return (session, hero);
    }

    [Fact]
    public void DivingUnderAHeartTurnsBlueEvenWithRumbleDiceLeft()
    {
        (GameSession session, Hero hero) = Under();

        Assert.Equal(new BreathHeld(hero.Id, 1, hero.Hearts), session.Do(new EndTurn(hero.Id)).SingleOf<BreathHeld>());
        Assert.Equal(0, session.State.Turn!.AttackDiceUsed);
    }

    [Fact]
    public void WithEveryHeartBlueDrowningCostsAHeart()
    {
        (GameSession session, Hero hero) = Under();
        hero.BlueHearts = hero.Hearts;
        int hearts = hero.Hearts;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Single(events.OfType<Drowned>());
        Assert.Equal(hearts - GameRules.DrownDamage, hero.Hearts);
        Assert.Equal(hero.Hearts, hero.BlueHearts);
    }

    [Fact]
    public void OutOfTheDeepABreathComesBackEachTurnButNotLyingInTheShallows()
    {
        GameSession session = TestRun.Start(Pond.Replace("....1....", "....1,...").Replace("....=....", "........."));
        Hero hero = session.HeroInSeat();
        hero.BlueHearts = 2;

        Assert.Equal(new BreathCaught(hero.Id, 1), session.Do(new EndTurn(hero.Id)).SingleOf<BreathCaught>());

        // Face down in the shallows, no breath.
        hero.Position = new GridPosition(5, 4);
        hero.Prone = true;
        session.EndRound();
        Assert.Equal(1, hero.BlueHearts);
    }

    [Fact]
    public void BlueHeartsNeverOutnumberTheHeartsLeft()
    {
        (_, Hero hero) = Under();
        hero.BlueHearts = 3;
        hero.Hearts = 2;

        Assert.Equal(2, hero.BlueHearts);
    }
}
