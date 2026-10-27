using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tavern;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Acid wears a hero's armor, and it stays worn until a blacksmith or an Autosmith Hammer mends it.</summary>
public class GearWearTests
{
    private const string Spitter = """
        .........
        .........
        .........
        ....A....
        ....1....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void AcidWearsTheArmorAHeroHasOn()
    {
        // The spitter's 6 and its Acid die (1): 7 against the hero's 1 and the shield's 1.
        GameSession session = TestRun.Start(Spitter, new ScriptedDice(6, 1, 1), cards: ["shield"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.EndRound();

        CardInstance shield = hero.Card("shield");
        Assert.Equal(new GearCorroded(hero.Id, shield.Id, 1, 0), events.SingleOf<GearCorroded>());
        Assert.Equal(0, hero.EffectiveStats.Armor);
        Assert.Equal("shield~1", shield.Key);
        Assert.False(hero.Conditions.Has(Condition.Acid));
    }

    [Fact]
    public void AcidOnAHeroWithNoArmorLeftBurnsInstead()
    {
        GameSession session = TestRun.Start(Spitter, new ScriptedDice(6, 1, 1));
        Hero hero = session.HeroInSeat();

        session.EndRound();

        Assert.Equal(1, hero.Conditions[Condition.Acid]);
    }

    [Fact]
    public void AnAutosmithHammerMendsTheMostWornGear()
    {
        GameSession session = TestRun.Start(Spitter, cards: ["shield", "mender"]);
        Hero hero = session.HeroInSeat();
        CardInstance shield = hero.Card("shield");
        shield.Wear = 1;

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("mender").Id, null));

        Assert.Equal(new GearMended(hero.Id, shield.Id), events.SingleOf<GearMended>());
        Assert.Equal(0, shield.Wear);
        Assert.Equal(1, hero.EffectiveStats.Armor);
    }

    [Fact]
    public void WornGearComesIntoARunWornAndTheBlacksmithMendsItForGold()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character hero = Character.Create("Morgan", "ember", StatAllocation.None, content, DateTimeOffset.UnixEpoch);
        hero.TakeCard("buckler");
        hero.GiveCard("chain_mail~2", content);
        hero.EarnGold(5);
        Assert.Equal(1, hero.Stats(content).Armor);

        RunSetup setup = DungeonGenerator.CreateRun(content, "emerald_keep", [hero.ToHeroSetup()], 1);
        var session = new GameSession(content, setup);
        session.Begin();
        Assert.Equal(2, session.HeroInSeat().Card("chain_mail").Wear);

        Assert.Equal(4, Smithy.RepairPrice("chain_mail~2"));
        Assert.True(Smithy.Repair(content, hero, "chain_mail~2").Done);
        Assert.Equal((1, 3), (hero.Gold, hero.Stats(content).Armor));
        Assert.Contains("chain_mail", hero.Equipped);
    }
}
