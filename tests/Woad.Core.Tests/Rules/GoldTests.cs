using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class GoldTests
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

    [Fact]
    public void GoldOnTheFloorGoesIntoThePurseEvenWithAFullBackpack()
    {
        GameSession session = TestRun.Start(Open);
        Hero hero = session.HeroInSeat();
        for (int i = 0; i < GameRules.BackpackCapacity; i++)
        {
            hero.AddToBackpack(new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("potion"), broughtInByHeroId: null));
        }

        session.RoomById().AddGold(hero.Position, 7);

        IReadOnlyList<GameEvent> events = session.Do(new PickUpLoot(hero.Id));

        Assert.Equal(new GoldFound(hero.Id, 7, 7), events.SingleOf<GoldFound>());
        Assert.Equal(0, session.RoomById().GoldAt(hero.Position));
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
        hero.Gold = 4;
        Assert.Equal(9, hero.TreasureValue);
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 4)));

        session.Do(new Escape(hero.Id));

        Assert.Equal(9, RunResults.For(session.State).Single().Gold);
    }

    [Fact]
    public void TheDeadDropTheirGoldOnTheirTombstone()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(1, 1), heroes: 2);
        Hero fallen = session.HeroInSeat(1);
        fallen.Status = HeroStatus.KeelingOver;
        fallen.DeathMarks = 1;
        fallen.Gold = 6;
        session.Do(new EndTurn(session.HeroInSeat(0).Id));

        IReadOnlyList<GameEvent> events = session.Do(new RollKeel(fallen.Id));

        GridPosition grave = events.SingleOf<HeroDied>().Tombstone.Position;
        Assert.Equal(new GoldAppeared("A", grave, 6), events.SingleOf<GoldAppeared>());
        Assert.Equal((0, 6), (fallen.Gold, session.RoomById().GoldAt(grave)));
    }
}
