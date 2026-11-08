using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Leviathan Jr.'s snatch: a 6 on its claws that hits seizes the hero, pulled into the deep and under, till they break free or it's hurt.</summary>
public sealed class SnatchTests
{
    private const string Pool = """
        .........
        .........
        ...===...
        ...=£=...
        ....1....
        .........
        .........
        .........
        .........
        """;

    private static GameSession Start(ScriptedDice dice, int attackDice = 1) =>
        TestRun.Start(Pool, dice, stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 0, AttackDice = attackDice });

    [Fact]
    public void ASixOnItsClawsThatHitsSnatchesTheHeroAndPullsThemUnder()
    {
        // Its claw: a 6 and the 2 it rolls in, 8, against no defense.
        GameSession session = Start(new ScriptedDice(6, 2, 1, 1, 1, 1), attackDice: 2);
        Hero hero = session.HeroInSeat();
        Monster snatcher = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new Snatched(snatcher.Id, hero.Id, 8), events.SingleOf<Snatched>());
        Assert.Equal((snatcher.Id, true, 8), (hero.GrabbedBy, hero.HeldUnder, hero.GripTotal));
        Assert.Equal(TileKind.DeepWater, session.RoomById().Layout[hero.Position]);
        Assert.Equal(1, hero.Position.ManhattanDistanceTo(snatcher.Position));
        Assert.Same(snatcher, session.HeldBy(hero));
    }

    [Fact]
    public void WithoutASixItsClawsDontSnatch()
    {
        GameSession session = Start(new ScriptedDice(5, 1, 1, 1));
        Hero hero = session.HeroInSeat();

        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is Snatched);
        Assert.Null(hero.GrabbedBy);
    }

    [Fact]
    public void UnderTheWaterSnatchedTheyHoldTheirBreathAndARumbleRollOverTheGripFreesThem()
    {
        // Held under beside it with a grip of 8; a struggle of 5 + 4, 9.
        GameSession session = Start(new ScriptedDice(5, 4), attackDice: 2);
        Hero hero = session.HeroInSeat();
        Monster snatcher = session.OnlyMonster();
        hero.Position = new GridPosition(3, 3);
        hero.GrabbedBy = snatcher.Id;
        hero.HeldUnder = true;
        hero.GripTotal = 8;
        Assert.Null(session.CheckBreakFree());

        StruggledFree struggle = session.Do(new BreakFree(hero.Id)).SingleOf<StruggledFree>();

        Assert.True(struggle.Free);
        Assert.Equal((null, false, 0), (hero.GrabbedBy, hero.HeldUnder, hero.GripTotal));
    }

    [Fact]
    public void LeftUnderItTheirBreathRunsOut()
    {
        GameSession session = Start(new ScriptedDice(6, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1), attackDice: 2);
        Hero hero = session.HeroInSeat();
        session.EndRound();

        Assert.Equal(new BreathHeld(hero.Id, 1, hero.Hearts), session.Do(new EndTurn(hero.Id)).SingleOf<BreathHeld>());
    }

    [Fact]
    public void HoldingSomeoneItSwimsOffFromEveryoneElseDraggingThemUnderBehindIt()
    {
        GameSession session = TestRun.Start("""
            2........
            ..=======
            ..=======
            ..=======
            ..==¢1===
            ..=======
            ..=======
            ..=======
            ..=======
            """, new ScriptedDice(1, 1, 1, 1, 1, 1, 1, 1), heroes: 2, stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 20 });
        Hero held = session.HeroInSeat(0), other = session.HeroInSeat(1);
        Monster dragger = session.OnlyMonster();
        held.GrabbedBy = dragger.Id;
        held.GripTotal = 6;
        int before = dragger.Position.ChebyshevDistanceTo(other.Position);

        session.EndRound();

        Assert.True(dragger.Position.ChebyshevDistanceTo(other.Position) > before, $"it swam away ({dragger.Position})");
        Assert.Equal(1, held.Position.ManhattanDistanceTo(dragger.Position));
        Assert.Equal(TileKind.DeepWater, session.RoomById().Layout[held.Position]);
        Assert.True(held.HeldUnder);
    }

    [Fact]
    public void HurtItLetsGo()
    {
        GameSession session = Start(new ScriptedDice(6, 2, 1, 1, 1, 6, 6, 3, 3), attackDice: 2);
        Hero hero = session.HeroInSeat();
        Monster snatcher = session.OnlyMonster();
        session.EndRound();

        session.Do(new AttackMonster(hero.Id, snatcher.Id, Dice: 2));
        while (session.State.Turn!.PendingRoll is not null || session.State.Turn.UnsettledPowerDice > 0)
        {
            session.Execute(session.State.Turn.UnsettledPowerDice > 0 ? new BankPowerDie(hero.Id) : new ConfirmAttack(hero.Id));
        }

        Assert.True(snatcher.Hearts < snatcher.MaxHearts);
        Assert.Equal((null, false), (hero.GrabbedBy, hero.HeldUnder));
    }
}
