using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class PowerDiceTests
{
    /// <summary>The hero at (4,4) with a monster of the given glyph directly north at (4,3).</summary>
    private static string Duel(char monster) => $"""
        .........
        .........
        .........
        ....{monster}....
        ....1....
        .........
        .........
        .........
        .........
        """;

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
    public void ASixOnAnAttackEarnsAPowerDieThatCanBeAddedToIt()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(6, 3, 4));
        Hero hero = session.HeroInSeat();
        Monster knight = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, knight.Id));
        Assert.Equal(new PowerDiceEarned(hero.Id, 1), events[^1]);
        Assert.NotNull(session.State.Turn!.PendingRoll);
        Assert.Contains("Power Die", session.Refuse(new EndTurn(hero.Id)));

        events = session.Do(new AddPowerDie(hero.Id));

        var added = events.SingleOf<PowerDieAdded>();
        Assert.Equal((3, 9), (added.Face, added.Total));
        Assert.Equal(6, events.SingleOf<DefenseRolled>().Total);
        Assert.Contains(new MonsterDefeated(knight.Id, hero.Id), events);
    }

    [Fact]
    public void APowerDieThatRollsASixEarnsAnother()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(6, 6, 2, 4));
        Hero hero = session.HeroInSeat();
        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Equal(new PowerDiceEarned(hero.Id, 1), session.Do(new AddPowerDie(hero.Id))[^1]);
        IReadOnlyList<GameEvent> events = session.Do(new AddPowerDie(hero.Id));

        Assert.Equal(14, events.SingleOf<PowerDieAdded>().Total);
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void BankedPowerDiceCanBeRolledIntoALaterAttack()
    {
        GameSession session = TestRun.Start(Duel('d'), new ScriptedDice(6, 1, 2), cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        session.Do(new AttackMonster(hero.Id, dummy.Id));

        IReadOnlyList<GameEvent> events = session.Do(new BankPowerDie(hero.Id));
        Assert.Equal(new PowerDiceBanked(hero.Id, 1, 1), events[0]);
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);

        session.EndRound();
        events = session.Do(new AttackMonster(hero.Id, dummy.Id, PowerDice: 1));

        Assert.Equal(new PowerDiceSpent(hero.Id, 1, 0), events[0]);
        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal([1, 2], attack.Roll.Faces);
        Assert.Equal((1, 4), (attack.PowerDice, attack.Total));
        Assert.Equal(0, hero.Power.Banked);
    }

    [Fact]
    public void YouCantSpendPowerDiceYouHaveNotBanked()
    {
        GameSession session = TestRun.Start(Duel('B'));
        Hero hero = session.HeroInSeat();

        Assert.Contains("0 banked Power Dice", session.Refuse(new AttackMonster(hero.Id, session.OnlyMonster().Id, PowerDice: 1)));
        Assert.Contains("0 banked Power Dice", session.Refuse(new RollMovement(hero.Id, PowerDice: 1)));
    }

    [Fact]
    public void AFullBankAddsTheNewPowerDieToTheRollAtOnce()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(6, 4, 2));
        Hero hero = session.HeroInSeat();
        hero.Power.Banked = GameRules.MaxBankedPowerDice;

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Equal(10, events.SingleOf<PowerDieAdded>().Total);
        Assert.DoesNotContain(events, gameEvent => gameEvent is PowerDiceEarned);
        Assert.Contains("no Power Die to bank", session.Refuse(new BankPowerDie(hero.Id)));
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
        Assert.Null(session.State.Turn!.PendingRoll);
    }

    [Fact]
    public void ASixOnAMoveWaitsToBeAddedOrBanked()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(6, 2));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        Assert.Contains("Power Die", session.Refuse(new MoveHero(hero.Id, new GridPosition(4, 3))));
        var added = session.Do(new AddPowerDie(hero.Id)).SingleOf<PowerDieAdded>();

        Assert.Equal(10, added.Total);
        Assert.Equal(10, session.State.Turn!.MovementPoints);
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 0)));
    }

    [Fact]
    public void ARerollThatComesUpSixEarnsAPowerDie()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(1, 6, 1), stats: GameRules.HeroBaseStats with { Luck = 1 });
        Hero hero = session.HeroInSeat();
        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        IReadOnlyList<GameEvent> events = session.Do(new RerollDie(hero.Id, 0));
        Assert.Equal(new PowerDiceEarned(hero.Id, 1), events[^1]);

        events = session.Do(new BankPowerDie(hero.Id));
        Assert.Equal(1, hero.Power.Banked);
        Assert.Equal(3, events.SingleOf<DefenseRolled>().Total);
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void SixesOnDefenseAreBanked()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(3, 6));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(new PowerDiceBanked(hero.Id, 1, 1), events);
        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void BankedDiceAreSpentToTurnAHitIntoABlock()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(5, 2, 4));
        Hero hero = session.HeroInSeat();
        hero.Power.Banked = 2;

        IReadOnlyList<GameEvent> events = session.EndRound();

        var defense = events.SingleOf<DefenseRolled>();
        Assert.Equal([2, 4], defense.Roll.Faces);
        Assert.Equal((1, 6), (defense.PowerDice, defense.Total));
        Assert.Contains(new PowerDiceSpent(hero.Id, 1, 1), events);
        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void BankedDiceAreKeptWhenTheDefenseHoldsAnyway()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(2, 5));
        Hero hero = session.HeroInSeat();
        hero.Power.Banked = 1;

        session.EndRound();

        Assert.Equal(1, hero.Power.Banked);
    }

    [Fact]
    public void MonstersRollTheirSixesStraightIntoTheAttack()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(6, 2, 5));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.EndRound();

        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal([6, 2], attack.Roll.Faces);
        Assert.Equal((1, 8), (attack.PowerDice, attack.Total));
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void MonstersBankTheSixesTheyRollOnDefenseAndSpendThemToBlock()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(4, 6, 5, 1, 5), stats: GameRules.HeroBaseStats with { AttackDice = 2 });
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();

        session.Do(new AttackMonster(hero.Id, brute.Id, Dice: 1));
        Assert.Equal(1, brute.Power.Banked);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, brute.Id, Dice: 1));

        Assert.Equal([1, 5], events.SingleOf<DefenseRolled>().Roll.Faces);
        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
        Assert.Equal(0, brute.Power.Banked);
    }
    [Fact]
    public void MonstersRollBankedPowerDiceIntoAnAttackThatLooksShort()
    {
        // The grunt rolls a 1: short of an unarmored hero's 4, so a banked die (3) goes in; 4 is still
        // short, so the other (2) too. The hero defends with a 1.
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(1, 3, 2, 1));
        Monster grunt = session.OnlyMonster();
        grunt.Power.Banked = 2;

        IReadOnlyList<GameEvent> events = session.EndRound();

        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal([1, 3, 2], attack.Roll.Faces);
        Assert.Equal((2, 6), (attack.PowerDice, attack.Total));
        Assert.Equal(new PowerDiceSpent(grunt.Id, 2, 0), events.SingleOf<PowerDiceSpent>());
    }

    [Fact]
    public void AMonsterThatCantReachAHeroRollsBankedPowerDiceIntoItsMove()
    {
        // The grunt (2 moves) is 3 steps from the tile beside the hero: a banked die (2) gets it there.
        GameSession session = TestRun.Start("""
            ....m....
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(2, 1, 5));
        Monster grunt = session.OnlyMonster();
        grunt.Power.Banked = 1;

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal([2], events.SingleOf<MonsterSurged>().Faces);
        Assert.Equal(new GridPosition(4, 3), grunt.Position);
        Assert.Single(events.OfType<AttackRolled>());
    }

    [Fact]
    public void BankedPowerDiceCarryIntoTheNextRunAndBackOut()
    {
        var setup = new RunSetup
        {
            Seed = 1,
            ThemeId = TestContent.ThemeId,
            Heroes = [new HeroSetup("Hero", TestRun.HeroStats, [], PowerDice: 2)],
            Rooms = [new RoomSetup("A", TestRun.Room(Open))],
            StartRoomId = "A",
        };
        var session = new GameSession(TestContent.Catalog, setup);
        session.Begin();

        Assert.Equal(2, session.HeroInSeat().Power.Banked);
    }
}
