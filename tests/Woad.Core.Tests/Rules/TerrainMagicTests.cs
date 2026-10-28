using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Batch 3 of Magic: Icicle Arrow's ice, the boons (Lifeforce, Shining Light, Shadowbind), and light and darkness.</summary>
public class TerrainMagicTests
{
    private const string Empty = """
        .........
        .........
        .........
        .........
        .1.......
        .........
        .........
        .........
        .........
        """;

    private static Hero Mage(GameSession session, int seat, int power, params string[] spells)
    {
        Hero hero = session.HeroInSeat(seat);
        hero.BaseStats = hero.BaseStats with { Magic = 1 };
        foreach (string spell in spells)
        {
            hero.Learn(TestContent.Catalog.GetSpell(spell));
        }

        hero.Power.Banked = power;
        return hero;
    }

    private static GameSession Dark(string room, ScriptedDice dice, bool dark = true, string[]? cards = null) =>
        TestRun.Start([new RoomSetup("A", TestRun.Room(room), Dark: dark)], dice, cards: cards);

    [Fact]
    public void IcicleArrowAtTheFloorLaysAsMuchIceAsItRolls()
    {
        // One Ice die: 4. A patch where it's aimed, three more to lay, a round each.
        GameSession session = TestRun.Start(Empty, new ScriptedDice(4));
        Hero hero = Mage(session, 0, 1, "icicle_arrow");
        Room room = session.RoomById();

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "icicle_arrow", new GridPosition(3, 4)));

        Assert.Equal(new GridPosition(3, 4), events.SingleOf<IceLaid>().Tiles.Single());
        Assert.Equal(3, session.State.Turn!.IceToLay);
        session.Do(new LayIce(hero.Id, new GridPosition(2, 4)));
        Assert.Contains("icy already", session.Refuse(new LayIce(hero.Id, new GridPosition(2, 4))));
        Assert.Contains("out of range", session.Refuse(new LayIce(hero.Id, new GridPosition(8, 8))));
        session.Do(new LayIce(hero.Id, hero.Position));
        Assert.Equal(new BoonGained(hero.Id, Boon.Snowshoes, 1, 0), session.Do(new LayIce(hero.Id, hero.Position)).SingleOf<BoonGained>());
        Assert.Contains("no ice", session.Refuse(new LayIce(hero.Id, new GridPosition(1, 3))));

        Assert.Equal(1, room.Ice[new GridPosition(3, 4)]);
        session.EndRound();
        Assert.Empty(room.Ice);
        Assert.Equal(0, hero.SnowshoesRounds);
    }

    [Fact]
    public void IceFreezesDeepWaterOverUntilItMelts()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1.=.....
            .........
            .........
            .........
            .........
            """);
        Room room = session.RoomById();
        var water = new GridPosition(3, 4);

        room.Freeze(water, 1);
        Assert.Equal(TileKind.Floor, room.Layout[water]);
        room.Melt();
        Assert.Equal(TileKind.DeepWater, room.Layout[water]);
    }

    [Fact]
    public void SteppingOntoIceSlidesYouATileFurtherAndEndsTheMove()
    {
        GameSession session = TestRun.Start(Empty, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        session.RoomById().Freeze(new GridPosition(3, 4), 3);
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(6, 4)));

        Assert.Equal(new Slid(hero.Id, new GridPosition(3, 4), new GridPosition(4, 4)), events.SingleOf<Slid>());
        Assert.Equal(new GridPosition(4, 4), hero.Position);
        Assert.Equal(0, session.State.Turn!.MovementPoints);
    }

    [Fact]
    public void SnowshoesWalkIceLikeFloor()
    {
        GameSession session = TestRun.Start(Empty, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        hero.SnowshoesRounds = 2;
        session.RoomById().Freeze(new GridPosition(3, 4), 3);
        session.Do(new RollMovement(hero.Id));

        session.Do(new MoveHero(hero.Id, new GridPosition(6, 4)));

        Assert.Equal(new GridPosition(6, 4), hero.Position);
    }

    [Fact]
    public void AMonsterSlidesOnIceToo()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1....m..
            .........
            .........
            .........
            .........
            """, new ScriptedDice([.. Enumerable.Repeat(1, 10)]));
        Monster grunt = session.OnlyMonster();
        session.RoomById().Freeze(new GridPosition(4, 4), 3);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Contains(events, gameEvent => gameEvent is Slid slid && slid.EntityId == grunt.Id);
        Assert.Equal(new GridPosition(3, 4), grunt.Position);
    }

    [Fact]
    public void LifeforceOnYourselfGrowsPlantsAndHealsAHeartPerSix()
    {
        // The Life die's 6 rolls another (2): two dice, one 6.
        GameSession session = TestRun.Start(Empty, new ScriptedDice(6, 2));
        Hero hero = Mage(session, 0, 1, "lifeforce");
        hero.Hearts = 2;

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "lifeforce", hero.Position));

        Assert.Equal(new HeroHealed(hero.Id, 1, 3), events.SingleOf<HeroHealed>());
        Assert.True(session.RoomById().PlantsFor(hero.Position, heroes: true));
        Assert.Empty(events.OfType<AttackRolled>());
    }

    [Fact]
    public void ShiningLightOnAnAllyShinesARoundPerDieWithADefenseForEachSix()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1...2...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 3), heroes: 2);
        Hero hero = Mage(session, 0, 1, "shining_light");
        Hero ally = session.HeroInSeat(1);

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "shining_light", ally.Position));

        Assert.Equal(new BoonGained(ally.Id, Boon.ShiningLight, 2, 2), events.SingleOf<BoonGained>());
        session.EndRound();
        Assert.Equal(1, ally.ShiningRounds);
        Assert.Contains(new BoonEnded(ally.Id, Boon.ShiningLight), session.EndRound());
    }

    [Fact]
    public void ShadowboundMonstersCantSeeYouUntilYouAttack()
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
            """, new ScriptedDice([6, 1, .. Enumerable.Repeat(5, 10)]));
        Hero hero = Mage(session, 0, 1, "shadowbind");

        // The Shadow die's 6 rolls another: two dice, two rounds unseen.
        session.Do(new CastSpell(hero.Id, "shadowbind", hero.Position));
        Assert.Equal(2, hero.ShadowboundRounds);
        IReadOnlyList<GameEvent> monsterTurn = session.Do(new EndTurn(hero.Id));

        Assert.DoesNotContain(monsterTurn, gameEvent => gameEvent is AttackRolled attack && attack.AttackerId == session.OnlyMonster().Id);
        Assert.Contains(new BoonEnded(hero.Id, Boon.Shadowbound), session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id)));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ShootingIntoTheDarkIsHarder(bool dark, int heartsLeft)
    {
        // The bow: 3 + 1. In the dark, -1: 3 against the Knight's 1 + armor 2 holds; lit, 4 gets through.
        GameSession session = Dark("""
            .........
            .........
            .........
            .........
            .1..K....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3, 1), dark, cards: ["bow"]);
        Hero hero = session.HeroInSeat();
        Monster knight = session.OnlyMonster();
        Assert.Equal(dark, session.InDarkness(session.RoomById(), knight.Position));

        session.Do(new AttackMonster(hero.Id, knight.Id));

        Assert.Equal(heartsLeft, session.RoomById().FindMonster(knight.Id)?.Hearts ?? 0);
    }

    [Fact]
    public void AHeldTorchLightsTheDarkAroundIt()
    {
        GameSession session = Dark(Empty, new ScriptedDice(1), cards: ["t_torch"]);
        Room room = session.RoomById();

        Assert.False(session.InDarkness(room, new GridPosition(3, 4)));
        Assert.True(session.InDarkness(room, new GridPosition(4, 4)));
    }

    [Fact]
    public void ShadowbindBitesHarderInTheDark()
    {
        GameSession session = Dark("""
            .........
            .........
            .........
            .........
            .1...K...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(2, 3, 1));
        Hero hero = Mage(session, 0, 1, "shadowbind");
        Monster knight = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new CastSpell(hero.Id, "shadowbind", knight.Position));

        Assert.Equal([knight.Id], events.SingleOf<ExtraDiceRolled>().TargetIds);
    }

    [Fact]
    public void BraziersCatchFromATorchBesideThemOrFireAimedAtThem()
    {
        GameSession session = Dark("""
            .........
            .........
            .........
            .........
            .1^...^..
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1), cards: ["t_torch"]);
        Hero hero = Mage(session, 0, 1, "hurl_fire");
        Room room = session.RoomById();

        IReadOnlyList<GameEvent> events = session.Do(new RollMovement(hero.Id));
        Assert.Equal(new BrazierLit(room.Id, new GridPosition(2, 4)), events.SingleOf<BrazierLit>());

        events = session.Do(new CastSpell(hero.Id, "hurl_fire", new GridPosition(6, 4)));
        Assert.Equal(new BrazierLit(room.Id, new GridPosition(6, 4)), events.SingleOf<BrazierLit>());
        Assert.Empty(events.OfType<AttackRolled>());
        Assert.False(session.InDarkness(room, new GridPosition(7, 6)));
    }

    [Fact]
    public void SomeGeneratedRoomsAreDarkWithBraziersButNeverWhereYouArrive()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        int dark = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            RunSetup setup = DungeonGenerator.CreateRun(content, "emerald_keep", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], seed);
            Assert.All(setup.Rooms.Where(room => room.Dark), room => Assert.NotEqual(RoomRole.Arrival, room.Role));
            Assert.All(setup.Rooms.Where(room => room.Dark), room => Assert.NotEmpty(room.Template.TilesOf(TileKind.Brazier)));
            dark += setup.Rooms.Count(room => room.Dark);
        }

        Assert.True(dark > 0);
    }
}
