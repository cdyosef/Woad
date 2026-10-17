using Woad.Core.Bots;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class TradeAndOrderTests
{
    /// <summary>Hero 1 at (4, 4) with a companion (seat 1) diagonally beside them at (5, 5).</summary>
    private const string Side = """
        .........
        .........
        .........
        .........
        ....1....
        .....2...
        .........
        .........
        .........
        """;

    private static GameSession WithCompanion(string room = Side, string[]? cards = null) =>
        TestRun.Start(room, heroes: 2, cards: cards, owners: [null, 0]);

    [Fact]
    public void AlliesBesideEachOtherTradeCardsAndStacks()
    {
        GameSession session = WithCompanion(cards: ["sword", "coin", "coin", "coin"]);
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        CardInstance coins = you.Card("coin");

        session.Do(new GiveCard(you.Id, pal.Id, coins.Id, Count: 2));

        Assert.Equal(1, coins.Count);
        Assert.Equal(5, pal.Card("coin").Count);
        session.Do(new TakeCard(you.Id, pal.Id, pal.Card("sword").Id));
        Assert.Equal(2, you.AllCards.Count(card => card.Definition.Id == "sword"));
    }

    [Fact]
    public void TradingNeedsYouSideBySide()
    {
        GameSession session = WithCompanion("""
            .........
            .........
            .........
            .........
            ....1....
            .........
            ......2..
            .........
            .........
            """, ["sword"]);
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);

        Assert.Contains("next to them", session.Refuse(new GiveCard(you.Id, pal.Id, you.Card("sword").Id)));
    }

    [Fact]
    public void GoldAndKeysChangeHandsToo()
    {
        GameSession session = WithCompanion();
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        pal.Gold = 10;
        pal.HasBossKey = true;
        you.Keys = 2;

        session.Do(new TakeGold(you.Id, pal.Id, 4));
        session.Do(new TakeKey(you.Id, pal.Id, BossKey: true));
        session.Do(new GiveKey(you.Id, pal.Id));

        Assert.Equal((4, 6), (you.Gold, pal.Gold));
        Assert.True(you.HasBossKey);
        Assert.Equal((1, 1), (you.Keys, pal.Keys));
    }

    [Fact]
    public void AnOwnerDressesTheirCompanion()
    {
        GameSession session = WithCompanion(cards: ["sword", "big_sword"]);
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        CardInstance big = pal.Card("big_sword");

        IReadOnlyList<GameEvent> events = session.Do(new OutfitCompanion(you.Id, pal.Id, big.Id, OutfitAction.Equip));

        Assert.Equal(big, pal.MainWeapon);
        Assert.Contains(pal.Card("sword"), pal.Backpack);
        Assert.Single(events.OfType<CompanionOutfitted>());
    }

    [Fact]
    public void FindingItsOwnerACompanionUnlocksTheDoorInItsWay()
    {
        RoomSetup[] rooms =
        [
            new RoomSetup("A", TestRun.Room(".........\n.........\n.........\n.........\n.......1.\n.........\n.........\n.........\n........."), new Dictionary<Direction, string> { [Direction.East] = "B" }),
            new RoomSetup("B", TestRun.Room(".........\n.........\n.........\n.........\n....2....\n.........\n.........\n.........\n........."), new Dictionary<Direction, string> { [Direction.West] = "A" }),
        ];
        GameSession session = TestRun.Start(rooms, heroes: 2, owners: [null, 0], passages: [new PassageSetup("A", Direction.East, PassageKind.Locked, 5)]);
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);

        // The owner is through in B; the companion stands in A's locked doorway with a key.
        you.RoomId = "B";
        pal.RoomId = "A";
        you.Position = new GridPosition(4, 4);
        pal.Position = new GridPosition(8, 4);
        pal.Keys = 1;
        pal.Order = CompanionOrder.Follow;
        session.Do(new EndTurn(you.Id));
        Assert.IsNotType<UnlockDoor>(CompanionBot.NextCommand(session));

        pal.Order = CompanionOrder.FindMe;
        Assert.Equal(new UnlockDoor(pal.Id, Direction.East), CompanionBot.NextCommand(session));
    }

    [Fact]
    public void AnExploringCompanionSearchesEachRoomOnce()
    {
        GameSession session = WithCompanion();
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        pal.Order = CompanionOrder.Explore;
        session.Do(new EndTurn(you.Id));

        Assert.IsType<Search>(CompanionBot.NextCommand(session));
        pal.SearchedRoomIds.Add("A");
        Assert.IsNotType<Search>(CompanionBot.NextCommand(session));
    }

    [Fact]
    public void ALootingCompanionTakesWhatsBesideIt()
    {
        GameSession session = WithCompanion();
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        session.RoomById().AddLoot(new GridPosition(6, 6), [new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("gem"), null)]);
        session.Do(new EndTurn(you.Id));

        Assert.IsNotType<PickUpLoot>(CompanionBot.NextCommand(session));
        pal.Order = CompanionOrder.Loot;
        Assert.Equal(new PickUpLoot(pal.Id, new GridPosition(6, 6)), CompanionBot.NextCommand(session));
    }

    [Fact]
    public void ABurglingCompanionPicksALockedChestBesideIt()
    {
        GameSession session = TestRun.Start(
            [new RoomSetup("A", TestRun.Room(Side.Replace("....1....\n", "....1....\n").Replace(".....2...", ".....2C..")), Features: new RoomFeatures { LockedChests = new Dictionary<GridPosition, int> { [new(6, 5)] = 4 } })],
            heroes: 2, owners: [null, 0]);
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        pal.Order = CompanionOrder.Burgle;
        session.Do(new EndTurn(you.Id));

        Assert.Equal(new PickLock(pal.Id, new GridPosition(6, 5)), CompanionBot.NextCommand(session));
    }

    [Fact]
    public void ACompanionHandsTheBossKeyToItsOwner()
    {
        GameSession session = WithCompanion();
        Hero you = session.HeroInSeat(0), pal = session.HeroInSeat(1);
        pal.HasBossKey = true;
        session.Do(new EndTurn(you.Id));

        Assert.Equal(new GiveKey(pal.Id, you.Id, BossKey: true), CompanionBot.NextCommand(session));
    }
}
