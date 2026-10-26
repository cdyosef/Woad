using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class FireTests
{
    [Fact]
    public void ASixOnFireDiceSetsTheTargetAlightEvenOnAMiss()
    {
        // Firebolt 6, 1, 1 (8, the 6 banked as a Power Die) against the Knight's 6 + armor 2: a miss.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1.K..
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1, 1, 6), cards: ["firebolt"]);
        Hero hero = session.HeroInSeat();
        Monster knight = session.OnlyMonster();

        session.Do(new UseCard(hero.Id, hero.Card("firebolt").Id, knight.Position));
        IReadOnlyList<GameEvent> events = session.Do(new BankPowerDie(hero.Id));

        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
        Assert.Equal(new Ignited(knight.Id, 1, hero.Id), events.SingleOf<Ignited>());
        Assert.Equal((1, hero.Id), (knight.FireDice, knight.IgnitedBy));
    }

    [Fact]
    public void FireDiceWithoutASixDontCatch()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1.d..
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 5), cards: ["firebolt"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("firebolt").Id, dummy.Position));

        Assert.Empty(events.OfType<Ignited>());
        Assert.Equal(0, dummy.FireDice);
    }

    private const string DummyRoom = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        ......d..
        .........
        """;

    [Fact]
    public void ABurningMonsterIsAttackedByItsFireAtTheStartOfItsTurnAndASixGrowsIt()
    {
        GameSession session = TestRun.Start(DummyRoom, new ScriptedDice(6));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        dummy.FireDice = 1;
        dummy.IgnitedBy = hero.Id;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(2, events.SingleOf<FireBurned>().FireDiceLeft);
        Assert.Equal((2, 2), (dummy.Hearts, dummy.FireDice));
    }

    [Fact]
    public void AOneSnuffsAFireDieOut()
    {
        GameSession session = TestRun.Start(DummyRoom, new ScriptedDice(1));
        Monster dummy = session.OnlyMonster();
        dummy.FireDice = 1;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.Equal(new FireOut(dummy.Id, FireOutCause.Snuffed), events.SingleOf<FireOut>());
        Assert.Equal(0, dummy.FireDice);
    }

    [Fact]
    public void WhoeverSetTheFireGetsTheKill()
    {
        GameSession session = TestRun.Start(DummyRoom, new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        dummy.Hearts = 1;
        dummy.FireDice = 1;
        dummy.IgnitedBy = hero.Id;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new MonsterDefeated(dummy.Id, hero.Id), events.SingleOf<MonsterDefeated>());
        Assert.Equal(1, hero.Kills);
    }

    [Fact]
    public void ASixSpreadsTheFireToANeighborOfAnySide()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1d...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 3, 1)); // The Dummy burns (6, spreading to the hero), then the hero burns (3 against 1).
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        dummy.FireDice = 1;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new FireSpread(dummy.Id, hero.Id, 1), events.SingleOf<FireSpread>());
        Assert.Equal(1, hero.FireDice);
    }

    [Fact]
    public void ABurningHeroIsAttackedAtTheStartOfTheirTurnAndKeelingOverEndsIt()
    {
        // Seat 2's fire rolls 4 against their defense 2: a hit, their last heart.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1....
            .........
            .........
            ......2..
            .........
            """, new ScriptedDice(4, 2), heroes: 2);
        Hero burning = session.HeroInSeat(1);
        burning.FireDice = 1;
        burning.Hearts = 1;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat(0).Id));

        Assert.Equal("Burned to death", events.SingleOf<HeroKeeledOver>().Bane);
        Assert.Equal(new TurnStarted(session.HeroInSeat(0).Id), events[^1]);
        Assert.Equal(1, burning.FireDice);
    }

    [Fact]
    public void WadingThroughWaterPutsTheFireOut()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1,....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        hero.FireDice = 2;
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(5, 4)));

        Assert.Equal(new FireOut(hero.Id, FireOutCause.Water), events.SingleOf<FireOut>());
        Assert.Equal(0, hero.FireDice);
    }

    [Fact]
    public void FireMonstersSetHeroesAlightWithASix()
    {
        // The Torch's 6 adds a Power Die (1): 7 against defense 2.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1t...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1, 2, 3, 1)); // Then the hero's fire, 3 against 1.
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new Ignited(hero.Id, 1, session.OnlyMonster().Id), events.SingleOf<Ignited>());
    }

    [Fact]
    public void StoppingDroppingAndRollingTakesTheWholeTurnAndLeavesYouProne()
    {
        GameSession session = TestRun.Start(DummyRoom, new ScriptedDice(3), heroes: 2);
        Hero hero = session.HeroInSeat(0);
        Assert.Contains("not on fire", session.Refuse(new StopDropRoll(hero.Id)));
        hero.FireDice = 2;

        IReadOnlyList<GameEvent> events = session.Do(new StopDropRoll(hero.Id));

        Assert.Equal(new FireOut(hero.Id, FireOutCause.Rolled), events.SingleOf<FireOut>());
        Assert.True(hero.Prone);
        Assert.Equal(new TurnStarted(session.HeroInSeat(1).Id), events[^1]);

        session.HeroInSeat(1).FireDice = 1;
        session.Do(new RollMovement(session.HeroInSeat(1).Id));
        Assert.Contains("whole turn", session.Refuse(new StopDropRoll(session.HeroInSeat(1).Id)));
    }
}

public class ProneTests
{
    private const string Open = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void LyingDownCostsAStepCrawlingCostsDoubleAndGettingUpCostsTwo()
    {
        // Wend 3 + Speed 2: 5 steps.
        GameSession session = TestRun.Start(Open, new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        session.Do(new GoProne(hero.Id));
        Assert.Equal((true, 4), (hero.Prone, session.State.Turn!.MovementPoints));
        Assert.Equal(2, session.GetReachableTiles()[new GridPosition(5, 4)]);
        Assert.DoesNotContain(new GridPosition(7, 4), session.GetReachableTiles().Keys);
        Assert.Empty(session.GetLeapTargets());

        session.Do(new MoveHero(hero.Id, new GridPosition(5, 4)));
        session.Do(new GetUp(hero.Id));

        Assert.Equal((false, 0), (hero.Prone, session.State.Turn!.MovementPoints));
    }

    [Fact]
    public void GettingUpTakesTwoSteps()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(1), stats: GameRules.HeroBaseStats with { Speed = 0 });
        Hero hero = session.HeroInSeat();
        hero.Prone = true;
        session.Do(new RollMovement(hero.Id));

        Assert.Contains("takes 2 steps", session.Refuse(new GetUp(hero.Id)));
    }

    [Fact]
    public void MeleeOnSomeoneProneRollsAnExtraDie()
    {
        // The Grunt rolls 2 dice (3, 3) instead of 1, against defense 5.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1m...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3, 3, 5));
        Hero hero = session.HeroInSeat();
        hero.Prone = true;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(2, events.SingleOf<AttackRolled>().Roll.Faces.Count);
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void RangedAttacksOnSomeoneProneFaceAnExtraDefenseDie()
    {
        // The Sniper's die (3) against 2 defense dice (1, 1) instead of 1.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1...n
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3, 1, 1));
        Hero hero = session.HeroInSeat();
        hero.Prone = true;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(2, events.SingleOf<DefenseRolled>().Roll.Faces.Count);
    }

    [Fact]
    public void DivingInDeepWaterHidesYouFromRangedAttacks()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ===1=...a
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3, 1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(2, 4)));

        Assert.True(session.Do(new GoProne(hero.Id)).SingleOf<WentProne>().Diving);
        Assert.True(session.IsDiving(hero));

        // Afloat on the Rumble die: the Archer has nothing to shoot at.
        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Empty(events.OfType<AttackRolled>());
    }
    /// <summary>The hero at (4,4), a dummy well off at (6,7).</summary>
    private const string FloorRoom = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        ......d..
        .........
        """;

    [Fact]
    public void FireAimedAtBareFloorSetsItBurningForThreeRounds()
    {
        GameSession session = TestRun.Start(FloorRoom, cards: ["firebolt"]);
        Hero hero = session.HeroInSeat();
        var floor = new GridPosition(5, 4);

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("firebolt").Id, floor));

        Assert.Equal(new FloorIgnited("A", [floor], GameRules.FlameRounds), events.SingleOf<FloorIgnited>(), new FloorComparer());
        Room room = session.RoomById();
        session.EndRound();
        session.EndRound();
        Assert.True(room.IsBurning(floor));
        Assert.Contains(session.EndRound(), gameEvent => gameEvent is FloorBurnedOut);
        Assert.False(room.IsBurning(floor));
    }

    [Fact]
    public void WalkingThroughBurningFloorSetsAHeroAlight()
    {
        GameSession session = TestRun.Start(FloorRoom, new ScriptedDice(3), cards: ["firebolt"]);
        Hero hero = session.HeroInSeat();
        session.Do(new UseCard(hero.Id, hero.Card("firebolt").Id, new GridPosition(5, 4)));

        session.Do(new RollMovement(hero.Id));
        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(6, 4)));

        Assert.Equal(1, hero.FireDice);
        Assert.Single(events.OfType<Ignited>());
    }

    [Fact]
    public void MonstersKeepOffBurningFloor()
    {
        // The grunt (2 moves) at (4,1) would walk straight down to the hero at (4,4); (4,2) is burning.
        GameSession session = TestRun.Start("""
            .........
            ....m....
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice([.. Enumerable.Repeat(1, 10)]), cards: ["firebolt"]);
        Hero hero = session.HeroInSeat();
        session.Do(new UseCard(hero.Id, hero.Card("firebolt").Id, new GridPosition(4, 2)));

        session.EndRound();

        Monster grunt = session.OnlyMonster();
        Assert.False(session.RoomById().IsBurning(grunt.Position));
        Assert.Equal(0, grunt.FireDice);
    }

    /// <summary>Compares floor fires by value (their tile lists are lists).</summary>
    private sealed class FloorComparer : IEqualityComparer<FloorIgnited>
    {
        public bool Equals(FloorIgnited? x, FloorIgnited? y) =>
            x is not null && y is not null && x.RoomId == y.RoomId && x.Rounds == y.Rounds && x.Tiles.SequenceEqual(y.Tiles);

        public int GetHashCode(FloorIgnited obj) => obj.RoomId.GetHashCode();
    }
}
