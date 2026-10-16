using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class ThiefTests
{
    private const string Beside = """
        .........
        .........
        .........
        .........
        ....1T...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void EachDieThatBeatsTheHeroesOwnRollStealsGoldThenAnItemThenGold()
    {
        // 5, 5, 5 against the hero's 2: three hits, for 2 gold, a card and 2 more gold. Then it fights no more.
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5, 5, 5, 2));
        Hero hero = session.HeroInSeat();
        hero.Gold = 10;
        var gem = new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("gem"), broughtInByHeroId: null);
        hero.AddToBackpack(gem);
        Monster thief = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new MonsterStole(thief.Id, hero.Id, 3, 4, gem.Id), events.SingleOf<MonsterStole>());
        Assert.Equal((6, 4), (hero.Gold, thief.StolenGold));
        Assert.Empty(hero.Backpack);
        Assert.Empty(events.OfType<Damaged>());
    }

    [Fact]
    public void AThiefWithLootRunsFromEveryone()
    {
        GameSession session = TestRun.Start(Beside);
        Monster thief = session.OnlyMonster();
        thief.StolenGold = 2;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.True(thief.Position.ManhattanDistanceTo(session.HeroInSeat().Position) >= 4);
        Assert.Empty(events.OfType<AttackRolled>());
    }

    [Fact]
    public void AStunnedThiefCantSteal()
    {
        // No Od Hand action: it attacks instead (1 against the hero's 6).
        GameSession session = TestRun.Start(Beside, new ScriptedDice(1, 6));
        Hero hero = session.HeroInSeat();
        hero.Gold = 10;
        Monster thief = session.OnlyMonster();
        thief.Stun.Add(powerful: false);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Empty(events.OfType<MonsterStole>());
        Assert.Equal(10, hero.Gold);
    }

    [Fact]
    public void KillingAThiefDropsWhatItStole()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Monster thief = session.OnlyMonster();
        thief.Hearts = 1;
        thief.StolenGold = 4;
        var gem = new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("gem"), broughtInByHeroId: null);
        thief.StolenCards.Add(gem);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, thief.Id, UseWeapon: false));

        GridPosition spot = events.SingleOf<GoldAppeared>().Position;
        Assert.Equal(4, session.RoomById().GoldAt(spot));
        Assert.Equal([gem], session.RoomById().LootAt(spot));
    }

    [Fact]
    public void AMuggerAttacksDieByDieAndEachHitTakesAGoldPiece()
    {
        // Its steal (1 against the hero's 5) fails; then two 1-die attacks, 5 each against defense 1: two hits.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1Y...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 5, 5, 1, 5, 1));
        Hero hero = session.HeroInSeat();
        hero.Gold = 5;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(0, events.SingleOf<MonsterStole>().Hits);
        Assert.Equal(2, events.OfType<Mugged>().Count());
        Assert.Equal((3, 2), (hero.Gold, hero.Hearts));
    }

    [Fact]
    public void AMuggerGoesForTheRichest()
    {
        // Two heroes beside it; the second carries more gold, so the mugger's attack goes to them.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1Y2...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 6, 5, 1, 5, 1), heroes: 2);
        Hero poor = session.HeroInSeat(0);
        Hero rich = session.HeroInSeat(1);
        rich.Gold = 9;
        session.Do(new EndTurn(poor.Id));

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(rich.Id));

        Assert.All(events.OfType<Damaged>(), damaged => Assert.Equal(rich.Id, damaged.TargetId));
    }
}
