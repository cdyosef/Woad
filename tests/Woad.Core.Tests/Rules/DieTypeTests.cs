using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Typed dice: each 6 rolls another of its type in and, on a hit, lays its effect on the target.</summary>
public class DieTypeTests
{
    /// <summary>The hero at (4,4) and a monster of the given glyph two tiles east, at (6,4).</summary>
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

    /// <summary>The hero at (4,4) with a monster of the given glyph directly north, at (4,3).</summary>
    private static string Beside(char monster) => $"""
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

    private static IReadOnlyList<GameEvent> Cast(GameSession session, string card, GridPosition at)
    {
        Hero hero = session.HeroInSeat();
        return session.Do(new UseCard(hero.Id, hero.Card(card).Id, at));
    }

    [Fact]
    public void ABoomerangsSixesStunOnlyOnAHit()
    {
        // 6 and its Power Die (1), +1: 8 against the Knight's 6 and armor 2. A tie holds.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1..K.
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1, 6), cards: ["boomerang"]);
        Hero hero = session.HeroInSeat();

        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));
        IReadOnlyList<GameEvent> events = session.Do(new AddPowerDie(hero.Id));

        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
        Assert.Empty(events.OfType<Stunned>());
    }

    [Fact]
    public void AStarDiesSixSurgesWithNoChoiceToMake()
    {
        // The Star die's 6 rolls a Power Die straight in (2) and banks another.
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 2, 1), cards: ["bolt_star"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_star", session.OnlyMonster().Position);

        Assert.Equal([DieType.Star], events.SingleOf<AttackRolled>().DieTypes);
        Assert.Equal(8, events.SingleOf<PowerDieAdded>().Total);
        Assert.Equal(new PowerDiceBanked(hero.Id, 1, 1), events.SingleOf<PowerDiceBanked>());
        Assert.Empty(events.OfType<PowerDiceEarned>());
        Assert.Null(session.State.Turn!.PendingRoll);
    }

    [Fact]
    public void AStarDiesSixWithTheBankFullRollsBothIn()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 2, 3, 1), cards: ["bolt_star"]);
        Hero hero = session.HeroInSeat();
        hero.Power.Banked = GameRules.MaxBankedPowerDice;

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_star", session.OnlyMonster().Position);

        Assert.Equal([2, 3], events.OfType<PowerDieAdded>().Select(added => added.Face));
        Assert.Empty(events.OfType<PowerDiceBanked>());
    }

    [Fact]
    public void ElektrikSixesStun()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 2), cards: ["bolt_elektrik"]);
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_elektrik", dummy.Position);

        Assert.Equal([DieType.Elektrik, DieType.Elektrik], events.SingleOf<AttackRolled>().DieTypes);
        Assert.Equal(new Stunned(dummy.Id, 1, 0, false), events.SingleOf<Stunned>());
    }

    [Fact]
    public void ASquallBlowsTheTargetAwayAndSlamsItIntoTheWall()
    {
        // Two 6s: a tile to the room's edge, then the wall stops it: a die (5) for that tile.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1..d.
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 6, 1, 5), cards: ["bolt_squall"]);
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_squall", dummy.Position);

        Assert.Equal(new GridPosition(8, 4), dummy.Position);
        Assert.Null(events.SingleOf<Slammed>().IntoId);
        Assert.Equal(1, dummy.Hearts);
    }

    [Fact]
    public void ASquallSlamsTheTargetIntoWhoeverStandsBehindItAndHurtsThemBoth()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1.dd.
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1, 4), cards: ["bolt_squall"]);
        Room room = session.RoomById();
        Monster front = room.MonsterAt(new GridPosition(6, 4))!;
        Monster back = room.MonsterAt(new GridPosition(7, 4))!;

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_squall", front.Position);

        Assert.Equal(back.Id, events.SingleOf<Slammed>().IntoId);
        Assert.Equal((1, 2), (front.Hearts, back.Hearts));
        Assert.Equal(new GridPosition(6, 4), front.Position);
    }

    [Fact]
    public void EachSquallSixBlowsTheTargetATile()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 6, 1), cards: ["bolt_squall"]);
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_squall", dummy.Position);

        Assert.Equal(new GridPosition(8, 4), dummy.Position);
        Assert.Empty(events.OfType<Slammed>());
    }

    [Fact]
    public void ABossTakesTwoSquallSixesToShiftATile()
    {
        GameSession session = TestRun.Start(Across('W'), new ScriptedDice(6, 6, 1), cards: ["bolt_squall"]);
        Monster boss = session.OnlyMonster();

        Cast(session, "bolt_squall", boss.Position);

        Assert.Equal(new GridPosition(7, 4), boss.Position);
    }

    [Fact]
    public void AHugeMonsterShiftsWholeATileForTwoSquallSixes()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ......O..
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 6, 1), cards: ["bolt_squall"]);
        Monster hulk = session.OnlyMonster();

        Cast(session, "bolt_squall", new GridPosition(6, 4));

        Assert.Equal(new GridPosition(7, 3), hulk.Position);
    }

    [Fact]
    public void TheFrostWandFreezesHitOrMiss()
    {
        // 1 and 1 against the Knight's 6 and armor 2: a miss, but it chills all the same.
        GameSession session = TestRun.Start(Across('K'), new ScriptedDice(1, 1, 6, 6), cards: ["t_frost"]);
        Hero hero = session.HeroInSeat();
        Monster knight = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("t_frost").Id, knight.Position));

        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
        Assert.Equal(1, knight.Conditions[Condition.Freeze]);
    }

    [Fact]
    public void TheFrostWandFreezesAStackThenItsIceSixesFreezeMoreOnAHit()
    {
        // The stack it always lays, then two Ice 6s that hit: a stack more for each.
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 6, 1, 1, 1, 1), cards: ["t_frost"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        var events = new List<GameEvent>(session.Do(new UseCard(hero.Id, hero.Card("t_frost").Id, dummy.Position)));
        while (session.State.Turn?.UnsettledPowerDice > 0)
        {
            events.AddRange(session.Do(new AddPowerDie(hero.Id)));
        }

        Assert.Equal([1, 3], events.OfType<ConditionChanged>().Where(change => change.Condition == Condition.Freeze).Select(change => change.Count));
    }

    [Fact]
    public void EarthKnocksTheTargetProneAndBuriesItAndItDigsOutWithItsTurn()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 6, 1, 3), cards: ["bolt_earth"]);
        Monster dummy = session.OnlyMonster();

        Cast(session, "bolt_earth", dummy.Position);
        Assert.True(dummy.Prone);
        Assert.Equal(1, dummy.Conditions[Condition.Buried]);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(0, events.SingleOf<DugOut>().BuriedLeft);
        Assert.False(dummy.Conditions.Has(Condition.Buried));
    }

    [Fact]
    public void ABuriedHeroCantMoveOrFightButDigsOutWithRumble()
    {
        GameSession session = TestRun.Start(Beside('d'), new ScriptedDice(4, 3), stats: TestRun.HeroStats with { AttackDice = 2 });
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Buried, 2);

        Assert.Contains("buried", session.Refuse(new RollMovement(hero.Id)));
        Assert.Contains("buried", session.Refuse(new AttackMonster(hero.Id, session.OnlyMonster().Id)));

        IReadOnlyList<GameEvent> events = session.Do(new DigOut(hero.Id));

        Assert.Equal(0, events.SingleOf<DugOut>().BuriedLeft);
        Assert.False(hero.Conditions.Has(Condition.Buried));
    }

    [Fact]
    public void PoisonIsRolledAgainstAtTheEndOfTheTurn()
    {
        // The bolt's 6 poisons the dummy; at the end of its turn it rolls a 1, not past its 1 token: a heart lost.
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 1, 1), cards: ["bolt_poison"]);
        Monster dummy = session.OnlyMonster();

        Cast(session, "bolt_poison", dummy.Position);
        Assert.Equal(1, dummy.Conditions[Condition.Poison]);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new PoisonRolled(dummy.Id, 1, true, 1), events.SingleOf<PoisonRolled>());
        Assert.Equal(1, dummy.Hearts);
    }

    [Fact]
    public void ASixThrowsOffAPoisonToken()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6));
        Monster dummy = session.OnlyMonster();
        dummy.Conditions.Add(Condition.Poison, 1);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new PoisonRolled(dummy.Id, 6, false, 0), events.SingleOf<PoisonRolled>());
        Assert.Equal(3, dummy.Hearts);
    }

    [Fact]
    public void PoisonTakesAPointOffEveryRollPerToken()
    {
        GameSession session = TestRun.Start(Beside('d'), new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Poison, 2);

        var attack = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id)).SingleOf<AttackRolled>();

        Assert.Equal((-2, 3), (attack.Bonus, attack.Total));
    }

    [Fact]
    public void ABossIsNeverFrozenSolid()
    {
        GameSession session = TestRun.Start("""
            ....W....
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """);
        Monster boss = session.OnlyMonster();
        boss.Conditions.Add(Condition.Freeze, 5);

        Assert.False(GameSession.IsFrozenSolid(boss));
        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is FrozenSolid);
    }

    [Fact]
    public void AMonsterWithFreezeTakingAllItsWendIsFrozenSolidAndMeltsAStackATurn()
    {
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
            """);
        Monster grunt = session.OnlyMonster();
        grunt.Conditions.Add(Condition.Freeze, 2);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(new FrozenSolid(grunt.Id), events);
        Assert.Empty(events.OfType<MonsterMoved>());
        Assert.Equal(1, grunt.Conditions[Condition.Freeze]);
    }

    [Fact]
    public void AFrozenHeroLosesTheirTurn()
    {
        GameSession session = TestRun.Start(Across('d'));
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Freeze, 3);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(new FrozenSolid(hero.Id), events);
        Assert.Equal(1, hero.Conditions[Condition.Freeze]);
        Assert.Equal(3, session.State.Round);
    }

    [Fact]
    public void IceSixesFreezeAndFireDiceMeltIt()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 6, 2, 1, 1, 1), cards: ["bolt_ice", "firebolt"]);
        Monster dummy = session.OnlyMonster();

        Cast(session, "bolt_ice", dummy.Position);
        Assert.Equal(2, dummy.Conditions[Condition.Freeze]);
        session.Do(new EndTurn(session.HeroInSeat().Id));
        Assert.Equal(1, dummy.Conditions[Condition.Freeze]);

        IReadOnlyList<GameEvent> events = Cast(session, "firebolt", dummy.Position);

        Assert.Contains(new ConditionChanged(dummy.Id, Condition.Freeze, 0, 0), events);
    }

    [Fact]
    public void AcidEatsArmorAwayAndBurnsWhatHasNone()
    {
        GameSession session = TestRun.Start(Across('Q'), new ScriptedDice(6, 1), cards: ["bolt_acid"]);
        Monster crab = session.OnlyMonster();

        Cast(session, "bolt_acid", crab.Position);

        Assert.Equal(1, crab.Conditions[Condition.Corroded]);
        Assert.False(crab.Conditions.Has(Condition.Acid));
    }

    [Fact]
    public void AnAcidTokenBurnsAtTheEndOfTheTurnAndIsGone()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 1, 4), cards: ["bolt_acid"]);
        Monster dummy = session.OnlyMonster();

        Cast(session, "bolt_acid", dummy.Position);
        Assert.Equal(1, dummy.Conditions[Condition.Acid]);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal([4], events.SingleOf<AcidBurned>().Roll.Faces);
        Assert.Equal(1, dummy.Hearts);
        Assert.False(dummy.Conditions.Has(Condition.Acid));
    }

    [Fact]
    public void ShineMakesItEasierToHitAndFades()
    {
        // 4 against the Knight's 2 and armor 2 would tie; its shine makes it 5.
        GameSession session = TestRun.Start(Beside('K'), new ScriptedDice(4, 2));
        Hero hero = session.HeroInSeat();
        Monster knight = session.OnlyMonster();
        knight.Conditions.Add(Condition.Shine, 1);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, knight.Id));

        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void RadiateSixesMakeItShineForATurn()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 1), cards: ["bolt_radiate"]);
        Monster dummy = session.OnlyMonster();

        Cast(session, "bolt_radiate", dummy.Position);
        Assert.Equal(1, dummy.Conditions[Condition.Shine]);
        session.EndRound();

        Assert.False(dummy.Conditions.Has(Condition.Shine));
    }

    [Fact]
    public void ABlindHeroOnlyReachesWhatsBesideThem()
    {
        GameSession session = TestRun.Start(Across('d'), cards: ["bow"]);
        Hero hero = session.HeroInSeat();
        Assert.NotEmpty(session.GetAttackableMonsters());

        hero.Conditions.Add(Condition.Blind, 1);

        Assert.Empty(session.GetAttackableMonsters());
    }

    [Fact]
    public void TendingBlindEyesClearsThem()
    {
        // Od Hand 6 and its Power Die (5): 11 against 10 + 1 token.
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 5));
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Blind, 1);

        session.Do(new Tend(hero.Id, hero.Id));
        session.Do(new AddPowerDie(hero.Id));

        Assert.False(hero.Conditions.Has(Condition.Blind));
    }

    [Fact]
    public void EyeDropsCureBlindness()
    {
        GameSession session = TestRun.Start(Across('d'), cards: ["drops"]);
        Hero hero = session.HeroInSeat();
        Assert.Contains("nothing for it to cure", session.Refuse(new UseCard(hero.Id, hero.Card("drops").Id, null)));
        hero.Conditions.Add(Condition.Blind, 2);

        session.Do(new UseCard(hero.Id, hero.Card("drops").Id, null));

        Assert.False(hero.Conditions.Has(Condition.Blind));
    }

    [Fact]
    public void ABlindMonsterCantFindAHeroPastTheTilesBesideIt()
    {
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
            """);
        session.OnlyMonster().Conditions.Add(Condition.Blind, 1);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Empty(events.OfType<AttackRolled>());
    }

    [Fact]
    public void NecroSixesBlackenALostHeartWhichWontHeal()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 1), cards: ["bolt_necro"]);
        Monster dummy = session.OnlyMonster();

        Cast(session, "bolt_necro", dummy.Position);

        Assert.Equal(2, dummy.Hearts);
        Assert.Equal(1, dummy.Conditions[Condition.BlackHeart]);
    }

    [Fact]
    public void AMonsterKilledByNecroRisesAsTheHerosZombie()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 1), cards: ["bolt_necro"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        dummy.Hearts = 1;

        IReadOnlyList<GameEvent> events = Cast(session, "bolt_necro", dummy.Position);

        Assert.Contains(events, gameEvent => gameEvent is MonsterDefeated);
        var risen = events.SingleOf<HeroSummoned>();
        Hero zombie = session.State.GetHero(risen.HeroId);
        Assert.Equal(("dummy", hero.Id, true), (zombie.Guise, zombie.OwnerId!.Value, zombie.Summoned));
        Assert.Equal(CompanionOrder.Fight, zombie.Order);
    }

    [Fact]
    public void AHeroBroughtDownByNecroDiesAndRisesForTheMonsters()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....G....
            ....1....
            .........
            .........
            .........
            2........
            """, new ScriptedDice(6, 1, 1), heroes: 2);
        Hero hero = session.HeroInSeat();
        hero.Hearts = 1;

        session.EndRound();

        Assert.Equal(HeroStatus.Dead, hero.Status);
        Assert.Contains(session.RoomById().Monsters, monster => monster.Definition.Id == "zombie");
    }

    [Fact]
    public void HolyWaterLetsBlackHeartsHealAgain()
    {
        GameSession session = TestRun.Start(Across('d'), cards: ["holy", "potion"]);
        Hero hero = session.HeroInSeat();
        hero.Hearts = 3;
        hero.Conditions.Add(Condition.BlackHeart, 1);
        Assert.Contains("Black Hearts", session.Refuse(new UseCard(hero.Id, hero.Card("potion").Id, null)));

        session.Do(new UseCard(hero.Id, hero.Card("holy").Id, null));

        Assert.False(hero.Conditions.Has(Condition.BlackHeart));
        Assert.Equal(1, hero.HeartsToHeal);
    }

    [Fact]
    public void LifeGrowsPlantsThenASentientShrubForTheAttackersSide()
    {
        GameSession session = TestRun.Start(Across('d'), new ScriptedDice(6, 1, 6, 1), cards: ["bolt_life", "bolt_life"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        Assert.Equal(new PlantsGrew("A", dummy.Position, true), Cast(session, "bolt_life", dummy.Position).SingleOf<PlantsGrew>());
        session.EndRound();
        var shrub = Cast(session, "bolt_life", dummy.Position).SingleOf<HeroSummoned>();

        Hero summoned = session.State.GetHero(shrub.HeroId);
        Assert.Equal(("sentient_shrub", hero.Id, 1), (summoned.Guise, summoned.OwnerId!.Value, summoned.FixedWendFace!.Value));
        Assert.Equal(1, summoned.Position.ChebyshevDistanceTo(dummy.Position));
    }

    [Fact]
    public void PlantsShelterTheirSide()
    {
        // The grunt's 4 against the hero's 3 would hit; the plants make it a tie.
        GameSession session = TestRun.Start(Beside('m'), new ScriptedDice(4, 3));
        Hero hero = session.HeroInSeat();
        session.RoomById().Grow(hero.Position, heroes: true);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void AConfusedHeroLashesOutAtWhoeverIsBesideThemAndStaggersOff()
    {
        GameSession session = TestRun.Start(Beside('d'), new ScriptedDice(5, 3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        hero.Conditions.Add(Condition.Confused, 2);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(new Staggered(hero.Id), events);
        Assert.Equal([dummy.Id], events.OfType<AttackRolled>().Single(attack => attack.AttackerId == hero.Id).TargetIds);
        Assert.Equal(2, dummy.Hearts);
        Assert.False(hero.Conditions.Has(Condition.Confused));
    }

    [Fact]
    public void AConfusedMonsterLashesOutAndStaggersOff()
    {
        GameSession session = TestRun.Start(Beside('m'), new ScriptedDice(2, 1));
        Monster grunt = session.OnlyMonster();
        grunt.Conditions.Add(Condition.Confused, 1);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(new Staggered(grunt.Id), events);
        Assert.False(grunt.Conditions.Has(Condition.Confused));
    }

    [Fact]
    public void StormMailTurnsRumbleDiceToElektrik()
    {
        GameSession session = TestRun.Start(Beside('d'), new ScriptedDice(6, 2), cards: ["storm"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Equal([DieType.Elektrik, DieType.Elektrik], events.SingleOf<AttackRolled>().DieTypes);
        Assert.Empty(events.OfType<PowerDieAdded>());
        Assert.Single(events.OfType<Stunned>());
    }

    [Fact]
    public void ATypedWeaponsOwnDiceAreItsType()
    {
        GameSession session = TestRun.Start(Beside('d'), new ScriptedDice(3, 6, 1), cards: ["brand"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Equal([DieType.Plain, DieType.Ice, DieType.Ice], events.SingleOf<AttackRolled>().DieTypes);
        Assert.Equal(1, dummy.Conditions[Condition.Freeze]);
    }

    [Fact]
    public void AMonstersTypedDiceTakeHoldOfAHeroItHits()
    {
        GameSession session = TestRun.Start(Beside('v'), new ScriptedDice(6, 1, 1));
        Hero hero = session.HeroInSeat();

        session.EndRound();

        Assert.Equal(1, hero.Conditions[Condition.Poison]);
    }
}
