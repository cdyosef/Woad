using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Tavern;
using Woad.Core.Tests.Support;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Tavern;

public class TavernTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();
    private static readonly TavernDefinition Inn = TavernDefinition.LoadDefault();
    private static readonly WoarldMap Map = WoarldMap.LoadDefault();

    private static Character Hero(string name, int gold, bool companion = false)
    {
        Character hero = Character.Create(name, "moss", StatAllocation.None, Content, DateTimeOffset.UnixEpoch, companion);
        hero.EarnGold(gold);
        return hero;
    }

    private static TavernNpc Npc(string id) => Inn.Npcs.Single(npc => npc.Id == id);

    private static MenuItem Item(string id) => Inn.Menu.Single(item => item.Id == id);

    [Fact]
    public void TheWaylayInnHasItsFolkAndStandsOnTheMap()
    {
        Assert.Equal("The Waylay Inn", Inn.Name);
        Assert.Contains(Map.Places, place => place.Id == Inn.PlaceId);
        Assert.Equal([NpcRole.Bartender, NpcRole.Merchant, NpcRole.Recruiter, NpcRole.Innkeeper, NpcRole.Gambler, NpcRole.Patron], Inn.Npcs.Select(npc => npc.Role));
        Assert.True(Inn.IsDoor(Inn.Start with { Y = Inn.Start.Y + 1 }), "the party starts just inside the door");
    }

    [Fact]
    public void EveryoneCanBeTalkedTo()
    {
        foreach (TavernNpc npc in Inn.Npcs)
        {
            Assert.True(Inn.TalkingSpot(npc, Inn.Start) is not null, $"{npc.Name} can be reached and talked to");
        }
    }

    [Fact]
    public void TheBartenderServesAcrossTheBar()
    {
        TavernNpc bartender = Npc("bartender");
        var acrossTheBar = new GridPosition(bartender.X, bartender.Y + 2);

        Assert.Equal('B', Inn.TileAt(bartender.Position with { Y = bartender.Y + 1 }));
        Assert.Equal(bartender, Inn.NpcInReach(acrossTheBar));
        Assert.Null(Inn.NpcInReach(Inn.Start));
    }

    [Fact]
    public void ThePartyWalksInALineAndCantWalkThroughFolkOrFurniture()
    {
        var visit = new TavernVisit(Inn, 3);
        Assert.Null(visit.Step(Direction.North));
        Assert.Null(visit.Step(Direction.North));
        Assert.Equal([Inn.Start with { Y = Inn.Start.Y - 1 }, Inn.Start], visit.Trail);

        TavernNpc gambler = Npc("gambler");
        IReadOnlyList<Direction> path = visit.PathTo(Inn.TalkingSpot(gambler, visit.Leader)!.Value)!;
        foreach (Direction step in path)
        {
            Assert.Null(visit.Step(step));
        }

        Assert.Equal(gambler, visit.NpcInReach);
        Assert.Null(visit.PathTo(gambler.Position));
    }

    [Fact]
    public void EatingCostsGoldAndBoostsTheNextRest()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 5);

        Assert.True(TavernServices.Eat(world, ash, Item("hearth_stew")).Done);
        Assert.True(TavernServices.Eat(world, ash, Item("woad_ale")).Done);
        Assert.Equal(1, ash.Gold);
        Assert.Equal(new RestBoost(1, 2), world.Fed(ash.Id));

        ServiceResult broke = TavernServices.Eat(world, ash, Item("hunters_pie"));
        Assert.False(broke.Done);
        Assert.Equal(1, ash.Gold);
    }

    [Fact]
    public void ToGoMealsAreEatenOneAtEachRest()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 20);
        TavernServices.Eat(world, ash, Item("woad_ale"));
        TavernServices.BuyToGo(world, ash, Item("hearth_stew"));
        TavernServices.BuyToGo(world, ash, Item("dragons_breath"));

        Assert.Equal([new RestBoost(1, 2)], world.TakeRestBoosts([ash.Id], Inn));
        Assert.Equal([new RestBoost(2, 0)], world.TakeRestBoosts([ash.Id], Inn));
        Assert.Equal([default(RestBoost)], world.TakeRestBoosts([ash.Id], Inn));
    }

    [Fact]
    public void AHeroCarriesAtMostThreeMealsToGo()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 20);
        for (int i = 0; i < RaidWorld.MaxProvisions; i++)
        {
            Assert.True(TavernServices.BuyToGo(world, ash, Item("woad_ale")).Done);
        }

        Assert.False(TavernServices.BuyToGo(world, ash, Item("woad_ale")).Done);
        Assert.Equal(20 - RaidWorld.MaxProvisions, ash.Gold);
    }

    [Fact]
    public void ARoundForThePartyBuildsBonds()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 10), bram = Hero("Bram", 0), cole = Hero("Cole", 0);

        ServiceResult round = TavernServices.RoundForTheParty(world, Inn, ash, [ash, bram, cole]);

        Assert.True(round.Done, round.Message);
        Assert.Equal(8, ash.Gold);
        Assert.Equal(1, world.Bond(ash.Id, bram.Id));
        Assert.Equal(1, world.Bond(cole.Id, ash.Id));
        Assert.Equal(0, world.Bond(bram.Id, cole.Id));
        Assert.Equal(Item("woad_ale").Boost, world.Fed(bram.Id));
        Assert.Equal(default, world.Fed(ash.Id));
    }

    [Fact]
    public void ARoundForTheBarRaisesReputation()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 25);

        TavernServices.RoundForTheBar(world, Inn, ash);
        TavernServices.RoundForTheBar(world, Inn, ash);

        Assert.Equal(2, world.Reputation(Inn.Id));
        Assert.Equal(5, ash.Gold);
        Assert.False(TavernServices.RoundForTheBar(world, Inn, ash).Done);
    }

    [Fact]
    public void TheMerchantSellsAtTwiceTheWorthAndKeepsWhatHeBuys()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 20);
        int potions = world.Stock(Inn).Count(card => card == "health_potion");

        Assert.True(TavernServices.Buy(world, Inn, Content, ash, "health_potion").Done);
        Assert.Equal(16, ash.Gold);
        Assert.Equal(potions - 1, world.Stock(Inn).Count(card => card == "health_potion"));
        Assert.Equal(2, ash.Belt.Count(card => card == "health_potion"));

        Assert.True(TavernServices.Sell(world, Inn, Content, ash, "wooden_sword").Done);
        Assert.Equal(18, ash.Gold);
        Assert.DoesNotContain("wooden_sword", ash.Equipped);
        Assert.Contains("wooden_sword", world.Stock(Inn));

        Assert.False(TavernServices.Buy(world, Inn, Content, ash, "golden_idol").Done);
        Assert.False(TavernServices.Sell(world, Inn, Content, ash, "golden_idol").Done);
    }

    [Fact]
    public void BonesPaysTheHighRoll()
    {
        Character ash = Hero("Ash", 10);

        Assert.Equal(5, TavernServices.Bones(new ScriptedDice(6, 5, 2, 2), ash, 5).Winnings);
        Assert.Equal(15, ash.Gold);
        Assert.Equal(-5, TavernServices.Bones(new ScriptedDice(1, 1, 3, 3), ash, 5).Winnings);
        Assert.Equal(0, TavernServices.Bones(new ScriptedDice(4, 3, 5, 2), ash, 5).Winnings);
        Assert.Equal(10, ash.Gold);
        Assert.Equal(0, TavernServices.Bones(new ScriptedDice(), ash, 11).Winnings);
        Assert.Equal(0, TavernServices.Bones(new ScriptedDice(), ash, TavernServices.MaxWager + 1).Winnings);
    }

    [Fact]
    public void BetterReputationBringsBetterRecruits()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        IReadOnlyList<RecruitOffer> plain = TavernServices.Recruits(world, Inn);
        Assert.Equal(TavernServices.RecruitsOffered, plain.Count);
        Assert.All(plain, offer => Assert.Equal((1, 10), (offer.Level, offer.Price)));
        Assert.Equal(plain, TavernServices.Recruits(world, Inn));

        world.RaiseReputation(Inn.Id, 6);
        IReadOnlyList<RecruitOffer> famous = TavernServices.Recruits(world, Inn);
        Assert.All(famous, offer => Assert.Equal(3, offer.Level));
        Assert.All(famous, offer => Assert.Contains("knights_sword", offer.Gear));
    }

    [Fact]
    public void HiringMakesAnOutfittedCompanion()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        world.RaiseReputation(Inn.Id, 6);
        Character ash = Hero("Ash", 40);
        RecruitOffer offer = TavernServices.Recruits(world, Inn)[0];

        (ServiceResult result, Character? recruit) = TavernServices.Hire(ash, offer, Content, DateTimeOffset.UnixEpoch);

        Assert.True(result.Done, result.Message);
        Assert.Equal(40 - offer.Price, ash.Gold);
        Assert.True(recruit!.IsCompanion);
        Assert.Equal(3, recruit.Level);
        Assert.Contains("knights_sword", recruit.Equipped);
        Assert.DoesNotContain("wooden_sword", recruit.Equipped);
        Assert.Equal(offer.Appearance, recruit.Appearance);
        Assert.Null(TavernServices.Hire(Hero("Broke", 0), offer, Content, DateTimeOffset.UnixEpoch).Recruit);
    }

    [Fact]
    public void RumorsComeInTurnAndSomeClearTheClouds()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        TavernNpc bartender = Npc("bartender");
        var temple = new GridPosition(33, 26);
        Assert.False(world.Journey.IsRevealed(temple));

        Rumor first = TavernServices.NextRumor(world, bartender)!;

        Assert.Equal(bartender.Rumors[0], first);
        Assert.True(world.Journey.IsRevealed(temple), "the rumor of the temple clears the clouds over it");
        Assert.Equal(bartender.Rumors[1], TavernServices.NextRumor(world, bartender));
        TavernServices.NextRumor(world, bartender);
        Assert.Equal(bartender.Rumors[0], TavernServices.NextRumor(world, bartender));
    }

    [Fact]
    public void TheWorldSavesAndLoads()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 50), bram = Hero("Bram", 0);
        TavernServices.RoundForTheParty(world, Inn, ash, [ash, bram]);
        TavernServices.RoundForTheBar(world, Inn, ash);
        TavernServices.BuyToGo(world, ash, Item("hunters_pie"));
        TavernServices.Sell(world, Inn, Content, ash, "buckler");
        TavernServices.NextRumor(world, Npc("gambler"));
        world.Journey.Step(Direction.East);

        RaidWorld loaded = RaidWorld.FromJson(world.ToJson(), Map);

        Assert.Equal(world.ToJson(), loaded.ToJson());
        Assert.Equal(1, loaded.Bond(ash.Id, bram.Id));
        Assert.Equal(1, loaded.Reputation(Inn.Id));
        Assert.Equal(["hunters_pie"], loaded.Provisions(ash.Id));
        Assert.Equal(1, loaded.RumorsHeard("gambler"));
        Assert.Equal(world.Journey.Leader, loaded.Journey.Leader);
    }
}
