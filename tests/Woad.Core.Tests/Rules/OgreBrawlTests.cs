using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>An ogre's off-hand fists (a Rock or Boulder swung in them) and its Bull Rush.</summary>
public sealed class OgreBrawlTests
{
    private static GameSession Yard(string row, ScriptedDice dice, int armor = 20) =>
        TestRun.Start($"""
            .........
            .........
            .........
            .........
            {row}
            .........
            .........
            .........
            .........
            """, dice, stats: TestRun.HeroStats with { DefenseDice = 0, Armor = armor });

    [Fact]
    public void BesideAHeroItPunchesWithItsOffHandToo_EverySixKnockingThemBack()
    {
        GameSession session = Yard("....1e...", new ScriptedDice(2, 6, 2, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster brawler = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new MonsterPunched(brawler.Id, hero.Id, null), events.SingleOf<MonsterPunched>());
        Assert.Equal(2, events.OfType<AttackRolled>().Count(attack => attack.AttackerId == brawler.Id));
        Assert.Equal(new GridPosition(3, 4), hero.Position);
    }

    [Fact]
    public void ARockInItsFistAddsToThePunchAndIsSpent()
    {
        GameSession session = Yard("....1e...", new ScriptedDice(2, 2, 2, 1, 1, 1));
        Monster brawler = session.OnlyMonster();
        brawler.Holding = new CardInstance(session.State.NextId(), session.Content.GetCard("rock"), broughtInByHeroId: null, 2);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal("Rock", events.SingleOf<MonsterPunched>().With);
        Assert.Equal(1, events.OfType<AttackRolled>().Last(attack => attack.AttackerId == brawler.Id).Bonus);
        Assert.Equal(1, brawler.Holding!.Count);
    }

    [Fact]
    public void ItCharges_ReachingAHeroInLineAndDrivingThemBackOnAHit()
    {
        GameSession session = Yard("..1...!..", new ScriptedDice(5, 4, 1, 1, 1), armor: 0);
        Hero hero = session.HeroInSeat();
        Monster charger = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.EndRound();

        MonsterCharged charged = events.SingleOf<MonsterCharged>();
        Assert.Equal((5, hero.Id), (charged.Length, charged.TargetId));
        Assert.Equal(new GridPosition(3, 4), charger.Position);
        Assert.Equal(2, events.OfType<AttackRolled>().Single(attack => attack.AttackerId == charger.Id).Bonus);
        Assert.Equal(new GridPosition(0, 4), hero.Position);
    }

    [Fact]
    public void MissingAtTheEndOfACharge_ItSprawlsProne()
    {
        GameSession session = Yard("..1...!..", new ScriptedDice(5, 1, 1, 1, 1));
        Monster charger = session.OnlyMonster();

        session.EndRound();

        Assert.True(charger.Prone);
    }

    [Fact]
    public void FallingShort_ItJustEndsUpNearer()
    {
        GameSession session = Yard("..1...!..", new ScriptedDice(1, 1, 1, 1));
        Monster charger = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Null(events.SingleOf<MonsterCharged>().TargetId);
        Assert.Equal(new GridPosition(5, 4), charger.Position);
        Assert.DoesNotContain(events, gameEvent => gameEvent is AttackRolled);
    }
}
