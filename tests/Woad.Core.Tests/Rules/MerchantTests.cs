using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Chained merchants: freeing them, trading, theft and murder, following the party, and rescue.</summary>
public sealed class MerchantTests
{
    private static readonly GridPosition Chains = new(4, 4);

    private static GameSession Dungeon(string layout, ScriptedDice? dice = null, Stats? stats = null)
    {
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(layout), Features: new RoomFeatures { Merchant = new MerchantSetup(Chains, 5, "Silas") })], dice, stats: stats);
        session.RoomById().Merchants.Single().AddStock("sword");
        return session;
    }

    private const string Hall = """
        .........
        .........
        .........
        .........
        ...1.....
        .........
        .........
        .........
        .........
        """;

    private static Merchant Silas(GameSession session) => session.State.Rooms.Values.SelectMany(room => room.Merchants).Single();

    [Fact]
    public void AKeyFreesAChainedMerchantWhoThenFollowsTheirRescuer()
    {
        GameSession session = Dungeon(Hall);
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;

        Assert.Contains("free them first", session.Refuse(new BuyFromMerchant(hero.Id, Silas(session).Id, "sword")));
        IReadOnlyList<GameEvent> events = session.Do(new FreeMerchant(hero.Id, Silas(session).Id));

        Assert.Equal(new MerchantFreed(hero.Id, Silas(session).Id, Picked: false), events.SingleOf<MerchantFreed>());
        Assert.Equal((false, hero.Id, 0), (Silas(session).Chained, Silas(session).LeaderId, hero.Keys));
    }

    [Fact]
    public void PickingTheLockOfTheChainsFreesThemToo()
    {
        GameSession session = Dungeon(Hall, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new PickLock(hero.Id, Chains));

        Assert.True(events.SingleOf<MerchantFreed>().Picked);
        Assert.False(Silas(session).Chained);
    }

    [Fact]
    public void PricesFallWithGoodStandingAndRiseWithBad()
    {
        CardDefinition card = TestContent.Catalog.GetCard("gem");
        Assert.Equal(30, GameSession.MerchantPrice(card, 0));
        Assert.Equal(27, GameSession.MerchantPrice(card, 2));
        Assert.Equal(33, GameSession.MerchantPrice(card, -2));
        Assert.Equal(15, GameSession.MerchantPrice(card, 25));
    }

    [Fact]
    public void AFreedMerchantBesideYouBuysAndSells()
    {
        GameSession session = Dungeon(Hall);
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;
        hero.Gold = 5;
        session.Do(new FreeMerchant(hero.Id, Silas(session).Id));

        session.Do(new BuyFromMerchant(hero.Id, Silas(session).Id, "sword"));
        CardInstance sword = hero.Card("sword");
        Assert.Equal(4, hero.Gold);
        Assert.Empty(Silas(session).Stock);

        session.Do(new SellToMerchant(hero.Id, Silas(session).Id, sword.Id));
        Assert.Equal(["sword"], Silas(session).Stock);
        Assert.Equal(5, hero.Gold);
    }

    [Fact]
    public void AFreedMerchantFollowsYouAndKeepsAwayFromMonsters()
    {
        GameSession session = Dungeon(Hall);
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;
        session.Do(new FreeMerchant(hero.Id, Silas(session).Id));
        hero.Position = new GridPosition(1, 1);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Single(events.OfType<MerchantMoved>());
        Assert.True(Silas(session).Position.ChebyshevDistanceTo(hero.Position) <= Silas(session).Position.ManhattanDistanceTo(Chains) + 3);
        Assert.True(Silas(session).Position.ManhattanDistanceTo(hero.Position) < Chains.ManhattanDistanceTo(hero.Position));
    }

    [Fact]
    public void AFreedMerchantFollowsTheirLeaderIntoTheNextRoom()
    {
        RoomTemplate open = TestRun.Room(Hall);
        GameSession session = TestRun.Start(
        [
            new RoomSetup("A", open, new Dictionary<Direction, string> { [Direction.East] = "B" }, Features: new RoomFeatures { Merchant = new MerchantSetup(Chains, 5, "Silas") }),
            new RoomSetup("B", open, new Dictionary<Direction, string> { [Direction.West] = "A" }, MapPosition: new(1, 0)),
        ], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;
        hero.Position = new GridPosition(5, 4);
        session.Do(new FreeMerchant(hero.Id, Silas(session).Id));
        hero.Position = new GridPosition(8, 4);
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new GoThroughDoor(hero.Id, Direction.East));

        Assert.Equal("B", events.SingleOf<MerchantFollowed>().RoomId);
        Assert.Single(session.RoomById("B").Merchants);
    }

    [Fact]
    public void EscapingWithAFreedMerchantRescuesThemAndRaisesTheGuildsOpinion()
    {
        GameSession session = Dungeon("""
            .........
            .........
            .........
            .........
            ...1.....
            ...*.....
            .........
            .........
            .........
            """);
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;
        session.Do(new FreeMerchant(hero.Id, Silas(session).Id));
        hero.Position = new GridPosition(3, 5);

        IReadOnlyList<GameEvent> events = session.Do(new Escape(hero.Id));

        Assert.Equal("Silas", events.SingleOf<MerchantRescued>().Name);
        HeroRunResult result = RunResults.For(session.State).Single();
        Assert.Equal(GameSession.RescueStanding + GameSession.RescuerExtraStanding, result.GuildStandingChange);
        Assert.Equal([("Silas", true)], result.MerchantsRescued);
    }

    [Fact]
    public void StealingFromAMerchantCostsYourNameWithTheGuildAndTheLaw()
    {
        // The thief's 5 against the merchant's 1 and 1.
        GameSession session = Dungeon(Hall, new ScriptedDice(5, 1, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new Steal(hero.Id, Silas(session).Id));

        Assert.Equal("sword", events.SingleOf<MerchantRobbed>().CardId);
        Assert.Equal((-GameSession.TheftGuildCost, -GameSession.TheftLawCost), (hero.GuildStandingChange, hero.LawStandingChange));
        Assert.NotNull(hero.Card("sword"));
    }

    [Fact]
    public void StrikingAMerchantDownIsACrimeAndSpillsTheirWares()
    {
        // The hero's 5 against the merchant's 1.
        GameSession session = Dungeon(Hall, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMerchant(hero.Id, Silas(session).Id, UseWeapon: false));

        Assert.True(events.SingleOf<MerchantSlain>().ByHero);
        Assert.Equal((-GameSession.MurderGuildCost, -GameSession.MurderLawCost), (hero.GuildStandingChange, hero.LawStandingChange));
        Assert.Contains(session.RoomById().LootAt(Chains), card => card.Definition.Id == "sword");
    }

    [Fact]
    public void AMonsterWithNoHeroInReachGoesForAMerchant()
    {
        // The Sniper can't see the hero across the wall, but it can see Silas: its 5 against his 1.
        GameSession session = Dungeon("""
            .........
            .........
            .........
            .........
            .#.......
            .#.......
            .#.......
            1#....n..
            .#.......
            """, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        MerchantSlain slain = events.SingleOf<MerchantSlain>();
        Assert.False(slain.ByHero);
        Assert.Equal(0, hero.GuildStandingChange);
    }
}
