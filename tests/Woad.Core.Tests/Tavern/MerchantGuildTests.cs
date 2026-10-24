using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Tavern;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Tavern;

/// <summary>The Merchant Guild out in the Woarld: its levels and wares, and its wandering merchants.</summary>
public class MerchantGuildTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();
    private static readonly TavernDefinition Inn = TavernDefinition.LoadDefault();
    private static readonly WoarldMap Map = WoarldMap.LoadDefault();

    private static Character Hero(string name, int gold)
    {
        Character hero = Character.Create(name, "moss", StatAllocation.None, Content, DateTimeOffset.UnixEpoch, false);
        hero.EarnGold(gold);
        return hero;
    }

    [Fact]
    public void BusinessAndRescuesRaiseTheGuildAndEachLevelBringsEveryMerchantAWare()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        int stock = world.Stock(Inn).Count;
        Assert.Equal(1, world.GuildLevel);

        IReadOnlyList<string> wares = world.SpendWithMerchants(RaidWorld.GuildGoldLevels[0], Content);
        Assert.Equal(2, world.GuildLevel);
        Assert.Single(wares);
        Assert.Equal(stock + 1, world.Stock(Inn).Count);

        world.RescueMerchants([new RememberedMerchant("Silas", null, [], 1)], Content);
        Assert.Equal(3, world.GuildLevel);
        Assert.Equal(stock + 2, world.Stock(Inn).Count);
        Assert.Equal(["Silas"], world.RememberedMerchants.Select(merchant => merchant.Name));
    }

    [Fact]
    public void WanderingMerchantsWaitOnOpenGroundNearbyForTheDay()
    {
        RaidWorld world = RaidWorld.Begin(Map);

        IReadOnlyList<WanderingMerchant> today = world.Wanderers(Content);

        Assert.NotEmpty(today);
        Assert.All(today, wanderer =>
        {
            Assert.True(Map.IsWalkable(wanderer.Position) && Map.PlaceAt(wanderer.Position) is null);
            Assert.InRange(wanderer.Position.ChebyshevDistanceTo(world.Journey.Leader), 4, 12);
            Assert.InRange(wanderer.Stock.Count, RaidWorld.WandererStock, RaidWorld.WandererStock + 1);
        });
        Assert.Same(today, world.Wanderers(Content));

        world.Journey.PassDay();
        Assert.NotSame(today, world.Wanderers(Content));
    }

    [Fact]
    public void ARescuedMerchantComesBackAndGivesTheirHelpersBetterPrices()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character helper = Hero("Aria", 100), stranger = Hero("Bran", 100);
        world.RescueMerchants([new RememberedMerchant("Silas", helper.Id, [helper.Id], 1)], Content);
        world.Journey.PassDay();

        WanderingMerchant silas = world.Wanderers(Content)[0];
        Assert.Equal("Silas", silas.Name);
        CardDefinition ware = Content.GetCard(silas.Stock[0]);
        Assert.True(TavernServices.WandererPrice(world, silas, ware, helper) < TavernServices.WandererPrice(world, silas, ware, stranger));

        ServiceResult bought = TavernServices.BuyFromWanderer(world, silas, Content, helper, ware.Id);
        Assert.True(bought.Done, bought.Message);
        Assert.Contains(ware.Id, helper.Backpack.Concat(helper.Equipped).Concat(helper.Belt));
    }

    [Fact]
    public void TheWorldRemembersItsWanderersAndTheGuild()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        world.SpendWithMerchants(60, Content);
        IReadOnlyList<WanderingMerchant> today = world.Wanderers(Content);

        RaidWorld loaded = RaidWorld.FromJson(world.ToJson(), Map);

        Assert.Equal((world.GuildLevel, world.GoldSpentWithMerchants), (loaded.GuildLevel, loaded.GoldSpentWithMerchants));
        Assert.Equal(today.Select(wanderer => (wanderer.Name, wanderer.Position)), loaded.Wanderers(Content).Select(wanderer => (wanderer.Name, wanderer.Position)));
    }
}
