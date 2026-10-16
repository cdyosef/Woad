using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class KeelingOverTests
{
    private const string TwoHeroes = """
        .........
        .........
        .........
        .........
        ....1....
        .....2...
        .........
        .........
        .........
        """;

    /// <summary>Seat 1 is Keeling Over and it's their turn.</summary>
    private static (GameSession Session, Hero Standing, Hero Fallen) FallenTurn(ScriptedDice dice, Stats? stats = null)
    {
        GameSession session = TestRun.Start(TwoHeroes, dice, heroes: 2, stats: stats);
        Hero fallen = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;
        fallen.Hearts = 0;
        session.Do(new EndTurn(session.HeroInSeat(0).Id));
        return (session, session.HeroInSeat(0), fallen);
    }

    [Fact]
    public void AKeelSixIsAMarkOfLifeAndRollsAPowerDieThatCanEarnAnother()
    {
        (GameSession session, _, Hero fallen) = FallenTurn(new ScriptedDice(6, 6));

        var rolled = session.Do(new RollKeel(fallen.Id)).SingleOf<KeelRolled>();

        Assert.Equal((6, 6, 2, 0), (rolled.Keel, rolled.BonusDie, rolled.LifeGained, rolled.DeathGained));
        Assert.Equal((2, 0), (fallen.LifeMarks, fallen.DeathMarks));
    }

    [Fact]
    public void AKeelOneIsAMarkOfDeathAndRollsADoomDieThatCanEarnAnother()
    {
        (GameSession session, _, Hero fallen) = FallenTurn(new ScriptedDice(1, 1));

        var rolled = session.Do(new RollKeel(fallen.Id)).SingleOf<KeelRolled>();

        Assert.Equal((1, 1, 2), (rolled.Keel, rolled.BonusDie, rolled.DeathGained));
        Assert.Equal((0, 2), (fallen.LifeMarks, fallen.DeathMarks));
    }

    [Fact]
    public void OtherFacesDoNothingAndTheTurnPassesOn()
    {
        (GameSession session, Hero standing, Hero fallen) = FallenTurn(new ScriptedDice(3));

        IReadOnlyList<GameEvent> events = session.Do(new RollKeel(fallen.Id));

        Assert.Null(events.SingleOf<KeelRolled>().BonusDie);
        Assert.Equal((0, 0), (fallen.LifeMarks, fallen.DeathMarks));
        Assert.Equal(new TurnStarted(standing.Id), events[^1]);
        Assert.Contains("on your feet", session.Refuse(new RollKeel(standing.Id)));
    }

    [Fact]
    public void BankedPowerDiceEarnMarksOfLifeOnSixesButNoDeathOnOnes()
    {
        (GameSession session, _, Hero fallen) = FallenTurn(new ScriptedDice(4, 6, 1));
        fallen.Power.Banked = 2;

        var rolled = session.Do(new RollKeel(fallen.Id, PowerDice: 2)).SingleOf<KeelRolled>();

        Assert.Equal([6, 1], rolled.PowerFaces);
        Assert.Equal((1, 0), (fallen.LifeMarks, fallen.DeathMarks));
        Assert.Equal(0, fallen.Power.Banked);
    }

    [Fact]
    public void DoomDiceLeftByAlliesAreRolledInAndCountOnes()
    {
        (GameSession session, _, Hero fallen) = FallenTurn(new ScriptedDice(4, 1, 5));
        fallen.DoomDiceOwed = 2;

        var rolled = session.Do(new RollKeel(fallen.Id)).SingleOf<KeelRolled>();

        Assert.Equal([1, 5], rolled.DoomFaces);
        Assert.Equal((0, 1, 0), (fallen.LifeMarks, fallen.DeathMarks, fallen.DoomDiceOwed));
    }

    [Fact]
    public void EveryKeelRollAfterTheFirstAddsOneMoreDoomDie()
    {
        // Rolls: 3 (no doom); 3 + doom 4; 3 + doom 1, 5.
        (GameSession session, Hero standing, Hero fallen) = FallenTurn(new ScriptedDice(3, 3, 4, 3, 1, 5));

        Assert.Empty(session.Do(new RollKeel(fallen.Id)).SingleOf<KeelRolled>().DoomFaces);
        session.Do(new EndTurn(standing.Id));
        var second = session.Do(new RollKeel(fallen.Id)).SingleOf<KeelRolled>();
        session.Do(new EndTurn(standing.Id));
        var third = session.Do(new RollKeel(fallen.Id)).SingleOf<KeelRolled>();

        Assert.Equal((1, 1), (second.CreepingDoom, second.DoomFaces.Count));
        Assert.Equal((2, 2), (third.CreepingDoom, third.DoomFaces.Count));
        Assert.Equal((0, 1, 3), (fallen.LifeMarks, fallen.DeathMarks, fallen.KeelRolls));
    }

    [Fact]
    public void ThreeMarksOfLifeStandTheHeroUpWithAHeart()
    {
        (GameSession session, Hero standing, Hero fallen) = FallenTurn(new ScriptedDice(6, 3));
        fallen.LifeMarks = 2;
        fallen.DeathMarks = 2;

        IReadOnlyList<GameEvent> events = session.Do(new RollKeel(fallen.Id));

        Assert.Equal(new HeroStoodUp(fallen.Id, GameRules.HeartsOnRising), events.SingleOf<HeroStoodUp>());
        Assert.Equal((HeroStatus.Active, 1, 0, 0), (fallen.Status, fallen.Hearts, fallen.LifeMarks, fallen.DeathMarks));
        Assert.Equal(new TurnStarted(standing.Id), events[^1]);
    }

    [Fact]
    public void ReachingThreeOfBothAtOnceLifeWins()
    {
        (GameSession session, _, Hero fallen) = FallenTurn(new ScriptedDice(3, 6, 1));
        fallen.LifeMarks = 2;
        fallen.DeathMarks = 2;
        fallen.Power.Banked = 1;
        fallen.DoomDiceOwed = 1;

        session.Do(new RollKeel(fallen.Id, PowerDice: 1));

        Assert.Equal(HeroStatus.Active, fallen.Status);
    }

    [Fact]
    public void ThreeMarksOfDeathLeaveATombstoneHoldingWhatTheyFound()
    {
        (GameSession session, Hero standing, Hero fallen) = FallenTurn(new ScriptedDice(1, 1, 3), GameRules.HeroBaseStats);
        var gem = new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("gem"), broughtInByHeroId: null);
        fallen.AddToBackpack(gem);
        fallen.Keys = 1;
        fallen.DeathMarks = 1;
        fallen.Bane = "Slain by a Grunt";

        IReadOnlyList<GameEvent> events = session.Do(new RollKeel(fallen.Id));

        var died = events.SingleOf<HeroDied>();
        var spot = new GridPosition(5, 5);
        Assert.Equal(HeroStatus.Dead, fallen.Status);
        Assert.Equal([gem.Id], died.DroppedCardIds);
        Assert.Equal((spot, "Slain by a Grunt"), (died.Tombstone.Position, died.Tombstone.Epitaph));
        Assert.Contains(died.Tombstone.Inscription, new[] { "Here lies Hero 2", "RIP Hero 2" });
        Room room = session.RoomById();
        Assert.Equal(died.Tombstone, room.TombstoneAt(spot));
        Assert.Equal([gem], room.LootAt(spot));
        Assert.Equal(1, room.KeysAt(spot));

        // A tombstone doesn't block the way.
        Assert.Equal(new TurnStarted(standing.Id), events[^1]);
        session.Do(new RollMovement(standing.Id));
        Assert.Contains(spot, session.GetReachableTiles().Keys);
    }

    [Fact]
    public void AnAdjacentAllyRousesWithOdHandDiceEverySixOfTheTotalAMarkOfLifeAndEveryOneADoomDie()
    {
        Stats deft = GameRules.HeroBaseStats with { OdHandDice = 3 };
        (GameSession session, Hero standing, Hero fallen) = FallenTurn(new ScriptedDice(3, 5, 4, 1), deft);
        session.Do(new RollKeel(fallen.Id));
        Assert.Equal([fallen], session.GetRousableAllies());

        var roused = session.Do(new Rouse(standing.Id, fallen.Id)).SingleOf<HeroRoused>();

        Assert.Equal(new HeroRoused(fallen.Id, standing.Id, 1, 1, 1, 1), roused);
        Assert.Equal((1, 1), (fallen.LifeMarks, fallen.DoomDiceOwed));
        Assert.False(session.State.Turn!.HasActed);
        Assert.Contains("all your Od Hand dice", session.Refuse(new Rouse(standing.Id, fallen.Id)));
    }

    [Fact]
    public void RousingToThreeMarksStandsThemUpAtOnce()
    {
        Stats deft = GameRules.HeroBaseStats with { OdHandDice = 3 };
        (GameSession session, Hero standing, Hero fallen) = FallenTurn(new ScriptedDice(3, 5, 5, 4), deft);
        session.Do(new RollKeel(fallen.Id));
        fallen.LifeMarks = 1;

        session.Do(new Rouse(standing.Id, fallen.Id));

        Assert.Equal((HeroStatus.Active, 1), (fallen.Status, fallen.Hearts));
    }

    [Fact]
    public void OnlyAnAdjacentAllyCanRouse()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1....
            .........
            ......2..
            .........
            .........
            """, heroes: 2);
        Hero fallen = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;

        Assert.Contains("Kneel beside them", session.Refuse(new Rouse(session.HeroInSeat(0).Id, fallen.Id)));
        Assert.Contains("nobody Keeling Over", session.Refuse(new Rouse(session.HeroInSeat(0).Id, session.HeroInSeat(0).Id)));
        Assert.Empty(session.GetRousableAllies());
    }

    [Fact]
    public void MonstersGoForHeroesStillStanding()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1m2...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1), heroes: 2);
        Hero fallen = session.HeroInSeat(0);
        Hero standing = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;
        session.Do(new EndTurn(fallen.Id));

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(standing.Id));

        Assert.Equal(new Damaged(standing.Id, 1, standing.Hearts), events.SingleOf<Damaged>());
        Assert.Empty(events.OfType<KeelerStruck>());
    }

    [Fact]
    public void WithNobodyStandingInReachAMonsterHitIsAMarkOfDeath()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....m....
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1));
        Hero fallen = session.HeroInSeat();
        fallen.Status = HeroStatus.KeelingOver;
        fallen.DeathMarks = 1;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(fallen.Id));

        Monster grunt = session.OnlyMonster();
        Assert.Equal(new KeelerStruck(fallen.Id, grunt.Id, 2), events.SingleOf<KeelerStruck>());
        Assert.Equal("Slain by a Grunt", fallen.Bane);
        Assert.Equal(RunPhase.InProgress, session.State.Phase);
        Assert.Equal(new TurnStarted(fallen.Id), events[^1]);
    }

    [Fact]
    public void ARoomClearHealsOnlyThoseStanding()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....m....
            ....1....
            ....2....
            .........
            .........
            .........
            """, new ScriptedDice(5, 1), heroes: 2);
        Hero fallen = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;
        fallen.Hearts = 0;

        session.Do(new AttackMonster(session.HeroInSeat(0).Id, session.OnlyMonster().Id));

        Assert.Equal(0, fallen.Hearts);
    }
}
