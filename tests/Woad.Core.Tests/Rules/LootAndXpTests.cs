using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class LootAndXpTests
{
    private const string PotBeside = """
        .........
        .........
        .........
        ....P....
        ....1....
        .........
        .........
        .........
        .........
        """;

    private static readonly GridPosition Pot = new(4, 3);

    [Fact]
    public void PotsCanHoldCoins()
    {
        GameSession session = TestRun.Start(PotBeside, loot: new ScriptedDice(10));
        Hero hero = session.HeroInSeat();

        var smashed = session.Do(new SmashPot(hero.Id, Pot)).SingleOf<PotSmashed>();

        Assert.True(smashed.AddedToBackpack);
        Assert.Equal("coin", Assert.Single(hero.Backpack).Definition.Id);
        Assert.Equal(TileKind.Floor, session.RoomById().Layout[Pot]);
        Assert.True(session.State.Turn!.HasActed);
    }

    [Fact]
    public void PotsCanHoldAHeart()
    {
        GameSession session = TestRun.Start(PotBeside, loot: new ScriptedDice(60));
        Hero hero = session.HeroInSeat();
        hero.Hearts = 2;

        var smashed = session.Do(new SmashPot(hero.Id, Pot)).SingleOf<PotSmashed>();

        Assert.Equal(GameRules.HeartsFromPot, smashed.HeartsRestored);
        Assert.Null(smashed.CardId);
        Assert.Equal(2 + GameRules.HeartsFromPot, hero.Hearts);
    }

    [Fact]
    public void SomePotsAreEmpty()
    {
        GameSession session = TestRun.Start(PotBeside, loot: new ScriptedDice(90));
        Hero hero = session.HeroInSeat();

        var smashed = session.Do(new SmashPot(hero.Id, Pot)).SingleOf<PotSmashed>();

        Assert.Equal((null, 0), (smashed.CardId, smashed.HeartsRestored));
        Assert.Empty(hero.Backpack);
    }

    [Fact]
    public void SmashingNeedsAnAdjacentPotAndYourAction()
    {
        GameSession session = TestRun.Start(PotBeside.Replace("....1....", "...1....."), new ScriptedDice(3), loot: new ScriptedDice(90));
        Hero hero = session.HeroInSeat();

        Assert.Contains("Stand next to", session.Refuse(new SmashPot(hero.Id, Pot)));
        Assert.Contains("no pot", session.Refuse(new SmashPot(hero.Id, new GridPosition(3, 3))));

        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 4)));
        session.Do(new SmashPot(hero.Id, Pot));
        Assert.Contains("no pot", session.Refuse(new SmashPot(hero.Id, Pot)));
    }

    [Fact]
    public void PotsBlockTheWayUntilSmashed()
    {
        GameSession session = TestRun.Start(PotBeside, new ScriptedDice(1, 1), cards: ["boots"], loot: new ScriptedDice(90));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id, Dice: 1));

        Assert.DoesNotContain(Pot, session.GetReachableTiles().Keys);
        session.Do(new SmashPot(hero.Id, Pot));
        session.Do(new RollMovement(hero.Id, Dice: 1));
        Assert.Contains(Pot, session.GetReachableTiles().Keys);
    }

    [Fact]
    public void DefeatedMonstersCanDropLootAndGiveXp()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....h....
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Equal(new XpGained(hero.Id, 7, 7), events.SingleOf<XpGained>());
        Assert.Equal(2, events.OfType<LootDropped>().Count());
        Assert.Equal(2, session.RoomById().LootAt(new GridPosition(4, 3)).Sum(card => card.Count));
        Assert.Equal(7, hero.Xp);
    }

    [Fact]
    public void DeeperLootWaitsForDeeperFloors()
    {
        var deck = new LootDeck(TestContent.Catalog.GetTheme(TestContent.ThemeId), TestContent.Catalog, new Pcg32(3));

        Assert.All(Enumerable.Range(0, 12), _ => Assert.Equal("coin", deck.Draw(floor: 1).Id));
        Assert.Contains("gem", Enumerable.Range(0, 3).Select(_ => deck.Draw(floor: 2).Id));
    }
}
