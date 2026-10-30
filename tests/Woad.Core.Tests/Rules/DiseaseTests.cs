using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>
/// Nekrot: a 6 on a carrier's hit tries to give it; a d6 resists (a 6, or 3 or more with Thieves' Oil, a
/// second roll with a Medicine Bag in hand). With it every lost heart turns black, and both carry on
/// from run to run until cured.
/// </summary>
public class DiseaseTests
{
    // A Rotter (1d6+4, carries Nekrot, no defense) beside the hero.
    private const string Beside = """
        .........
        .........
        .........
        .........
        ....1I...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void ASixOnTheHitTriesToInfectAndAFailedResistCatchesIt()
    {
        // The Rotter's 6 (and the Power Die it rolls in, a 1) +4 against the hero's 1, then a 5 to resist: not a 6.
        GameSession session = TestRun.Start(Beside, new ScriptedDice(6, 1, 1, 5));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        InfectionRolled rolled = events.SingleOf<InfectionRolled>();
        Assert.Equal((Condition.Nekrot, 6, false), (rolled.Disease, rolled.Needed, rolled.Resisted));
        Assert.Equal([5], rolled.Faces);
        Assert.True(hero.Conditions.Has(Condition.Nekrot));
    }

    [Fact]
    public void ASixResistsIt()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(6, 1, 1, 6));
        Hero hero = session.HeroInSeat();

        Assert.True(session.Do(new EndTurn(hero.Id)).SingleOf<InfectionRolled>().Resisted);
        Assert.False(hero.Conditions.Has(Condition.Nekrot));
    }

    [Fact]
    public void WithoutASixOnTheHitThereIsNoTry()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();

        Assert.Empty(session.Do(new EndTurn(hero.Id)).OfType<InfectionRolled>());
    }

    [Fact]
    public void ThievesOilResistsOnAThreeForTenRounds()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(6, 1, 1, 3), cards: ["t_oil"]);
        Hero hero = session.HeroInSeat();
        session.Do(new UseCard(hero.Id, hero.Card("t_oil").Id));
        Assert.Equal(10, hero.ThievesOilRounds);

        InfectionRolled rolled = session.Do(new EndTurn(hero.Id)).SingleOf<InfectionRolled>();

        Assert.Equal((3, true), (rolled.Needed, rolled.Resisted));
        Assert.Equal(9, hero.ThievesOilRounds);
    }

    [Fact]
    public void AMedicineBagInHandGivesASecondRollToResist()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(6, 1, 1, 5, 6), cards: ["t_handbag"]);
        Hero hero = session.HeroInSeat();

        InfectionRolled rolled = session.Do(new EndTurn(hero.Id)).SingleOf<InfectionRolled>();

        Assert.Equal([5, 6], rolled.Faces);
        Assert.True(rolled.Resisted);
    }

    [Fact]
    public void WithNekrotEveryLostHeartTurnsBlack()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Nekrot, 1);

        session.Do(new EndTurn(hero.Id));

        Assert.Equal((hero.MaxHearts - 1, 1), (hero.Hearts, hero.Conditions[Condition.BlackHeart]));
    }

    [Fact]
    public void ModernMedicineCuresIt()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(), cards: ["t_medicine"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Nekrot, 1);

        session.Do(new UseCard(hero.Id, hero.Card("t_medicine").Id));

        Assert.False(hero.Conditions.Has(Condition.Nekrot));
    }

    [Fact]
    public void HolyWaterDrunkCuresItAndEveryBlackHeart()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(), cards: ["t_holy"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Nekrot, 1);
        hero.Conditions.Add(Condition.BlackHeart, 2);

        session.Do(new UseCard(hero.Id, hero.Card("t_holy").Id, hero.Position));

        Assert.False(hero.Conditions.Has(Condition.Nekrot));
        Assert.False(hero.Conditions.Has(Condition.BlackHeart));
    }

    [Fact]
    public void AidWithAMedicineBagTakesItAtFourteen()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(), cards: ["t_handbag"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Nekrot, 1);

        Assert.Contains((Ailment.Nekrot, GameRules.NekrotAidDifficulty), session.GetAidCures(hero.Id));
    }

    [Fact]
    public void BlackHeartsAndNekrotCarryOnToTheNextRun()
    {
        var setup = new RunSetup
        {
            Seed = TestRun.Seed,
            ThemeId = TestContent.ThemeId,
            Heroes = [new HeroSetup("Wren", TestRun.HeroStats, [], Lingering: new Dictionary<Condition, int> { [Condition.Nekrot] = 1, [Condition.BlackHeart] = 2 })],
            Rooms = [new RoomSetup("A", TestRun.Room(Beside.Replace('I', '.')))],
            StartRoomId = "A",
        };
        var session = new GameSession(TestContent.Catalog, setup);
        Hero hero = session.HeroInSeat();

        Assert.Equal((1, 2), (hero.Conditions[Condition.Nekrot], hero.Conditions[Condition.BlackHeart]));
        Assert.Equal(hero.MaxHearts - 2, hero.Hearts);
    }

    [Fact]
    public void LosingAHeartPuffsGraveDustThatCanInfectThoseBesideIt()
    {
        // The hero's 5 takes a heart off the Rotter; the dust's 3 gets in, and a 4 doesn't resist.
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5, 3, 4));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.False(events.SingleOf<GraveDustBurst>().Destroyed);
        Assert.True(hero.Conditions.Has(Condition.Nekrot));
    }

    [Fact]
    public void GraveDustOverThreeIsBreathedOutAgain()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5, 4));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Empty(events.OfType<InfectionRolled>());
    }

    [Fact]
    public void DestroyedItBurstsAtEveryoneWithinTwoButTheUndead()
    {
        // The hero's 5 destroys it; the burst's Poison 1 and Necro 1 (2) hit the Dummy (no defense) but
        // not the hero (a 6), and the Bones are spared. The dust's 4 is breathed out again.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1Id..
            .....k...
            .........
            .........
            .........
            """, new ScriptedDice(5, 1, 1, 6, 4));
        Hero hero = session.HeroInSeat();
        Monster rotter = session.RoomById().Monsters.Single(monster => monster.Definition.Id == "rotter");
        Monster dummy = session.RoomById().Monsters.Single(monster => monster.Definition.Id == "dummy");
        Monster bones = session.RoomById().Monsters.Single(monster => monster.Definition.Id == "bones");
        rotter.Hearts = 1;

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, rotter.Id));

        Assert.True(events.SingleOf<GraveDustBurst>().Destroyed);
        AttackRolled burst = events.OfType<AttackRolled>().Single(attack => attack.AttackerId == rotter.Id);
        Assert.Equal([DieType.Poison, DieType.Necro], burst.DieTypes);
        Assert.Equal([hero.Id, dummy.Id], burst.TargetIds);
        Assert.Equal(dummy.MaxHearts - 1, dummy.Hearts);
        Assert.Equal(bones.MaxHearts, bones.Hearts);
        Assert.Equal(hero.MaxHearts, hero.Hearts);
    }

    [Fact]
    public void FireKeepsTheGraveDustDown()
    {
        // A held torch's fire die goes into the blow: 5 and 1, and no dust.
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5, 1), cards: ["t_torch"]);
        Hero hero = session.HeroInSeat();
        Assert.True(hero.TorchesHeld > 0);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Single(events.OfType<Damaged>(), damaged => damaged.TargetId == session.OnlyMonster().Id && damaged.Amount == 1);
        Assert.Empty(events.OfType<GraveDustBurst>());
    }

    [Fact]
    public void OutOfReachItCastsSwirlyEyesAndDragsTheHeroCloser()
    {
        // Three tiles off, it can't strike: its Psycho 4 against the hero's 1 hits, and drags them a tile nearer.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ..1..I...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(4, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal("swirly_eyes", events.SingleOf<MonsterCast>().SpellId);
        Assert.Equal([DieType.Psycho], events.SingleOf<AttackRolled>().DieTypes);
        Assert.Equal(new GridPosition(3, 4), hero.Position);
    }
}
