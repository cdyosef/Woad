using Woad.Core.Bots;
using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Magic stat and casting the basic spells.</summary>
public class MagicTests
{
    private const string Range = """
        .........
        .........
        .........
        .........
        .1..d....
        .........
        .........
        .........
        .........
        """;

    /// <summary>The hero in the first seat, made a caster: their Magic rank, banked Power and the spells they know.</summary>
    private static Hero Mage(GameSession session, int magic, int power, params string[] spells)
    {
        Hero hero = session.HeroInSeat();
        hero.BaseStats = hero.BaseStats with { Magic = magic };
        foreach (string spell in spells)
        {
            hero.Learn(TestContent.Catalog.GetSpell(spell));
        }

        hero.Power.Banked = power;
        return hero;
    }

    [Fact]
    public void ARankOfMagicCostsTwoPointsAndTeachesABasicSpell()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        var allocation = new StatAllocation { Magic = 1 };
        Assert.Equal(2, PointBuy.Cost(allocation));
        Assert.Throws<ArgumentException>(() => Character.Create("Morgana", "moss", allocation, content, DateTimeOffset.UnixEpoch));

        Character mage = Character.Create("Morgana", "moss", allocation, content, DateTimeOffset.UnixEpoch, spells: ["hurl_fire"]);
        Assert.Equal(["hurl_fire"], mage.Spells);
        Assert.Throws<ArgumentException>(() => mage.Allocate(allocation with { Magic = 2 }, ["hurl_fire"], content));

        mage.Allocate(allocation with { Magic = 2 }, ["quake"], content);
        Assert.Equal(["hurl_fire", "quake"], mage.Spells);
        Assert.Equal(["hurl_fire", "quake"], mage.ToHeroSetup().Spells);
        Assert.NotNull(PointBuy.Check(new StatAllocation { Magic = GameRules.MaxMagic + 1 }, 20, 100));
    }

    [Fact]
    public void ThePowerBankHoldsAnotherDieForEachRankOfMagic()
    {
        GameSession session = TestRun.Start(Range);
        Hero hero = Mage(session, magic: 2, power: 0);

        Assert.Equal(GameRules.MaxBankedPowerDice + 2, hero.Power.Capacity);
    }

    [Fact]
    public void HurlFireSpendsAPowerAndTheRumbleDiceAndRollsAFireDiePlusTheMagicRank()
    {
        GameSession session = TestRun.Start(Range, new ScriptedDice(4));
        Hero hero = Mage(session, magic: 1, power: 2, "hurl_fire");
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "hurl_fire", dummy.Position));

        var cast = events.SingleOf<SpellCast>();
        Assert.Equal(("hurl_fire", 1, dummy.Id), (cast.SpellId, cast.Power, cast.TargetIds.Single()));
        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal((5, DieType.Fire), (attack.Total, attack.DieTypes.Single()));
        Assert.Equal(2, dummy.Hearts);
        Assert.Equal((1, 0), (hero.Power.Banked, session.AttackDiceLeft));
    }

    [Fact]
    public void EachExtraPowerAddsADieUpToTheMagicRank()
    {
        GameSession session = TestRun.Start(Range, new ScriptedDice(2, 3));
        Hero hero = Mage(session, magic: 1, power: 3, "hurl_fire");
        Monster dummy = session.OnlyMonster();

        Assert.Contains("at most 1 more Power", session.Refuse(new CastSpell(hero.Id, "hurl_fire", dummy.Position, ExtraPower: 2)));
        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "hurl_fire", dummy.Position, ExtraPower: 1));

        Assert.Equal(6, events.SingleOf<AttackRolled>().Total);
        Assert.Equal(1, hero.Power.Banked);
    }

    [Fact]
    public void CastingTakesTheSpellThePowerAndARumbleDie()
    {
        GameSession session = TestRun.Start(Range, new ScriptedDice(1, 1));
        Hero hero = Mage(session, magic: 1, power: 0, "hurl_fire");
        GridPosition dummy = session.OnlyMonster().Position;

        Assert.Contains("don't know", session.Refuse(new CastSpell(hero.Id, "quake")));
        Assert.Contains("takes 1 Power", session.Refuse(new CastSpell(hero.Id, "hurl_fire", dummy)));
        hero.Power.Banked = 2;
        session.Do(new CastSpell(hero.Id, "hurl_fire", dummy));
        Assert.Contains("Rumble dice", session.Refuse(new CastSpell(hero.Id, "hurl_fire", dummy)));
    }

    [Fact]
    public void ShiningLightReachesPastAWallWhereHurlFireCant()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1.#d....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3));
        Hero hero = Mage(session, magic: 1, power: 3, "hurl_fire", "shining_light");
        GridPosition dummy = session.OnlyMonster().Position;

        Assert.Contains("out of range", session.Refuse(new CastSpell(hero.Id, "hurl_fire", dummy)));
        Assert.Contains(dummy, session.GetSpellTargets("shining_light"));
        session.Do(new CastSpell(hero.Id, "shining_light", dummy));
    }

    [Fact]
    public void PoisonMistFillsTheLineAndDrawsThePoisonOutOfTheCaster()
    {
        // A Poison die and two more for the caster's tokens: 1, 1, 1 and the Magic rank.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1dd.....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 1, 1));
        Hero hero = Mage(session, magic: 1, power: 1, "poison_mist");
        hero.Conditions.Add(Condition.Poison, 2);
        List<int> dummies = [.. session.RoomById().Monsters.Select(monster => monster.Id)];

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "poison_mist", new GridPosition(3, 4)));

        Assert.Equal(dummies.Order(), events.SingleOf<SpellCast>().TargetIds.Order());
        Assert.Equal(4, events.SingleOf<AttackRolled>().Total);
        Assert.False(hero.Conditions.Has(Condition.Poison));
    }

    [Fact]
    public void QuakeShakesEveryMonsterBesideTheCasterAndThoseOnRubbleHarder()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ...d.....
            ..d1:....
            .........
            .........
            .........
            .......d.
            """, new ScriptedDice(1, 4));
        Hero hero = Mage(session, magic: 1, power: 1, "quake");
        Monster onRubble = session.RoomById().Monsters.Single(monster => monster.Position == new GridPosition(7, 8));
        onRubble.Position = new GridPosition(4, 4);

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "quake"));

        Assert.Equal(3, events.SingleOf<SpellCast>().TargetIds.Count);
        Assert.Equal([onRubble.Id], events.SingleOf<ExtraDiceRolled>().TargetIds);
        Assert.Equal(2, onRubble.Hearts);
    }

    [Fact]
    public void AcidBallSplashesSomeoneBesideItsTarget()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .....d...
            .1..d....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3));
        Hero hero = Mage(session, magic: 1, power: 1, "acid_ball");

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "acid_ball", new GridPosition(4, 4)));

        Assert.Equal(2, events.SingleOf<SpellCast>().TargetIds.Count);
        Assert.All(session.RoomById().Monsters, monster => Assert.Equal(2, monster.Hearts));
    }

    [Fact]
    public void ElektroboltsSixJumpsToAMonsterBesideItsTarget()
    {
        // The Elektrik 6 rolls another (1): 7 + 1.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .....d...
            .1..d....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1));
        Hero hero = Mage(session, magic: 1, power: 1, "elektrobolt");
        Monster beside = session.RoomById().Monsters.Single(monster => monster.Position == new GridPosition(5, 3));

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "elektrobolt", new GridPosition(4, 4)));

        Assert.Equal(new Cleaved(hero.Id, beside.Id), events.SingleOf<Cleaved>());
        Assert.Equal(2, beside.Hearts);
    }

    [Fact]
    public void SquallWavesRumbleDiceShoveTheTargetToo()
    {
        // The Squall die (2) and the Magic rank hit; then the Rumble die (5) shoves the dummy back a tile.
        GameSession session = TestRun.Start(Range, new ScriptedDice(2, 5));
        Hero hero = Mage(session, magic: 1, power: 1, "squall_wave");
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "squall_wave", dummy.Position));

        Assert.Equal(2, events.OfType<AttackRolled>().Count());
        Assert.Equal(new GridPosition(5, 4), dummy.Position);
    }

    [Fact]
    public void SwirlyEyesMovesTheTargetItHitsWhereTheCasterChooses()
    {
        GameSession session = TestRun.Start(Range, new ScriptedDice(3));
        Hero hero = Mage(session, magic: 1, power: 1, "swirly_eyes");
        Monster dummy = session.OnlyMonster();

        Assert.Contains("free tile beside it", session.Refuse(new CastSpell(hero.Id, "swirly_eyes", dummy.Position, MoveTo: new GridPosition(6, 4))));
        session.Do(new CastSpell(hero.Id, "swirly_eyes", dummy.Position, MoveTo: new GridPosition(4, 3)));

        Assert.Equal(new GridPosition(4, 3), dummy.Position);
    }

    [Fact]
    public void AShootingStarsSixSurges()
    {
        GameSession session = TestRun.Start(Range, new ScriptedDice(6, 2, 1));
        Hero hero = Mage(session, magic: 1, power: 1, "shooting_star");

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "shooting_star", session.OnlyMonster().Position));

        Assert.Equal(9, events.SingleOf<PowerDieAdded>().Total);
        Assert.Equal(1, hero.Power.Banked);
    }

    [Fact]
    public void ARankOfMagicBoughtInARunTeachesTheSpellPicked()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character hero = Character.Create("Morgana", "moss", StatAllocation.None, content, DateTimeOffset.UnixEpoch);
        var session = new GameSession(content, DungeonGenerator.CreateRun(content, "emerald_keep", [hero.ToHeroSetup()], 1));
        session.Begin();
        Hero mage = session.HeroInSeat();

        Assert.Contains("Pick the basic spell", session.Refuse(new SpendPoint(mage.Id, mage.Id, StatKind.Magic)));
        IReadOnlyList<GameEvent> events = session.Do(new SpendPoint(mage.Id, mage.Id, StatKind.Magic, "elektrobolt"));

        Assert.Equal(new SpellLearned(mage.Id, "elektrobolt"), events.SingleOf<SpellLearned>());
        Assert.Equal((1, 4), (mage.EffectiveStats.Magic, mage.Power.Capacity));
        Assert.Contains("Pick a basic spell", session.Refuse(new SpendPoint(mage.Id, mage.Id, StatKind.Magic, "elektrobolt")));
    }

    [Fact]
    public void AFightingCompanionCastsAtWhatItsWeaponCantReach()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            1.2...d..
            .........
            .........
            .........
            .........
            """, new ScriptedDice([.. Enumerable.Repeat(5, 40)]), heroes: 2, owners: [null, 0]);
        Hero companion = session.HeroInSeat(1);
        companion.BaseStats = companion.BaseStats with { Magic = 1 };
        companion.Learn(TestContent.Catalog.GetSpell("hurl_fire"));
        companion.Power.Banked = 1;
        session.Do(new OrderCompanion(session.HeroInSeat(0).Id, companion.Id, CompanionOrder.Fight));

        session.Do(new EndTurn(session.HeroInSeat(0).Id));
        Assert.Equal(new CastSpell(companion.Id, "hurl_fire", session.OnlyMonster().Position), CompanionBot.NextCommand(session));
    }
}
