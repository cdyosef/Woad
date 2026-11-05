using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class GoldTests
{
    [Theory]
    [InlineData(0, "0g", "no coin")]
    [InlineData(5, "5c", "5 copper")]
    [InlineData(40, "4s", "4 silver")]
    [InlineData(235, "2g 3s 5c", "2 gold, 3 silver and 5 copper")]
    [InlineData(1203, "12g 3c", "12 gold and 3 copper")]
    public void CoinsAreWrittenInGoldSilverAndCopper(int copper, string shortly, string longly)
    {
        Assert.Equal(shortly, Money.Short(copper));
        Assert.Equal(longly, Money.Long(copper));
        Assert.Equal(copper, Money.Gold(copper / 100) + Money.Silver(copper / 10 % 10) + copper % 10);
    }

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

    [Fact]
    public void GoldOnTheFloorGoesIntoThePurseEvenWithAFullBackpack()
    {
        GameSession session = TestRun.Start(Open);
        Hero hero = session.HeroInSeat();
        for (int i = 0; i < GameRules.BackpackCapacity; i++)
        {
            hero.AddToBackpack(new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("potion"), broughtInByHeroId: null));
        }

        session.RoomById().AddCoins(hero.Position, 7);

        IReadOnlyList<GameEvent> events = session.Do(new PickUpLoot(hero.Id));

        Assert.Equal(new GoldFound(hero.Id, 7, 7), events.SingleOf<GoldFound>());
        Assert.Equal(0, session.RoomById().CoinsAt(hero.Position));
    }

    [Fact]
    public void GoldCountsAsTreasureAndComesHomeWithAnEscapedHero()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1*....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3), cards: ["coin"]);
        Hero hero = session.HeroInSeat();
        hero.Coins = 4;
        Assert.Equal(Money.Gold(5) + 4, hero.TreasureValue);
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 4)));

        session.Do(new Escape(hero.Id));

        Assert.Equal(Money.Gold(5) + 4, RunResults.For(session.State).Single().Coins);
    }

    [Fact]
    public void TheDeadDropTheirGoldOnTheirTombstone()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(1, 1), heroes: 2);
        Hero fallen = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;
        fallen.DeathMarks = 1;
        fallen.Coins = 6;
        session.Do(new EndTurn(session.HeroInSeat(0).Id));

        IReadOnlyList<GameEvent> events = session.Do(new RollKeel(fallen.Id));

        GridPosition grave = events.SingleOf<HeroDied>().Tombstone.Position;
        Assert.Equal(new GoldAppeared("A", grave, 6), events.SingleOf<GoldAppeared>());
        Assert.Equal((0, 6), (fallen.Coins, session.RoomById().CoinsAt(grave)));
    }
}
