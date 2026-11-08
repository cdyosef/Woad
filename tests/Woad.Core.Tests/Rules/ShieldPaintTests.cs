using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Shield Paint: a crest painted on the shield in your off hand, kept between runs; heroes beside its bearer rally to it.</summary>
public sealed class ShieldPaintTests
{
    private const string Room = """
        .........
        .........
        .........
        .........
        ..U12....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void ItPaintsTheShieldInYourOffHandAndIsUsedUp()
    {
        GameSession session = TestRun.Start(Room, cards: ["shield", "shield_paint"]);
        Hero hero = session.HeroInSeat();
        CardInstance shield = hero.Equipment[EquipSlot.OffHand];
        CardInstance paint = hero.Card("shield_paint");

        Assert.Equal(new ShieldPainted(hero.Id, shield.Id), session.Do(new UseCard(hero.Id, paint.Id)).SingleOf<ShieldPainted>());
        Assert.True(shield.Painted);
        Assert.Null(hero.FindCard(paint.Id));
        Assert.Equal("shield*", shield.Key);
    }

    [Fact]
    public void WithoutAnUnpaintedShieldInHandTheresNothingToPaint()
    {
        GameSession session = TestRun.Start(Room, cards: ["shield_paint"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("off hand", session.Refuse(new UseCard(hero.Id, hero.Card("shield_paint").Id)));
    }

    [Fact]
    public void HeroesBesideABearerOfAPaintedShieldDefendAtPlusOne()
    {
        GameSession session = TestRun.Start(Room, new ScriptedDice(3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3), heroes: 2, cards: ["shield"]);
        Hero hero = session.HeroInSeat(0);
        Hero bearer = session.HeroInSeat(1);
        bearer.Equipment[EquipSlot.OffHand].Painted = true;

        DefenseRolled defense = session.EndRound().OfType<DefenseRolled>().First(rolled => rolled.DefenderId == hero.Id);

        Assert.Equal(hero.EffectiveStats.Armor + 1, defense.Armor);
    }

    [Fact]
    public void APaintedCardsKeyKeepsItsPaintAndWear()
    {
        string key = CardKey.Of("tower_shield", 1, painted: true);

        Assert.Equal("tower_shield~1*", key);
        Assert.Equal(("tower_shield", 1, true), (CardKey.Id(key), CardKey.Wear(key), CardKey.Painted(key)));
        Assert.False(CardKey.Painted("tower_shield~1"));
    }
}
