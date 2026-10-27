using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Mimic, the Ogre's knockback, the Hexer, the Necromancer and the Gelatinous Cube.</summary>
public sealed class NewMonsterTests
{
    private const string ChestRoom = """
        .........
        .........
        .........
        .........
        ...1C....
        .........
        .........
        .........
        .........
        """;

    private static readonly GridPosition Chest = new(4, 4);

    private static GameSession MimicRun(ScriptedDice dice, int searchNeeded = 6, bool locked = false) =>
        TestRun.Start([new RoomSetup("A", TestRun.Room(ChestRoom), Features: new RoomFeatures
        {
            Mimics = new Dictionary<GridPosition, int> { [Chest] = searchNeeded },
            LockedChests = locked ? new Dictionary<GridPosition, int> { [Chest] = 4 } : new Dictionary<GridPosition, int>(),
        })], dice);

    [Fact]
    public void OpeningAMimicWakesItAndItsBiteHoldsYouUntilYouHurtIt()
    {
        // The bite's 5 against the hero's 1, then a move roll of 3, then the hero's 5 on the Lurker (no defense dice).
        GameSession session = MimicRun(new ScriptedDice(5, 1, 3, 5));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new OpenChest(hero.Id, Chest));

        Monster mimic = session.OnlyMonster();
        Assert.Equal("lurker", mimic.Definition.Id);
        Assert.Equal(Chest, mimic.Position);
        Assert.Equal(TileKind.Floor, session.RoomById().Layout[Chest]);
        Assert.Equal(new MimicWoke(mimic.Id, "A", Chest, hero.Id), events.SingleOf<MimicWoke>());
        Assert.Equal(SpawnCause.Mimic, events.SingleOf<MonsterSpawned>().Cause);
        Assert.Single(events.OfType<Grabbed>());
        Assert.Equal(TestRun.HeroStats.Hearts - 1, hero.Hearts);

        session.Do(new RollMovement(hero.Id));
        Assert.Contains("has hold of you", session.Refuse(new MoveHero(hero.Id, new GridPosition(3, 2))));
        Assert.Empty(session.GetReachableTiles());

        session.Do(new AttackMonster(hero.Id, mimic.Id, UseWeapon: false));
        Assert.Null(session.HeldBy(hero));
        session.Do(new MoveHero(hero.Id, new GridPosition(3, 2)));
    }

    [Fact]
    public void TryingAMimicsLockWakesItWithoutAKeyOrARoll()
    {
        GameSession session = MimicRun(new ScriptedDice(1, 2), locked: true);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new PickLock(hero.Id, Chest));

        Assert.Single(events.OfType<MimicWoke>());
        Assert.Empty(events.OfType<OdHandRolled>());
        Assert.Equal(0, session.State.Turn!.OdHandDiceUsed);
    }

    [Fact]
    public void AGoodSearchFindsAMimicOutBeforeItCanBite()
    {
        GameSession session = MimicRun(new ScriptedDice(5), searchNeeded: 5);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new Search(hero.Id));

        Assert.Equal(0, events.SingleOf<MimicWoke>().HeroId);
        Assert.Empty(events.OfType<AttackRolled>());
        Assert.Equal(TestRun.HeroStats.Hearts, hero.Hearts);
        Assert.Null(session.RoomById().MimicAt(Chest));
    }

    [Fact]
    public void APoorSearchLeavesTheMimicHidden()
    {
        GameSession session = MimicRun(new ScriptedDice(4), searchNeeded: 5);

        IReadOnlyList<GameEvent> events = session.Do(new Search(session.HeroInSeat().Id));

        Assert.Empty(events.OfType<MimicWoke>());
        Assert.Empty(session.RoomById().Monsters);
    }

    [Fact]
    public void AGrabLetsGoWhenTheMonsterIsStunnedOrNoLongerBeside()
    {
        GameSession session = MimicRun(new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();
        session.Do(new OpenChest(hero.Id, Chest));
        Monster mimic = session.OnlyMonster();
        Assert.Same(mimic, session.HeldBy(hero));

        mimic.FrozenTurns = 1;
        Assert.Null(session.HeldBy(hero));
        mimic.FrozenTurns = 0;
        hero.Position = new GridPosition(1, 4);
        Assert.Null(session.HeldBy(hero));
    }

    [Fact]
    public void AnOgresBlowKnocksTheHeroBackIntoAPit()
    {
        // The Basher's 5 against the hero's 1: a hit that knocks them from (4,4) into the pit at (3,4).
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...~1o...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        KnockedBack knocked = events.SingleOf<KnockedBack>();
        Assert.Equal((new GridPosition(4, 4), new GridPosition(3, 4)), (knocked.From, knocked.To));
        Assert.Single(events.OfType<FellIntoPit>());
    }

    [Fact]
    public void AChampsSixesKnockTheHeroFurtherBackEvenOnAMiss()
    {
        // Two 6s (their Power Dice's 1s, then the two they banked, spent: 1s) can't get past the hero's armor, but each 6 knocks them a tile back.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1Z...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 6, 1, 1, 1, 1), stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 20 });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
        Assert.Equal(new GridPosition(2, 4), events.SingleOf<KnockedBack>().To);
        Assert.Equal(new GridPosition(2, 4), hero.Position);
    }

    [Fact]
    public void AGrandChestHoldsThreeCardsFromDeeperDownAndGold()
    {
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(ChestRoom), Features: new RoomFeatures
        {
            LockedChests = new Dictionary<GridPosition, int> { [Chest] = 8 },
            GrandChests = [Chest],
        })]);
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;

        IReadOnlyList<GameEvent> events = session.Do(new OpenChest(hero.Id, Chest));

        Assert.Equal(3, events.SingleOf<ChestOpened>().CardIds.Count);
        Assert.Equal(GameRules.GrandChestGold, hero.Gold);
    }

    [Fact]
    public void AWallStopsAKnockback()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...#1o...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Empty(events.OfType<KnockedBack>());
        Assert.Equal(new GridPosition(4, 4), hero.Position);
    }

    [Fact]
    public void AHexDrainsLuckThenStunsWhenThereIsNoneLeft()
    {
        // Two rounds of the Hexer's 5 against the hero's 1.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1...H...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1, 5, 1), stats: TestRun.HeroStats with { Luck = 1, Hearts = 6 });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> first = session.Do(new EndTurn(hero.Id));
        Assert.True(first.SingleOf<Hexed>().Drained);
        Assert.Equal(0, hero.LuckRemaining);
        Assert.Empty(first.OfType<Stunned>());

        IReadOnlyList<GameEvent> second = session.EndRound();
        Assert.False(second.SingleOf<Hexed>().Drained);
        Assert.Single(second.OfType<Stunned>());
    }

    [Fact]
    public void ANecromancerRaisesTheDeadEveryOtherTurnAndTheyCrumbleWithIt()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1N....
            .........
            .........
            .........
            .........
            """, new ScriptedDice([.. Enumerable.Repeat(1, 12), 5]));
        Hero hero = session.HeroInSeat();
        Monster raiser = session.OnlyMonster();

        IReadOnlyList<GameEvent> first = session.Do(new EndTurn(hero.Id));
        Assert.Equal(raiser.Id, first.SingleOf<MonsterRaised>().RaiserId);
        Assert.Equal(SpawnCause.Raised, first.SingleOf<MonsterSpawned>().Cause);
        Monster risen = session.RoomById().Monsters.Single(monster => monster != raiser);
        Assert.Equal(raiser.Id, risen.RaisedBy);

        // Its second turn raises nothing; its third raises the second (and last) of its dead.
        Assert.Empty(session.EndRound().OfType<MonsterRaised>());
        Assert.Single(session.EndRound().OfType<MonsterRaised>());
        Assert.Empty(session.EndRound().OfType<MonsterRaised>());
        Assert.Empty(session.EndRound().OfType<MonsterRaised>());
        Assert.Equal(3, session.RoomById().Monsters.Count);

        // Slay the Necromancer (a 5 against no defense): its dead fall to dust. (Its Skeletons' 1s only ever tied the hero's 1s.)
        int xp = hero.Xp;
        IReadOnlyList<GameEvent> slain = session.Do(new AttackMonster(hero.Id, raiser.Id, UseWeapon: false));
        Assert.Equal(2, slain.OfType<Crumbled>().Count());
        Assert.Empty(session.RoomById().Monsters);
        Assert.Equal(xp + 5, hero.Xp);
    }

    [Fact]
    public void ACubeSwallowsTheLootItCrawlsOverAndDropsItWhereItFalls()
    {
        // The Blob crawls from (6,4) over the gem at (5,4) to the hero; its 1 misses the hero's 2.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1..u..
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 2, 5));
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();
        var gem = new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("gem"), broughtInByHeroId: null);
        room.AddLoot(new GridPosition(5, 4), [gem]);
        room.AddGold(new GridPosition(5, 4), 4);
        Monster blob = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Engulfed engulfed = events.SingleOf<Engulfed>();
        Assert.Equal((new GridPosition(5, 4), 4), (engulfed.Tile, engulfed.Gold));
        Assert.Equal([gem.Id], engulfed.CardIds);
        Assert.False(room.HasLootAt(new GridPosition(5, 4)));

        session.Do(new AttackMonster(hero.Id, blob.Id, UseWeapon: false));
        Assert.Contains(room.LootPositions.SelectMany(room.LootAt), card => card.Id == gem.Id);
    }
}
