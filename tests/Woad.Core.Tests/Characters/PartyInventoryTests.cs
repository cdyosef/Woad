using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Saves;
using Woad.Core.Tavern;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Characters;

public class PartyInventoryTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static Character Morgan() => Character.Create("Morgan", "moss", StatAllocation.None, Content, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ACardCanBeWornPushingOutWhatFillsItsSlot()
    {
        Character morgan = Morgan();
        morgan.GiveCard("great_axe", Content);
        morgan.GiveCard("buckler", Content);
        Assert.Contains("buckler", morgan.Equipped);

        Assert.Null(morgan.Move("great_axe", morgan.Belt.Contains("great_axe") ? CardPlace.Belt : CardPlace.Pack, CardPlace.Worn, Content));

        Assert.Contains("great_axe", morgan.Equipped);
        Assert.DoesNotContain("club", morgan.Equipped);
        Assert.DoesNotContain("buckler", morgan.Equipped);
        Assert.Contains("club", morgan.Backpack);
        Assert.Contains("buckler", morgan.Backpack);
    }

    [Fact]
    public void HandGearAndPotionsGoOnTheBeltWhileThereIsRoom()
    {
        Character morgan = Morgan();
        Assert.Equal(4, morgan.BeltCapacity(Content));
        morgan.GiveCard("health_potion", Content);
        Assert.Null(morgan.Move("club", CardPlace.Worn, CardPlace.Belt, Content));
        Assert.Contains("club", morgan.Belt);
        Assert.DoesNotContain("club", morgan.Equipped);

        // Potions stack in one slot; the belt (torch, potions, lockpicks, club) is now full.
        Assert.Equal(4, morgan.BeltUsed(Content));
        morgan.GiveCard("dagger", Content);
        Assert.Contains("full", morgan.CheckMove("dagger", morgan.Equipped.Contains("dagger") ? CardPlace.Worn : CardPlace.Pack, CardPlace.Belt, Content));
        Assert.Contains("doesn't go on a belt", morgan.CheckMove("basic_buckle", CardPlace.Worn, CardPlace.Belt, Content));
    }

    [Fact]
    public void TakingTheBeltOffPacksItWholeAndWearingItBucklesItAllBackOn()
    {
        Character morgan = Morgan();
        morgan.GiveCard("buckle_horns", Content);
        Assert.Null(morgan.Move("buckle_horns", morgan.Equipped.Contains("buckle_horns") ? CardPlace.Worn : CardPlace.Pack, CardPlace.Worn, Content));
        List<string> belt = [.. morgan.Belt];

        Assert.Null(morgan.Move("basic_belt", CardPlace.Worn, CardPlace.Pack, Content));

        string packed = Assert.Single(morgan.Backpack, key => CardKey.Id(key) == "basic_belt");
        Assert.Empty(morgan.Belt);
        Assert.Equal(["club"], morgan.Equipped);
        Assert.Equal(0, morgan.BeltCapacity(Content));

        Assert.Null(morgan.Move(packed, CardPlace.Pack, CardPlace.Worn, Content));

        Assert.Equal(belt, morgan.Belt);
        Assert.Contains("basic_buckle", morgan.Equipped);
        Assert.Contains("buckle_horns", morgan.Equipped);
    }

    [Fact]
    public void AModNeedsWhatItGoesOn()
    {
        Character morgan = Morgan();
        morgan.Move("basic_buckle", CardPlace.Worn, CardPlace.Pack, Content);
        morgan.GiveCard("buckle_horns", Content);

        Assert.Contains("goes on a buckle", morgan.CheckMove("buckle_horns", CardPlace.Pack, CardPlace.Worn, Content));
        Assert.Contains("can't be worn", morgan.CheckMove("health_potion", CardPlace.Belt, CardPlace.Worn, Content));
    }

    [Fact]
    public void ThePartyChestHoldsCardsAndCoinsForEveryone()
    {
        Character morgan = Morgan(), pip = Character.Create("Pip", "ember", StatAllocation.None, Content, DateTimeOffset.UnixEpoch);
        var chest = new PartyChest();
        morgan.Earn(Money.Gold(3) + 7);

        Assert.Null(chest.Put(morgan, "club", CardPlace.Worn, Content));
        Assert.Null(PartyChest.Transfer(morgan, chest, Money.Gold(2)));
        Assert.NotNull(PartyChest.Transfer(morgan, chest, Money.Gold(2)));

        Assert.Equal(["club"], chest.Cards);
        Assert.Equal((Money.Gold(2), Money.Gold(1) + 7), (chest.Coins, morgan.Coins));
        Assert.Null(chest.Take(pip, "club", Content));
        Assert.Null(PartyChest.Transfer(chest, pip, chest.Coins));
        Assert.Empty(chest.Cards);
        Assert.Equal(Money.Gold(2), pip.Coins);
        Assert.Equal(2, pip.Equipped.Count(key => key == "club") + pip.Backpack.Count(key => key == "club") + pip.Belt.Count(key => key == "club"));
    }

    [Fact]
    public void ThePartyChestBuysIntoItselfSellsFromItselfAndPaysForMending()
    {
        RaidWorld world = RaidWorld.Begin(WoarldMap.LoadDefault());
        TavernDefinition inn = TavernDefinition.LoadDefault();
        Character morgan = Morgan();
        var chest = new PartyChest();
        chest.Earn(Money.Gold(10));
        var shopper = new Shopper(chest, 0, key => chest.Cards.Add(key));

        ServiceResult bought = TavernServices.Buy(world, inn, Content, shopper, "bomb");

        Assert.True(bought.Done, bought.Message);
        Assert.StartsWith("The Party Chest buys", bought.Message);
        Assert.Equal(["bomb"], chest.Cards);
        Assert.Equal(Money.Gold(10) - TavernServices.BuyPrice(Content.GetCard("bomb")), chest.Coins);

        int before = chest.Coins;
        Assert.True(TavernServices.Sell(world, inn, Content, chest, "bomb", () => chest.Cards.Remove("bomb")).Done);
        Assert.Empty(chest.Cards);
        Assert.Equal(before + TavernServices.SellPrice(Content.GetCard("bomb")), chest.Coins);

        morgan.GiveCard("chain_mail~2", Content);
        before = chest.Coins;
        Assert.True(Smithy.Repair(Content, morgan, "chain_mail~2", chest).Done);
        Assert.Equal(before - Smithy.RepairPrice("chain_mail~2"), chest.Coins);
        Assert.Empty(morgan.WornGear);
    }

    [Fact]
    public void ThePartyChestPaysAtTheInn()
    {
        RaidWorld world = RaidWorld.Begin(WoarldMap.LoadDefault());
        TavernDefinition inn = TavernDefinition.LoadDefault();
        Character morgan = Morgan();
        var chest = new PartyChest();
        chest.Earn(Money.Gold(3));

        Assert.True(TavernServices.Eat(world, morgan, inn.Menu[0], chest).Done);
        Assert.True(TavernServices.RoundForTheBar(world, inn, chest).Done);

        Assert.Equal(0, morgan.Coins);
        Assert.Equal(Money.Gold(1) - inn.Menu[0].Price, chest.Coins);
        Assert.False(world.Fed(morgan.Id).IsNone);
    }

    [Fact]
    public void TheSheetsArmorCountsWearAgainstItAndShiningLightForIt()
    {
        Character morgan = Morgan();
        morgan.GiveCard("chain_mail~1", Content);
        Hero hero = Hero.Preview(morgan, Content, null, out _);

        Assert.Equal((2, 0, 1), (hero.ArmorTotal, hero.ArmorBoost, hero.ArmorWear));
        hero.ShiningRounds = 3;
        hero.ShiningDefense = 2;
        Assert.Equal((4, 2, 1), (hero.ArmorTotal, hero.ArmorBoost, hero.ArmorWear));
        Assert.Same(morgan.Allocation, hero.Allocation);
    }

    [Fact]
    public void ThePartyChestIsSavedWithTheRaid()
    {
        var raid = new RaidSave { Name = "Morgan's Raid", Party = [Guid.NewGuid()] };
        raid.Chest.Cards.Add("great_axe");
        raid.Chest.Earn(345);

        RaidSave loaded = RaidSave.FromJson(raid.ToJson());

        Assert.Equal(["great_axe"], loaded.Chest.Cards);
        Assert.Equal(345, loaded.Chest.Coins);
        Assert.Empty(RaidSave.FromJson(raid.ToJson().Replace("\"chest\"", "\"oldChest\"")).Chest.Cards);
    }
}
