using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;
using Xunit;

namespace Woad.Core.Tests.Rules;

public class SkillTests
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

    private const string Beside = """
        .........
        .........
        .........
        .........
        ....1d...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void WithoutTheSkillOdHandDiceCantBeRolledOnIt()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(4), skills: Skill.None);
        Hero hero = session.HeroInSeat();

        Assert.Contains("haven't learned to Search", session.CheckSearch());
        Assert.Contains("haven't learned to Search", session.Refuse(new Search(hero.Id)));
        Assert.Equal(0, session.OdHandDiceFor(Skill.Search));
    }

    [Fact]
    public void WithTheSkillOdHandDiceRollOnIt()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(4), skills: Skill.Search);
        Hero hero = session.HeroInSeat();

        Assert.Null(session.CheckSearch());
        Assert.Equal(4, session.Do(new Search(hero.Id)).SingleOf<OdHandRolled>().Total);
    }

    [Fact]
    public void ALitTorchSearchesForYouOnceATurnWithoutTheSkill()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3), cards: ["t_torch"], skills: Skill.None);
        Hero hero = session.HeroInSeat();

        OdHandRolled rolled = session.Do(new Search(hero.Id)).SingleOf<OdHandRolled>();

        Assert.Equal((1, 3), (rolled.ToolDice, rolled.Total));
        Assert.Equal(0, session.State.Turn!.OdHandDiceUsed);
        Assert.Contains("haven't learned to Search", session.Refuse(new Search(hero.Id)));
    }

    [Fact]
    public void ToolsStandInForAnUnlearnedSkillRollingOnlyTheirOwnDice()
    {
        // Burgling Tools steal for you: +1 and their d6 (4), no Od Hand dice.
        GameSession session = TestRun.Start(Beside, new ScriptedDice(4, 1), cards: ["t_burgle"], skills: Skill.None);
        Hero hero = session.HeroInSeat();

        Assert.Null(session.CheckSteal(session.OnlyMonster().Id));
        OdHandRolled rolled = session.Do(new Steal(hero.Id, session.OnlyMonster().Id)).SingleOf<OdHandRolled>();

        Assert.Equal((1, 1, 5), (rolled.ToolDice, rolled.Bonus, rolled.Total));
        Assert.Equal(0, session.State.Turn!.OdHandDiceUsed);
    }

    [Fact]
    public void WithTheSkillAToolAddsToTheOdHandDice()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(3, 4, 1), cards: ["t_burgle"], skills: Skill.Steal);
        Hero hero = session.HeroInSeat();

        OdHandRolled rolled = session.Do(new Steal(hero.Id, session.OnlyMonster().Id)).SingleOf<OdHandRolled>();

        Assert.Equal((1, 1, 8), (rolled.ToolDice, rolled.Bonus, rolled.Total));
    }

    [Fact]
    public void WithoutTheSkillChosenOdHandDiceStayOutAndTheToolRollsAlone()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(4, 1), cards: ["t_burgle"], skills: Skill.None);
        Hero hero = session.HeroInSeat();

        OdHandRolled rolled = session.Do(new Steal(hero.Id, session.OnlyMonster().Id, Dice: 1)).SingleOf<OdHandRolled>();

        Assert.Equal((1, 5), (rolled.ToolDice, rolled.Total));
        Assert.Equal(0, session.State.Turn!.OdHandDiceUsed);
    }

    [Fact]
    public void TheThievesToolsServeTheirSkills()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();

        Assert.Equal(Skill.PickLock | Skill.Disarm | Skill.Steal, content.GetCard("burgling_tools").Tool!.For);
        Assert.Equal(EquipSlot.OffHand, content.GetCard("burgling_tools").Slot);
        Assert.All(new[] { "cutpurse_knife", "sticky_gloves" }, id => Assert.Equal(new ToolUse { For = Skill.Steal, Bonus = 1, Dice = 1 }, content.GetCard(id).Tool));
        Assert.Equal((EquipSlot.OffHand, EquipSlot.MainHand, 1), (content.GetCard("cutpurse_knife").Slot, content.GetCard("cutpurse_knife").AltSlot, content.GetCard("cutpurse_knife").AttackBonus));
        Assert.Equal(EquipSlot.Accessory, content.GetCard("sticky_gloves").Slot);
    }

    [Fact]
    public void ShovingTakesTheSkill()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(), skills: Skill.None);

        Assert.Contains("haven't learned to Shove", session.CheckShove(session.OnlyMonster().Id));
    }

    private static GameSession Charge(string row, params int[] dice) => TestRun.Start($"""
        .........
        .........
        .........
        .........
        {row}
        .........
        .........
        .........
        .........
        """, new ScriptedDice(dice));

    private static readonly GridPosition East = new(8, 4);

    [Fact]
    public void ABullRushStrikesTheFirstMonsterWithTheTilesToSpareAndPushesItTwoTiles()
    {
        // A Wend 3 and the Wend bonus 2 make 5 tiles: 3 to reach the Dummy, 2 to spare on the blow (a 4).
        GameSession session = Charge("1...d....", 3, 4);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new BullRush(hero.Id, East));

        BullRushed rushed = events.SingleOf<BullRushed>();
        Assert.Equal((5, dummy.Id, 2), (rushed.Length, rushed.MonsterId, rushed.Leftover));
        Assert.Equal((4, 2, 6), (events.SingleOf<AttackRolled>().Roll.Total, events.SingleOf<AttackRolled>().Bonus, events.SingleOf<AttackRolled>().Total));
        Assert.Equal(new GridPosition(3, 4), hero.Position);
        Assert.Equal(new GridPosition(6, 4), dummy.Position);
        Assert.Equal(2, events.OfType<Shoved>().Count());
        Assert.False(hero.Prone);
        Assert.Equal((1, 1, false), (session.State.Turn!.MoveDiceUsed, session.State.Turn.AttackDiceUsed, session.State.Turn.HasActed));
    }

    [Fact]
    public void AMissedBullRushLeavesYouProne()
    {
        // 1+2 = 3 tiles: just enough to reach the Brute, with nothing to spare. Its 6 turns the 1 aside.
        GameSession session = Charge("1...B....", 1, 1, 6);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new BullRush(hero.Id, East));

        Assert.Equal(0, events.SingleOf<BullRushed>().Leftover);
        Assert.Equal(new GridPosition(4, 4), session.OnlyMonster().Position);
        Assert.True(hero.Prone);
    }

    [Fact]
    public void ChargingIntoAWallLeavesYouProneWithTheRumbleDiceUnspent()
    {
        GameSession session = Charge("1..#.....", 5);
        Hero hero = session.HeroInSeat();

        Assert.Contains(new GridPosition(2, 4), session.GetBullRushTargets());
        Assert.DoesNotContain(new GridPosition(3, 4), session.GetBullRushTargets());
        session.Do(new BullRush(hero.Id, new GridPosition(2, 4)));

        Assert.Equal(new GridPosition(2, 4), hero.Position);
        Assert.True(hero.Prone);
        Assert.Equal(0, session.State.Turn!.AttackDiceUsed);
    }

    [Fact]
    public void AShortChargeJustStops()
    {
        GameSession session = Charge("1.......d", 1);
        Hero hero = session.HeroInSeat();

        session.Do(new BullRush(hero.Id, East));

        Assert.Equal((new GridPosition(3, 4), false), (hero.Position, hero.Prone));
        Assert.Equal(0, session.State.Turn!.AttackDiceUsed);
    }

    [Fact]
    public void ABullRushDrivesBackEvenABoss()
    {
        GameSession session = Charge("1...W....", 3, 4);
        Hero hero = session.HeroInSeat();

        session.Do(new BullRush(hero.Id, East));

        Assert.Equal(new GridPosition(6, 4), session.OnlyMonster().Position);
    }

    [Fact]
    public void ABullRushNeedsTheSkillAndRoomToCharge()
    {
        GameSession session = Charge("1d.......");

        Assert.DoesNotContain(new GridPosition(1, 4), session.GetBullRushTargets());
        Assert.Contains("haven't learned to Bull Rush", TestRun.Start(Open, skills: Skill.None).CheckBullRush());
    }

    [Fact]
    public void ParryHoldsAnOdHandDieForYourNextDefense()
    {
        // Parry rolls a 3 and holds it; the Slugger's 4+4 meets the base die's 2 and the held 3.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1U...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3, 4, 2));
        Hero hero = session.HeroInSeat();

        DefenseHeld held = session.Do(new Parry(hero.Id)).SingleOf<DefenseHeld>();
        Assert.Equal((hero.Id, Skill.Parry, 3), (held.ForHeroId, held.Skill, held.Roll.Total));
        Assert.Equal(1, session.State.Turn!.OdHandDiceUsed);

        DefenseRolled defense = session.Do(new EndTurn(hero.Id)).SingleOf<DefenseRolled>();

        Assert.Equal([2, 3], defense.Roll.Faces);
        Assert.Equal(1, defense.Held);
        Assert.Empty(hero.HeldDefense);
    }

    [Fact]
    public void AHeldDieRunsOutAtTheStartOfYourNextTurn()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        session.Do(new Parry(hero.Id));

        session.EndRound();

        Assert.Empty(hero.HeldDefense);
    }

    [Fact]
    public void ParryNeedsTheSkillAndAnOdHandDie()
    {
        Assert.Contains("haven't learned to Parry", TestRun.Start(Open, skills: Skill.None).CheckParry());
        Assert.Contains("used all your Od Hand dice", TestRun.Start(Open, stats: TestRun.HeroStats with { OdHandDice = 0 }).CheckParry());
    }

    [Fact]
    public void GuardHoldsARumbleDieForAnAllyBesideYou()
    {
        // The first hero guards the second with a 4; the Slugger's 5+4 against their base 1 and the held 4.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...12U...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(4, 5, 1), heroes: 2);
        Hero guardian = session.HeroInSeat(0);
        Hero ally = session.HeroInSeat(1);

        Assert.Equal([guardian.Id, ally.Id], session.GetGuardTargets().Select(hero => hero.Id).Order());
        session.Do(new GuardHero(guardian.Id, ally.Id));
        Assert.Equal(1, session.State.Turn!.AttackDiceUsed);

        DefenseRolled defense = session.EndRound().SingleOf<DefenseRolled>();

        Assert.Equal((ally.Id, 5), (defense.DefenderId, defense.Total));
        Assert.Equal([1, 4], defense.Roll.Faces);
    }

    [Fact]
    public void GuardReachesOnlyAlliesBesideYou()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ..1..2...
            .........
            .........
            .........
            .........
            """, heroes: 2);

        Assert.Contains("Stand next to", session.CheckGuard(session.HeroInSeat(1).Id));
        Assert.Contains("haven't learned to Guard", TestRun.Start(Open, skills: Skill.None).CheckGuard(1));
    }

    [Fact]
    public void HighAlertRollsARumbleDieIntoTheNextSearch()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3, 2));
        Hero hero = session.HeroInSeat();

        session.Do(new HighAlert(hero.Id));
        Assert.Contains("already on High Alert", session.CheckHighAlert());
        OdHandRolled rolled = session.Do(new Search(hero.Id)).SingleOf<OdHandRolled>();

        Assert.Equal((5, 1), (rolled.Total, rolled.ToolDice));
        Assert.Equal((1, 1, 0), (session.State.Turn!.AttackDiceUsed, session.State.Turn.OdHandDiceUsed, session.State.Turn.AlertDice));
    }

    [Fact]
    public void HighAlertSearchesAloneWithoutTheSearchSkill()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(4), skills: Skill.HighAlert);
        Hero hero = session.HeroInSeat();
        Assert.Contains("haven't learned to Search", session.CheckSearch());

        session.Do(new HighAlert(hero.Id));
        OdHandRolled rolled = session.Do(new Search(hero.Id)).SingleOf<OdHandRolled>();

        Assert.Equal(4, rolled.Total);
        Assert.Equal(0, session.State.Turn!.OdHandDiceUsed);
    }
}
