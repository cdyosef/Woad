using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Rock Bag is a place of its own on the belt, for Rocks only: they never take one of the belt's own slots.</summary>
public sealed class RockBagPlaceTests
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

    private static CardInstance Rocks(GameSession session, int count) =>
        new(session.State.NextId(), session.Content.GetCard("t_rock"), broughtInByHeroId: null, count);

    [Fact]
    public void WithTheBeltFullRocksStillGoIntoTheBagAPlaceOfItsOwn()
    {
        // A belt of 4 filled with four things of its own, and a Rock Bag on it.
        GameSession session = TestRun.Start(Quiet, cards: ["t_rockbag", "potion", "drops", "holy", "bomb"]);
        Hero hero = session.HeroInSeat();
        Assert.Equal((4, 4, 5), (hero.BeltUsed, hero.BeltCapacity, hero.BeltPlaces));

        session.RoomById().AddLoot(hero.Position, [Rocks(session, 12)]);
        session.Do(new PickUpLoot(hero.Id));

        Assert.Equal(12, hero.BagStack(session.Content.GetCard("t_rock"))!.Count);
        Assert.Equal(4, hero.BeltUsed);
    }

    [Fact]
    public void TheBagsPlaceIsForRocksOnly()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_rockbag", "potion", "drops", "holy", "bomb", "scroll"]);
        Hero hero = session.HeroInSeat();
        CardInstance scroll = hero.Card("scroll");
        Assert.Contains(scroll, hero.Backpack);

        Assert.Contains("belt is full", session.Refuse(new BeltCard(hero.Id, scroll.Id)));
    }

    [Fact]
    public void WithoutABagRocksTakeABeltSlotLikeAnythingElse()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["potion", "drops", "holy"]);
        Hero hero = session.HeroInSeat();
        CardInstance rocks = Rocks(session, 3);
        session.RoomById().AddLoot(hero.Position, [rocks]);
        session.Do(new PickUpLoot(hero.Id));
        session.Do(new BeltCard(hero.Id, hero.AllCards.First(card => card.Definition.Id == "t_rock").Id));

        Assert.Equal((4, 4), (hero.BeltUsed, hero.BeltPlaces));
    }

    [Fact]
    public void RocksHandedOverGoStraightIntoTheRockBag()
    {
        GameSession session = TestRun.Start(Quiet.Replace("....1....", "....12..."), heroes: 2, cards: ["t_rockbag"]);
        Hero giver = session.HeroInSeat(0), taker = session.HeroInSeat(1);
        CardInstance rocks = Rocks(session, 6);
        giver.AddToBackpack(rocks);
        Assert.Null(taker.BagStack(rocks.Definition));

        session.Do(new GiveCard(giver.Id, taker.Id, rocks.Id, 4));

        Assert.Equal(4, taker.BagStack(rocks.Definition)!.Count);
        Assert.DoesNotContain(taker.Backpack, card => card.Definition.Id == "t_rock");
    }

    [Fact]
    public void RocksPutAwayFromAHandGoBackIntoTheBagEvenInAFight()
    {
        GameSession session = TestRun.Start(Quiet.Replace("....1....", "....1d..."), cards: ["t_rockbag"]);
        Hero hero = session.HeroInSeat();
        session.RoomById().AddLoot(hero.Position, [Rocks(session, 12)]);
        session.Do(new PickUpLoot(hero.Id));
        CardDefinition rock = session.Content.GetCard("t_rock");
        session.Do(new EquipCard(hero.Id, hero.BagStack(rock)!.Id, EquipSlot.OffHand));
        Assert.Equal(2, hero.BagStack(rock)!.Count);
        Assert.True(session.InCombat);

        session.Do(new StowCard(hero.Id, hero.Equipment[EquipSlot.OffHand].Id));

        Assert.Equal(12, hero.BagStack(rock)!.Count);
        Assert.False(hero.Equipment.ContainsKey(EquipSlot.OffHand));
    }

    [Fact]
    public void AFullBagCantComeOffABeltWithNoRoomForItsRocks()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_rockbag", "potion", "drops", "holy", "bomb"]);
        Hero hero = session.HeroInSeat();
        session.RoomById().AddLoot(hero.Position, [Rocks(session, 5)]);
        session.Do(new PickUpLoot(hero.Id));
        CardInstance bag = hero.Mods.Single(mod => mod.Definition.Id == "t_rockbag");

        Assert.Contains("no room for what's in", session.Refuse(new StowCard(hero.Id, bag.Id)));
    }
}
