using Woad.Core.Content;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;
using Xunit;

namespace Woad.Core.Tests.Rules;

public class AidTests
{
    private const string Pair = """
        .........
        .........
        .........
        .........
        ....12...
        .........
        .........
        .........
        .........
        """;

    private static string Across(char monster) => $"""
        .........
        .........
        .........
        .........
        ....1.{monster}..
        .........
        .........
        .........
        .........
        """;

    private static readonly Stats Deft = TestRun.HeroStats with { OdHandDice = 3 };

    [Fact]
    public void AidTakesOneStackOfTheAilmentChosenAtTen()
    {
        // 5+5+5, less 2 for the poison: 13 against 10. One of the 2 stacks of Poison comes off.
        GameSession session = TestRun.Start(Pair, new ScriptedDice(5, 5, 5), stats: Deft);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Poison, 2);
        hero.Conditions.Add(Condition.Blind, 1);

        Assert.Contains("Choose what to cure", session.Refuse(new Aid(hero.Id, hero.Id)));
        Assert.Equal([(Ailment.Poison, 10), (Ailment.Blind, 10)], session.GetAidCures(hero.Id));
        IReadOnlyList<GameEvent> events = session.Do(new Aid(hero.Id, hero.Id, Ailment.Poison));

        Assert.Equal(10, events.SingleOf<OdHandRolled>().Difficulty);
        Assert.Equal(1, hero.Conditions[Condition.Poison]);
        Assert.True(hero.Conditions.Has(Condition.Blind));
    }

    [Fact]
    public void AnAllyBesideYouCanBeAided()
    {
        GameSession session = TestRun.Start(Pair, new ScriptedDice(4, 4, 4), heroes: 2, stats: Deft);
        Hero ally = session.HeroInSeat(1);
        ally.Conditions.Add(Condition.Confused, 1);

        Assert.Contains(ally, session.GetAidTargets());
        session.Do(new Aid(session.HeroInSeat().Id, ally.Id));

        Assert.False(ally.Conditions.Has(Condition.Confused));
    }

    [Fact]
    public void OnlyAMedicineBagCuresFreeze()
    {
        GameSession session = TestRun.Start(Pair, new ScriptedDice(5, 5));
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Freeze, 1);

        Assert.Contains("Only a Medicine Bag", session.Refuse(new Aid(hero.Id, hero.Id)));
    }

    [Fact]
    public void AMedicineBagAddsOneAndADieAndCuresFreeze()
    {
        // The Od Hand die 5 and the bag's 5, +1: 11 against 10 + 1 stack.
        GameSession session = TestRun.Start(Pair, new ScriptedDice(5, 5), cards: ["t_bag"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Freeze, 1);

        OdHandRolled rolled = session.Do(new Aid(hero.Id, hero.Id)).SingleOf<OdHandRolled>();

        Assert.Equal((1, 1, 11), (rolled.ToolDice, rolled.Bonus, rolled.Total));
        Assert.False(hero.Conditions.Has(Condition.Freeze));
    }

    [Fact]
    public void AMedicineBagAidsWithNoOdHandDiceOnceATurnAndAFailedRollWithAOneUsesItUp()
    {
        Stats clumsy = TestRun.HeroStats with { OdHandDice = 0 };
        GameSession session = TestRun.Start(Pair, new ScriptedDice(1), stats: clumsy, cards: ["t_bag"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Blind, 1);

        Assert.Null(session.CheckAid(hero.Id));
        IReadOnlyList<GameEvent> events = session.Do(new Aid(hero.Id, hero.Id, Dice: 0));

        Assert.Equal(1, events.SingleOf<OdHandRolled>().ToolDice);
        Assert.Contains(events, gameEvent => gameEvent is CardConsumed);
        Assert.Null(hero.MedicineBag);
        Assert.True(hero.Conditions.Has(Condition.Blind));
    }

    [Fact]
    public void AMedicineBagGoesOnTheBeltAndWorksFromThere()
    {
        GameSession session = TestRun.Start(Pair, new ScriptedDice(5, 5), cards: ["t_bag"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Blind, 1);

        Assert.Equal(CardLocation.Belt, hero.FindCard(hero.Card("t_bag").Id)!.Value.Location);
        Assert.Equal(1, session.Do(new Aid(hero.Id, hero.Id)).SingleOf<OdHandRolled>().ToolDice);
    }

    [Fact]
    public void BandagesAddADieToARouseAndOneIsUsedUp()
    {
        // The Od Hand die 2 and the bandage's 4: 6, a mark of life.
        GameSession session = TestRun.Start(Pair, new ScriptedDice(2, 4), heroes: 2, cards: ["t_bandage"]);
        Hero fallen = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new Aid(hero.Id, fallen.Id));

        Assert.Equal(1, events.SingleOf<HeroRoused>().LifeGained);
        Assert.Null(hero.Bandages);
    }

    [Fact]
    public void AShovelInHandDigsAStackOffAtTheStartOfYourTurn()
    {
        GameSession session = TestRun.Start(Pair, new ScriptedDice(), cards: ["t_shovel"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Buried, 2);

        session.EndRound();

        Assert.Equal(1, hero.Conditions[Condition.Buried]);
    }

    [Fact]
    public void HolyWaterDestroysLesserUndeadOutrightBonesAndAll()
    {
        GameSession session = TestRun.Start(Across('k'), new ScriptedDice(), cards: ["t_holy"]);
        Hero hero = session.HeroInSeat();
        Monster bones = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("t_holy").Id, bones.Position));

        Assert.True(events.SingleOf<Sanctified>().Destroyed);
        Assert.Contains(events, gameEvent => gameEvent is MonsterDefeated);
        Assert.DoesNotContain(events, gameEvent => gameEvent is MonsterCollapsed);
        Assert.Empty(session.RoomById().Monsters);
    }

    [Fact]
    public void HolyWaterHitsGreaterUndeadWithFiveRadiateDice()
    {
        GameSession session = TestRun.Start(Across('c'), new ScriptedDice(2, 2, 2, 2, 2), cards: ["t_holy"]);
        Hero hero = session.HeroInSeat();
        Monster ghoul = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("t_holy").Id, ghoul.Position));

        Assert.False(events.SingleOf<Sanctified>().Destroyed);
        Assert.Equal(5, events.SingleOf<AttackRolled>().Roll.Faces.Count);
    }

    [Fact]
    public void HolyWaterIsOnlyThrownAtTheUndead()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(), cards: ["t_holy"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("the undead", session.Refuse(new UseCard(hero.Id, hero.Card("t_holy").Id, session.OnlyMonster().Position)));
    }

    [Fact]
    public void AGhoulsMiasmaAttacksAHeroStartingTheirTurnBesideIt()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1c...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1, 1, 1, 1, 1, 1));

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(events, gameEvent => gameEvent is AttackRolled { Miasma: true });
    }

    [Fact]
    public void TheSineaterDollSoaksUpAnAilmentAndBurstsItOnEveryoneNear()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(), cards: ["t_doll"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Poison, 3);
        CardInstance doll = hero.Card("t_doll");

        Assert.Contains("holds nothing", session.Refuse(new UseCard(hero.Id, doll.Id, session.OnlyMonster().Position)));
        SoakedUp soaked = session.Do(new SoakUp(hero.Id, doll.Id, hero.Id, Ailment.Poison)).SingleOf<SoakedUp>();
        Assert.Equal((3, 3), (soaked.Stacks, soaked.Held));
        Assert.False(hero.Conditions.Has(Condition.Poison));

        session.EndRound();
        Monster dummy = session.OnlyMonster();
        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, doll.Id, dummy.Position));

        Assert.Contains(dummy.Id, events.SingleOf<DollBurst>().Caught);
        Assert.Equal(3, dummy.Conditions[Condition.Poison]);
        Assert.DoesNotContain(hero.AllCards, card => card.Id == doll.Id);
    }

    [Fact]
    public void TheSineaterDollHoldsTenStacksAtMost()
    {
        GameSession session = TestRun.Start(Pair, new ScriptedDice(), cards: ["t_doll"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Shine, 12);
        CardInstance doll = hero.Card("t_doll");

        session.Do(new SoakUp(hero.Id, doll.Id, hero.Id, Ailment.Shine));

        Assert.Equal(GameRules.DollCapacity, doll.SoakedStacks);
        Assert.Equal(2, hero.Conditions[Condition.Shine]);
    }
}
