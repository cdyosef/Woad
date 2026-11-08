using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>
/// Hand gear swapped out goes on the belt, so it's still to hand when a fight starts: in a swap it takes the place the
/// new card left; it falls back to the bag only out of a fight and with the belt full. In a fight, a weapon picked up
/// goes on the belt too.
/// </summary>
public sealed class SwapToBeltTests
{
    private const string Quiet = """
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

    private const string Fight = """
        .........
        .........
        .........
        .........
        ....1d...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void DrawingABeltWeaponOutOfAFightPutsTheOneInHandOnTheBeltInItsPlace()
    {
        // A full belt of 4: the spare big sword, and three potions.
        GameSession session = TestRun.Start(Quiet, cards: ["sword", "big_sword", "potion", "drops", "holy"]);
        Hero hero = session.HeroInSeat();
        CardInstance sword = hero.Card("sword"), bigSword = hero.Card("big_sword");
        Assert.Contains(bigSword, hero.Belt);
        Assert.Equal(hero.BeltCapacity, hero.Belt.Count);
        Assert.False(session.InCombat);

        session.Do(new EquipCard(hero.Id, bigSword.Id));

        Assert.Same(bigSword, hero.Equipment[EquipSlot.MainHand]);
        Assert.Contains(sword, hero.Belt);
        Assert.DoesNotContain(sword, hero.Backpack);
    }

    [Fact]
    public void SwappedOutBeforeAFightTheOldWeaponCanStillBeDrawnInIt()
    {
        GameSession session = TestRun.Start(Fight, cards: ["sword", "big_sword"]);
        Hero hero = session.HeroInSeat();
        CardInstance sword = hero.Card("sword"), bigSword = hero.Card("big_sword");
        Assert.True(session.InCombat);

        // In the fight, the big sword is drawn and the sword goes on the belt; then back again.
        session.Do(new EquipCard(hero.Id, bigSword.Id));
        Assert.Contains(sword, hero.Belt);
        session.EndRound();
        Assert.Null(session.CheckEquip(sword.Id));
        session.Do(new EquipCard(hero.Id, sword.Id));
        Assert.Contains(bigSword, hero.Belt);
    }

    [Fact]
    public void OutOfAFightWithTheBeltFullAndNothingComingOffItTheOldWeaponGoesInTheBag()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["sword", "potion", "drops", "holy", "bomb", "big_sword"]);
        Hero hero = session.HeroInSeat();
        CardInstance sword = hero.Card("sword"), bigSword = hero.Card("big_sword");
        Assert.Equal(hero.BeltCapacity, hero.Belt.Count);
        Assert.Contains(bigSword, hero.Backpack);

        session.Do(new EquipCard(hero.Id, bigSword.Id));

        Assert.Contains(sword, hero.Backpack);
    }

    [Fact]
    public void InAFightAWeaponPickedUpGoesOnTheBelt()
    {
        GameSession session = TestRun.Start(Fight, cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        var found = new CardInstance(session.State.NextId(), session.Content.GetCard("big_sword"), broughtInByHeroId: null);
        session.RoomById().AddLoot(hero.Position, [found]);

        session.Do(new PickUpLoot(hero.Id));

        Assert.Contains(found, hero.Belt);
        Assert.Null(session.CheckEquip(found.Id));
    }
}
