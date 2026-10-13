using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class CardTests
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

    /// <summary>A spearman at (4,2) walled in by pits: it can never move or reach the hero at (4,5).</summary>
    private const string CagedSpearman = """
        .........
        ...~~~...
        ...~g~...
        ...~~~...
        .........
        ....1....
        .........
        .........
        .........
        """;

    [Fact]
    public void StartingCardsAreEquippedThenBeltedThenPacked()
    {
        GameSession session = TestRun.Start(Open, cards: ["sword", "shield", "potion", "bomb", "potion", "coin"]);
        Hero hero = session.HeroInSeat();

        Assert.Equal("sword", hero.Equipment[EquipSlot.MainHand].Definition.Id);
        Assert.Equal("shield", hero.Equipment[EquipSlot.OffHand].Definition.Id);
        Assert.Equal(["potion", "bomb"], hero.Belt.Select(card => card.Definition.Id));
        Assert.Equal(["potion", "coin"], hero.Backpack.Select(card => card.Definition.Id));
        Assert.Equal((1, 1), (hero.EffectiveStats.AttackDice, hero.EffectiveStats.Weapon));
        Assert.Equal((1, 1), (hero.EffectiveStats.DefenseDice, hero.EffectiveStats.Armor));
        Assert.Equal(5, hero.TreasureValue);
    }

    [Fact]
    public void EquippingSwapsTheOldCardIntoTheBackpack()
    {
        GameSession session = TestRun.Start(Open, cards: ["sword", "big_sword"]);
        Hero hero = session.HeroInSeat();
        CardInstance sword = hero.Card("sword");
        CardInstance bigSword = hero.Card("big_sword");

        IReadOnlyList<GameEvent> events = session.Do(new EquipCard(hero.Id, bigSword.Id));

        Assert.Equal<GameEvent>([new CardStowed(hero.Id, sword.Id), new CardEquipped(hero.Id, bigSword.Id)], events);
        Assert.Same(bigSword, hero.Equipment[EquipSlot.MainHand]);
        Assert.Equal([sword], hero.Backpack);
        Assert.Equal(2, hero.EffectiveStats.Weapon);
        Assert.False(session.State.Turn!.HasActed);
    }

    [Fact]
    public void ChangingGearIsFreeWithNoMonstersInTheRoom()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3), cards: ["sword", "shield", "big_sword", "potion", "potion", "potion"]);
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        Assert.True(session.GearIsFree);
        session.Do(new EquipCard(hero.Id, hero.Card("big_sword").Id));
        session.Do(new EquipCard(hero.Id, hero.Card("sword").Id));
        session.Do(new StowCard(hero.Id, hero.Belt[0].Id));
        session.Do(new BeltCard(hero.Id, hero.Backpack[^1].Id));

        Assert.Equal("sword", hero.Equipment[EquipSlot.MainHand].Definition.Id);
        Assert.False(session.State.Turn!.HasActed);
        Assert.Equal(5, session.State.Turn.MovementPoints);
    }

    [Fact]
    public void InAFightChangingGearTakesTheAction()
    {
        GameSession session = TestRun.Start(CagedSpearman, cards: ["sword", "big_sword"]);
        Hero hero = session.HeroInSeat();

        Assert.False(session.GearIsFree);
        session.Do(new EquipCard(hero.Id, hero.Card("big_sword").Id));

        Assert.True(session.State.Turn!.HasActed);
        string reason = session.Refuse(new EquipCard(hero.Id, hero.Card("sword").Id));
        Assert.Contains("in a fight takes your action", reason);
        Assert.Equal(reason, session.CheckEquip(hero.Card("sword").Id));
    }

    [Fact]
    public void TheChecksExplainWhyACardCantBeUsedOrMoved()
    {
        GameSession session = TestRun.Start(CagedSpearman, cards: ["sword", "shield", "potion", "bomb", "boomerang", "coin", "potion"]);
        Hero hero = session.HeroInSeat();
        int potion = hero.Belt[0].Id;

        Assert.Contains("carry it out", session.CheckUse(hero.Card("coin").Id));
        Assert.Contains("full hearts", session.CheckUse(potion));
        Assert.Contains("Equip the Boomerang", session.CheckUse(hero.Card("boomerang").Id));
        Assert.Contains("isn't worn or held", session.CheckEquip(potion));
        Assert.Contains("Only single-use", session.CheckBelt(hero.Card("sword").Id));
        Assert.Contains("belt is full", session.CheckBelt(hero.Backpack[^1].Id));
        Assert.Contains("already in your backpack", session.CheckStow(hero.Card("coin").Id));

        Assert.Null(session.CheckEquip(hero.Card("boomerang").Id));
        Assert.Null(session.CheckStow(potion));
    }

    [Fact]
    public void TwoHandedWeaponsFillBothHands()
    {
        GameSession session = TestRun.Start(Open, cards: ["sword", "shield", "bow"]);
        Hero hero = session.HeroInSeat();
        CardInstance bow = hero.Card("bow");

        session.Do(new EquipCard(hero.Id, bow.Id));

        Assert.Same(bow, hero.Equipment[EquipSlot.MainHand]);
        Assert.Same(bow, hero.Equipment[EquipSlot.OffHand]);
        Assert.Equal(["sword", "shield"], hero.Backpack.Select(card => card.Definition.Id));
        Assert.Equal(4, hero.AttackRange);
        Assert.Equal((1, 0), (hero.EffectiveStats.Weapon, hero.EffectiveStats.Armor));
    }

    [Fact]
    public void SwappingNeedsRoomInTheBackpack()
    {
        GameSession session = TestRun.Start(Open, cards: ["sword", "shield", "bow", "coin", "coin", "coin", "coin", "coin"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("no room", session.Refuse(new EquipCard(hero.Id, hero.Card("bow").Id)));
    }

    [Fact]
    public void OnlySingleUseCardsGoOnTheBelt()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion", "bomb", "potion", "coin"]);
        Hero hero = session.HeroInSeat();
        CardInstance packedPotion = hero.Backpack[0];

        Assert.Contains("single-use", session.Refuse(new BeltCard(hero.Id, hero.Card("coin").Id)));
        Assert.Contains("belt is full", session.Refuse(new BeltCard(hero.Id, packedPotion.Id)));

        session.Do(new DropCard(hero.Id, hero.Card("bomb").Id));
        session.Do(new BeltCard(hero.Id, packedPotion.Id));

        Assert.Equal(["potion", "potion"], hero.Belt.Select(card => card.Definition.Id));
    }

    [Fact]
    public void ABeltItemIsFreeOncePerTurn()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion", "potion"]);
        Hero hero = session.HeroInSeat();
        hero.Hearts = 1;

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Belt[0].Id));

        Assert.Equal(new HeroHealed(hero.Id, 3, 4), events.SingleOf<HeroHealed>());
        Assert.Single(events.OfType<CardConsumed>());
        Assert.False(session.State.Turn!.HasActed);
        Assert.Contains("already used your free belt item", session.Refuse(new UseCard(hero.Id, hero.Belt[0].Id)));
    }

    [Fact]
    public void UsingACardFromTheBackpackTakesTheAction()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion", "potion", "potion"]);
        Hero hero = session.HeroInSeat();
        hero.Hearts = 1;

        session.Do(new UseCard(hero.Id, hero.Backpack[0].Id));

        Assert.True(session.State.Turn!.HasActed);
        Assert.Equal(4, hero.Hearts);
        Assert.Empty(hero.Backpack);
    }

    [Fact]
    public void PotionsHealOnlyUpToMaxHearts()
    {
        GameSession session = TestRun.Start(Open, cards: ["potion", "potion"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("full hearts", session.Refuse(new UseCard(hero.Id, hero.Belt[0].Id)));
        Assert.Empty(session.GetCardTargets(hero.Belt[0].Id));

        hero.Hearts = 3;
        Assert.Equal([hero.Position], session.GetCardTargets(hero.Belt[0].Id));
        Assert.Equal(1, session.Do(new UseCard(hero.Id, hero.Belt[0].Id)).SingleOf<HeroHealed>().Amount);
    }

    [Fact]
    public void BombsHitEveryMonsterInTheBlast()
    {
        GameSession session = TestRun.Start("""
            .........
            ...BB.B..
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 1, 1), cards: ["bomb"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("bomb").Id, new GridPosition(4, 1)));

        // One roll (10) against each brute's own defense: a heart from each in the blast.
        Assert.Equal(10, events.SingleOf<AttackRolled>().Total);
        Assert.Equal([1, 1], events.OfType<Damaged>().Select(damaged => damaged.Amount));
        Assert.Equal([1, 1, 2], session.RoomById().Monsters.Select(monster => monster.Hearts));
        Assert.Empty(hero.Belt);
    }

    [Fact]
    public void BombsNeedAMonsterInTheBlastAndWithinRange()
    {
        GameSession session = TestRun.Start("""
            ....B....
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, cards: ["bomb"]);
        Hero hero = session.HeroInSeat();
        int bomb = hero.Card("bomb").Id;

        Assert.Contains("out of range", session.Refuse(new UseCard(hero.Id, bomb, new GridPosition(4, 0))));
        Assert.Contains("no monsters", session.Refuse(new UseCard(hero.Id, bomb, new GridPosition(4, 6))));
        Assert.Contains("target tile", session.Refuse(new UseCard(hero.Id, bomb)));

        // Only (4,1) is both within 3 steps of the hero and inside a blast that reaches the brute.
        Assert.Equal([new GridPosition(4, 1)], session.GetCardTargets(bomb));
    }

    [Fact]
    public void StunnedMonstersSkipATurnAndShakeItOffBeforeTheNextStun()
    {
        GameSession session = TestRun.Start(CagedSpearman, cards: ["boomerang"]);
        Hero hero = session.HeroInSeat();
        Monster spearman = session.OnlyMonster();
        var useBoomerang = new UseCard(hero.Id, hero.Card("boomerang").Id, spearman.Position);

        Assert.Equal(new MonsterStunned(spearman.Id, 1), session.Do(useBoomerang).SingleOf<MonsterStunned>());
        Assert.True(session.State.Turn!.HasActed);
        Assert.Contains(new MonsterTurnSkipped(spearman.Id), session.EndRound());

        Assert.Contains("shaking off", session.Refuse(useBoomerang));
        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is MonsterTurnSkipped);

        session.Do(useBoomerang);
    }

    [Fact]
    public void BossesCantBeStunned()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....W....
            .........
            .........
            ....1....
            .........
            .........
            .........
            """, cards: ["boomerang"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("can't be stunned", session.Refuse(new UseCard(hero.Id, hero.Card("boomerang").Id, session.OnlyMonster().Position)));
    }

    [Fact]
    public void ReusableCardsMustBeEquippedToUse()
    {
        GameSession session = TestRun.Start(CagedSpearman, cards: ["shield", "boomerang"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("Equip the Boomerang", session.Refuse(new UseCard(hero.Id, hero.Card("boomerang").Id, session.OnlyMonster().Position)));
    }

    [Fact]
    public void ChargedCardsSpendAChargePerUse()
    {
        GameSession session = TestRun.Start(CagedSpearman, cards: ["wand"]);
        Hero hero = session.HeroInSeat();
        CardInstance wand = hero.Card("wand");
        Monster spearman = session.OnlyMonster();

        session.Do(new UseCard(hero.Id, wand.Id, spearman.Position));
        Assert.Equal(1, wand.ChargesRemaining);

        session.EndRound();
        session.EndRound();
        wand.ChargesRemaining = 0;
        Assert.Contains("no charges", session.Refuse(new UseCard(hero.Id, wand.Id, spearman.Position)));
    }

    [Fact]
    public void DroppedCardsCanBePickedUpAgain()
    {
        GameSession session = TestRun.Start(Open, cards: ["coin"]);
        Hero hero = session.HeroInSeat();
        CardInstance coin = hero.Card("coin");

        Assert.Equal(new CardDropped(hero.Id, coin.Id, hero.Position), session.Do(new DropCard(hero.Id, coin.Id)).SingleOf<CardDropped>());
        Assert.False(session.State.Turn!.HasActed);
        Assert.Equal([coin], session.RoomById().LootAt(hero.Position));

        Assert.Equal([coin.Id], session.Do(new PickUpLoot(hero.Id)).SingleOf<LootPickedUp>().CardIds);
        Assert.True(session.State.Turn.HasActed);
        Assert.Equal([coin], hero.Backpack);
        Assert.Empty(session.RoomById().LootAt(hero.Position));
    }

    [Fact]
    public void ChestsDealFromTheThemesLootDeck()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....C....
            ....1....
            .......C.
            .........
            .........
            .........
            """);
        Hero hero = session.HeroInSeat();

        Assert.Contains("Stand next to", session.Refuse(new OpenChest(hero.Id, new GridPosition(7, 5))));
        Assert.Contains("no chest", session.Refuse(new OpenChest(hero.Id, new GridPosition(4, 5))));

        var opened = session.Do(new OpenChest(hero.Id, new GridPosition(4, 3))).SingleOf<ChestOpened>();

        CardInstance loot = Assert.Single(hero.Backpack);
        Assert.Equal(loot.Id, opened.CardId);
        Assert.True(opened.AddedToBackpack);
        Assert.Contains(loot.Definition.Id, new[] { "coin", "gem" });
        Assert.Null(loot.BroughtInByHeroId);

        session.EndRound();
        Assert.Contains("already open", session.Refuse(new OpenChest(hero.Id, new GridPosition(4, 3))));
    }

    [Fact]
    public void LootThatDoesntFitIsLeftOnTheFloor()
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
            """, cards: ["coin", "coin", "coin", "coin", "coin", "coin"]);
        Hero hero = session.HeroInSeat();

        Assert.False(session.Do(new OpenChest(hero.Id, new GridPosition(4, 3))).SingleOf<ChestOpened>().AddedToBackpack);
        Assert.Single(session.RoomById().LootAt(hero.Position));
    }

    [Fact]
    public void TakingOffHeartGearTrimsHearts()
    {
        GameSession session = TestRun.Start(Open, cards: ["cap"]);
        Hero hero = session.HeroInSeat();
        Assert.Equal(5, hero.Hearts);

        session.Do(new StowCard(hero.Id, hero.Card("cap").Id));

        Assert.Equal(4, hero.MaxHearts);
        Assert.Equal(4, hero.Hearts);
    }

    [Fact]
    public void UnknownCardsAreRefused()
    {
        GameSession session = TestRun.Start(Open);
        int heroId = session.HeroInSeat().Id;

        Assert.Contains("don't have that card", session.Refuse(new UseCard(heroId, 999)));
        Assert.Contains("don't have that card", session.Refuse(new EquipCard(heroId, 999)));
        Assert.Contains("don't have that card", session.Refuse(new DropCard(heroId, 999)));
    }
}
