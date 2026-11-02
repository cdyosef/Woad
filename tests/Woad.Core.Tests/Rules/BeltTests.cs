using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Belts and buckles are cards: the belt says how many cards it holds, mods go on it, and what won't fit anywhere is carried.</summary>
public class BeltTests
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

    /// <summary>A training dummy two tiles from the hero: they're in combat with it.</summary>
    private const string DummyNear = """
        .........
        .........
        ....d....
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    private static CardInstance Card(GameSession session, string id) =>
        new(session.State.NextId(), TestContent.Catalog.GetCard(id), broughtInByHeroId: null);

    [Fact]
    public void TheBeltWornSaysHowManyCardsItHolds()
    {
        GameSession session = TestRun.Start(Open, cards: ["big_belt", "potion", "potion", "bomb", "scroll", "potion", "bomb"]);
        Hero hero = session.HeroInSeat();

        // The Big Belt in the backpack: the belt of 4 is full, and the other two wait in the bag.
        Assert.Equal(4, hero.BeltCapacity);
        Assert.Equal(4, hero.Belt.Count);
        session.Do(new EquipCard(hero.Id, hero.Card("big_belt").Id));

        Assert.Equal(6, hero.BeltCapacity);
        Assert.Contains(hero.Card("belt"), hero.Backpack);
        session.Do(new BeltCard(hero.Id, hero.Backpack.First(card => card.Definition.Id == "potion").Id));
        Assert.Equal(5, hero.Belt.Count);
    }

    [Fact]
    public void ABeltTooSmallForWhatsOnYoursCantGoOn()
    {
        GameSession session = TestRun.Start(Open, cards: ["small_belt", "potion", "bomb"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("holds 1 cards, and you've 2", session.Refuse(new EquipCard(hero.Id, hero.Card("small_belt").Id)));
    }

    [Fact]
    public void ABeltlessHeroCantBeltAnythingAndTheBeltComesOffOnlyEmpty()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion"]);
        Hero hero = session.HeroInSeat();
        CardInstance potion = hero.Card("potion");

        Assert.Contains("cards off your belt first", session.Refuse(new StowCard(hero.Id, hero.Card("belt").Id)));
        session.Do(new StowCard(hero.Id, potion.Id));
        Assert.Contains("Take the buckle off first", session.Refuse(new StowCard(hero.Id, hero.Card("belt").Id)));
        session.Do(new StowCard(hero.Id, hero.Card("buckle").Id));
        session.Do(new StowCard(hero.Id, hero.Card("belt").Id));

        Assert.Equal(0, hero.BeltCapacity);
        Assert.Contains("beltless", session.Refuse(new BeltCard(hero.Id, potion.Id)));
    }

    [Fact]
    public void ModsGoOnTheBeltAndBuckleOnceEachAndAddTheirModifiers()
    {
        GameSession session = TestRun.Start(Open, cards: ["studs", "horns"]);
        Hero hero = session.HeroInSeat();

        // Started with, they went straight on.
        Assert.Equal(["studs", "horns"], hero.Mods.Select(mod => mod.Definition.Id));
        Assert.Equal(TestRun.HeroStats.Armor + 1, hero.EffectiveStats.Armor);
        Assert.Equal(TestRun.HeroStats.Weapon + 1, hero.EffectiveStats.Weapon);

        // A second pair of studs is refused; the buckle can't come off with its horns on.
        CardInstance more = Card(session, "studs");
        session.RoomById().AddLoot(hero.Position, [more]);
        session.Do(new PickUpLoot(hero.Id));
        Assert.Contains("has Studs already", session.Refuse(new EquipCard(hero.Id, more.Id)));
        Assert.Contains("mods off your buckle first", session.Refuse(new StowCard(hero.Id, hero.Card("buckle").Id)));

        // Off comes the horns, then the buckle.
        session.Do(new StowCard(hero.Id, hero.Card("horns").Id));
        session.Do(new StowCard(hero.Id, hero.Card("buckle").Id));
        Assert.Equal(TestRun.HeroStats.Weapon, hero.EffectiveStats.Weapon);
        Assert.Contains("goes on a buckle, and you've none", session.Refuse(new EquipCard(hero.Id, hero.Card("horns").Id)));
    }

    [Fact]
    public void CarryingAnythingMeansNoRumbleAndNoOdHand()
    {
        GameSession session = TestRun.Start(DummyNear, cards: ["sword", "potion"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        Assert.True(session.AttackDiceLeft > 0);

        session.Do(new CarryCard(hero.Id, hero.Card("potion").Id));

        Assert.True(hero.IsCarrying);
        Assert.Equal(0, session.AttackDiceLeft);
        Assert.Equal(0, session.OdHandDiceLeft);
        Assert.Contains("arms are full (Potion)", session.Refuse(new AttackMonster(hero.Id, dummy.Id)));
        Assert.Contains("arms are full", session.Refuse(new Search(hero.Id)));

        // Dropped, the hands are free again.
        session.Do(new DropCard(hero.Id, hero.Card("potion").Id));
        Assert.False(hero.IsCarrying);
        Assert.True(session.AttackDiceLeft > 0);
    }

    [Fact]
    public void TheRelicIsCarryOnlyAndFillsBothArms()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion", "shield"]);
        Hero hero = session.HeroInSeat();
        CardInstance relic = Card(session, "t_relic");
        session.RoomById().AddLoot(hero.Position, [relic]);

        session.Do(new PickUpLoot(hero.Id));

        Assert.Equal(CardLocation.Carried, hero.FindCard(relic.Id)!.Value.Location);
        Assert.Equal(GameRules.CarrySlots, hero.CarrySlotsUsed);
        Assert.Contains("only be carried", session.Refuse(new BeltCard(hero.Id, relic.Id)));
        Assert.Contains("only be carried", session.Refuse(new StowCard(hero.Id, relic.Id)));
        Assert.Contains("no room to carry", session.Refuse(new CarryCard(hero.Id, hero.Card("potion").Id)));
    }

    [Fact]
    public void WithArmsFullTheRelicIsLeftWhereItLies()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion"]);
        Hero hero = session.HeroInSeat();
        session.Do(new CarryCard(hero.Id, hero.Card("potion").Id));
        session.RoomById().AddLoot(hero.Position, [Card(session, "t_relic")]);

        Assert.NotNull(session.Refuse(new PickUpLoot(hero.Id)));
    }

    [Fact]
    public void WhatFindsNoRoomAtTheStartIsCarried()
    {
        string[] cards = [.. Enumerable.Repeat("potion", 4), .. Enumerable.Repeat("sword", GameRules.BackpackCapacity + 2)];
        GameSession session = TestRun.Start(Open, cards: cards);
        Hero hero = session.HeroInSeat();

        // One sword in hand, the rest in the bag, and the one left over in the hero's arms.
        Assert.Equal(GameRules.BackpackCapacity, hero.Backpack.Count);
        Assert.Equal("sword", Assert.Single(hero.Carried).Definition.Id);
    }

    [Fact]
    public void UnbuckledTheBeltComesOffWholeAndBucklesBackOnAsItWas()
    {
        GameSession session = TestRun.Start(Open, cards: ["studs", "horns", "potion", "bomb"]);
        Hero hero = session.HeroInSeat();
        CardInstance belt = hero.Card("belt");

        IReadOnlyList<GameEvent> events = session.Do(new UnbuckleBelt(hero.Id));

        Assert.Equal(new BeltUnbuckled(hero.Id, belt.Id), events.SingleOf<BeltUnbuckled>());
        Assert.Equal(0, hero.BeltCapacity);
        Assert.Empty(hero.Belt);
        Assert.Empty(hero.Mods);
        Assert.False(hero.Equipment.ContainsKey(EquipSlot.Buckle));
        Assert.Equal([belt], hero.Backpack);
        Assert.Equal(["buckle", "studs", "horns"], belt.Packed!.Attached.Select(card => card.Definition.Id));
        Assert.Equal(["potion", "bomb"], belt.Packed.Held.Select(card => card.Definition.Id));
        Assert.Equal("belt{buckle,studs,horns;potion,bomb}", belt.Key);
        Assert.Equal(TestRun.HeroStats.Armor, hero.EffectiveStats.Armor);

        session.Do(new EquipCard(hero.Id, belt.Id));

        Assert.Null(belt.Packed);
        Assert.Equal(4, hero.BeltCapacity);
        Assert.Equal(["potion", "bomb"], hero.Belt.Select(card => card.Definition.Id));
        Assert.Equal(["studs", "horns"], hero.Mods.Select(card => card.Definition.Id));
        Assert.Equal("buckle", hero.Equipment[EquipSlot.Buckle].Definition.Id);
        Assert.Empty(hero.Backpack);
    }

    [Fact]
    public void BucklingOnAPackedBeltUnbucklesTheOneWornIntoTheBag()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion"]);
        Hero hero = session.HeroInSeat();
        CardInstance worn = hero.Card("belt");
        CardInstance packed = CardInstance.FromKey("big_belt{buckle,studs;bomb,bomb}", TestContent.Catalog, session.State.NextId, null);
        session.RoomById().AddLoot(hero.Position, [packed]);
        session.Do(new PickUpLoot(hero.Id));

        session.Do(new EquipCard(hero.Id, packed.Id));

        Assert.Equal(6, hero.BeltCapacity);
        Assert.Equal(["bomb", "bomb"], hero.Belt.Select(card => card.Definition.Id));
        Assert.Equal(["studs"], hero.Mods.Select(card => card.Definition.Id));
        Assert.Equal("belt{buckle;potion}", Assert.Single(hero.Backpack).Key);
        Assert.Same(worn, hero.Backpack[0]);
    }

    [Fact]
    public void UnbucklingWaitsTillTheFightsDone()
    {
        GameSession session = TestRun.Start(DummyNear);
        Hero hero = session.HeroInSeat();

        Assert.Contains("fight's done", session.Refuse(new UnbuckleBelt(hero.Id)));
    }

    [Fact]
    public void TheBeltLookIsChosenFromBeltsTheHeroOwns()
    {
        GameSession session = TestRun.Start(Open, cards: ["big_belt"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("belt you own", session.Refuse(new ChooseBeltLook(hero.Id, "small_belt", "")));
        session.Do(new ChooseBeltLook(hero.Id, "big_belt", ""));

        Assert.Equal("big_belt", hero.Appearance.BeltLook);
        Assert.Equal(["belt", "big_belt"], hero.Looks(CardType.Belt));
    }

    [Fact]
    public void GearCanBeHiddenFromTheFigureAndShownAgain()
    {
        GameSession session = TestRun.Start(Open);
        Hero hero = session.HeroInSeat();

        session.Do(new ShowGear(hero.Id, EquipSlot.Belt, false));
        session.Do(new ShowGear(hero.Id, EquipSlot.Head, false));
        Assert.False(hero.Appearance.Shows(EquipSlot.Belt));
        Assert.Equal("Belt,Head", hero.Appearance.HiddenGear);

        session.Do(new ShowGear(hero.Id, EquipSlot.Belt, true));
        Assert.True(hero.Appearance.Shows(EquipSlot.Belt));
        Assert.False(hero.Appearance.Shows(EquipSlot.Head));
    }

    [Fact]
    public void APackedBeltsKeyKeepsItsStacksAndWear()
    {
        CardInstance belt = CardInstance.FromKey("big_belt~1{buckle;t_bag,t_bag,potion}", TestContent.Catalog, new Counter().Next, null);

        Assert.Equal(1, belt.Wear);
        CardInstance bags = belt.Packed!.Held[0];
        Assert.Equal(("t_bag", 2), (bags.Definition.Id, bags.Count));
        Assert.Equal("big_belt~1{buckle;t_bag,t_bag,potion}", belt.Key);
        Assert.Equal("big_belt", CardKey.Id(belt.Key));
    }

    private sealed class Counter
    {
        private int _next;

        public int Next() => ++_next;
    }
}
