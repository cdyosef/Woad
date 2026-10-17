using Woad.Core.Commands;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class StackAndLootTests
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

    private static CardInstance Card(GameSession session, string id, int count = 1) =>
        new(session.State.NextId(), TestContent.Catalog.GetCard(id), broughtInByHeroId: null, count);

    [Fact]
    public void CopiesOfAStackableCardGatherIntoOneSlot()
    {
        GameSession session = TestRun.Start(Open, cards: ["tonic", "tonic", "tonic"]);
        Hero hero = session.HeroInSeat();

        CardInstance tonics = Assert.Single(hero.Belt);
        Assert.Equal(3, tonics.Count);

        // Picking up another joins the stack, even with the backpack full of other things.
        session.RoomById().AddLoot(new GridPosition(4, 5), [Card(session, "tonic")]);
        session.Do(new PickUpLoot(hero.Id, new GridPosition(4, 5)));
        Assert.Equal(4, tonics.Count);
        Assert.Empty(hero.Backpack);
    }

    [Fact]
    public void UsingOneTakesItOffTheStack()
    {
        GameSession session = TestRun.Start(Open, cards: ["tonic", "tonic"]);
        Hero hero = session.HeroInSeat();
        hero.Hearts = 1;
        CardInstance tonics = hero.Card("tonic");

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, tonics.Id));

        Assert.Equal(new CardConsumed(hero.Id, tonics.Id, Left: 1), events.SingleOf<CardConsumed>());
        Assert.Equal(1, tonics.Count);
    }

    [Fact]
    public void DroppingPartOfAStackSplitsIt()
    {
        GameSession session = TestRun.Start(Open, cards: ["coin", "coin", "coin"]);
        Hero hero = session.HeroInSeat();
        CardInstance coins = hero.Card("coin");

        session.Do(new DropCard(hero.Id, coins.Id, Count: 2));

        Assert.Equal(1, coins.Count);
        Assert.Equal(2, Assert.Single(session.RoomById().LootAt(hero.Position)).Count);
        Assert.Equal(5, hero.TreasureValue);
    }

    [Fact]
    public void TreasureIsWorthEveryCopyInTheStack()
    {
        GameSession session = TestRun.Start(Open, cards: ["gem", "gem", "coin"]);

        Assert.Equal(2 * 15 + 5, session.HeroInSeat().TreasureValue);
    }

    [Fact]
    public void PilesCanBeReachedFromBesideThemDiagonalsToo()
    {
        GameSession session = TestRun.Start(Open);
        Hero hero = session.HeroInSeat();
        CardInstance gem = Card(session, "gem");
        session.RoomById().AddLoot(new GridPosition(5, 5), [gem]);
        session.RoomById().AddLoot(new GridPosition(4, 6), [Card(session, "coin")]);

        Assert.Contains("next to it", session.Refuse(new TakeLoot(hero.Id, new GridPosition(4, 6), session.RoomById().LootAt(new GridPosition(4, 6))[0].Id)));
        session.Do(new TakeLoot(hero.Id, new GridPosition(5, 5), gem.Id));

        Assert.Equal([gem], hero.Backpack);
    }

    [Fact]
    public void MoreFromTheSamePileIsFreeButAnotherPileTakesTheAction()
    {
        GameSession session = TestRun.Start(Open);
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();
        CardInstance gem = Card(session, "gem"), coin = Card(session, "coin"), other = Card(session, "gem");
        room.AddLoot(new GridPosition(5, 4), [gem, coin]);
        room.AddLoot(new GridPosition(3, 4), [other]);

        session.Do(new TakeLoot(hero.Id, new GridPosition(5, 4), gem.Id));
        session.Do(new TakeLoot(hero.Id, new GridPosition(5, 4), coin.Id));
        Assert.False(session.State.Turn!.HasActed);

        session.Do(new TakeLoot(hero.Id, new GridPosition(3, 4), other.Id));
        Assert.True(session.State.Turn!.HasActed);
    }

    [Fact]
    public void YouCanPutCardsIntoAPileBesideYou()
    {
        GameSession session = TestRun.Start(Open, cards: ["sword", "coin", "coin"]);
        Hero hero = session.HeroInSeat();
        session.RoomById().AddLoot(new GridPosition(5, 5), [Card(session, "coin")]);

        session.Do(new PutLoot(hero.Id, new GridPosition(5, 5), hero.Card("coin").Id, Count: 1));

        Assert.Equal(2, Assert.Single(session.RoomById().LootAt(new GridPosition(5, 5))).Count);
        Assert.Equal(1, hero.Card("coin").Count);
        Assert.False(session.State.Turn!.HasActed);
    }

    [Fact]
    public void AnOpenedChestKeepsWhatYouLeaveInIt()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....C....
            ....1....
            .........
            .........
            .........
            .........
            """);
        Hero hero = session.HeroInSeat();
        var chest = new GridPosition(4, 3);
        session.Do(new OpenChest(hero.Id, chest));
        session.RoomById().AddLoot(chest, [Card(session, "gem")]);

        // Free right after opening: the chest is the pile looted this turn.
        session.Do(new TakeLoot(hero.Id, chest, session.RoomById().LootAt(chest)[0].Id));

        Assert.Single(session.RoomById().LootAt(chest));
        Assert.False(session.State.Turn!.HasActed);
    }

    [Fact]
    public void AFailedPickWithAOneShowingSnapsALockpick()
    {
        // Od Hand 1, the pick's die 2, +1: 4 against 5, with a 1 showing.
        GameSession session = LockedDoor(new ScriptedDice(1, 2), "picks", "picks");
        Hero hero = session.HeroInSeat();
        CardInstance picks = hero.Card("picks");
        Assert.Equal(2, picks.Count);

        IReadOnlyList<GameEvent> events = session.Do(new PickLock(hero.Id, hero.Position));

        Assert.Equal(new LockpickSnapped(hero.Id, picks.Id, 1), events.SingleOf<LockpickSnapped>());
        Assert.Equal(1, picks.Count);
    }

    [Fact]
    public void APickThatWorksOrFailsWithoutAOneSurvives()
    {
        GameSession worked = LockedDoor(new ScriptedDice(1, 4), "picks");
        Assert.Empty(worked.Do(new PickLock(worked.HeroInSeat().Id, worked.HeroInSeat().Position)).OfType<LockpickSnapped>());

        GameSession failed = LockedDoor(new ScriptedDice(2, 2), "picks");
        Assert.Empty(failed.Do(new PickLock(failed.HeroInSeat().Id, failed.HeroInSeat().Position)).OfType<LockpickSnapped>());
    }

    [Fact]
    public void ASixOnTheToolsDieRollsAnotherStraightIn()
    {
        // Od Hand 2, the pick's 6 and the extra 3, +1: 12, the extra die a Power Die.
        GameSession session = LockedDoor(new ScriptedDice(2, 6, 3), "picks");
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new PickLock(hero.Id, hero.Position));

        OdHandRolled rolled = events.SingleOf<OdHandRolled>();
        Assert.Equal((12, 1), (rolled.Total, rolled.PowerDice));
        Assert.Equal(new CritRolled(hero.Id, 1), events.SingleOf<CritRolled>());
    }

    [Fact]
    public void ToolsDontHelpSearching()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3), cards: ["picks"]);
        Hero hero = session.HeroInSeat();

        OdHandRolled rolled = session.Do(new Search(hero.Id)).SingleOf<OdHandRolled>();

        Assert.Equal((0, 3), (rolled.Bonus, rolled.Total));
    }

    [Fact]
    public void TheLongswordAddsADieAndMoreHeldInBothHands()
    {
        // Rumble 3, its die 4, Weapon 1, and +1 with the off hand free: 9. With a shield: 8.
        GameSession twoHanded = Duel(new ScriptedDice(3, 4, 1), "longsword");
        Assert.Equal(9, Strike(twoHanded).Total);

        GameSession sworded = Duel(new ScriptedDice(3, 4, 1), "longsword", "shield");
        AttackRolled attack = Strike(sworded);
        Assert.Equal((8, 1), (attack.Total, attack.WeaponDice));
    }

    [Fact]
    public void FallingLosesOnlyTheCopiesFound()
    {
        GameSession session = TestRun.Start(Open, cards: ["coin"]);
        Hero hero = session.HeroInSeat();
        session.RoomById().AddLoot(hero.Position, [Card(session, "coin", 2)]);
        session.Do(new PickUpLoot(hero.Id));
        CardInstance coins = hero.Card("coin");

        Assert.Equal((3, 2), (coins.Count, coins.FoundBy(hero.Id)));
    }

    private static GameSession LockedDoor(ScriptedDice dice, params string[] cards)
    {
        RoomSetup[] rooms =
        [
            new RoomSetup("A", TestRun.Room(".........\n.........\n.........\n.........\n........1\n.........\n.........\n.........\n........."), new Dictionary<Direction, string> { [Direction.East] = "B" }),
            new RoomSetup("B", TestRun.Room(Open.Replace('1', '.')), new Dictionary<Direction, string> { [Direction.West] = "A" }),
        ];
        return TestRun.Start(rooms, dice, cards: cards, passages: [new PassageSetup("A", Direction.East, PassageKind.Locked, 5)]);
    }

    private static GameSession Duel(ScriptedDice dice, params string[] cards) => TestRun.Start("""
        .........
        .........
        .........
        .........
        ....1d...
        .........
        .........
        .........
        .........
        """, dice, cards: cards);

    private static AttackRolled Strike(GameSession session) =>
        session.Do(new AttackMonster(session.HeroInSeat().Id, session.OnlyMonster().Id)).SingleOf<AttackRolled>();
}
