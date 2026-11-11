using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Tavern;
using Woad.Core.Tests.Support;
using Woad.Core.Woarld;

namespace Woad.Core.Tests.Tavern;

public class TavernTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();
    private static readonly TavernDefinition Inn = TavernDefinition.LoadDefault();
    private static readonly WoarldMap Map = WoarldMap.LoadDefault();

    /// <summary>A character with a purse of <paramref name="coins"/> copper (see <see cref="Money"/>).</summary>
    private static Character Hero(string name, int coins, bool companion = false)
    {
        Character hero = Character.Create(name, "moss", StatAllocation.None, Content, DateTimeOffset.UnixEpoch, companion);
        hero.Earn(coins);
        return hero;
    }

    private static TavernNpc Npc(string id) => Inn.Npcs.Single(npc => npc.Id == id);

    private static MenuItem Item(string id) => Inn.Menu.Single(item => item.Id == id);

    [Fact]
    public void TheWaylayInnHasItsFolkAndStandsOnTheMap()
    {
        Assert.Equal("The Waylay Inn", Inn.Name);
        Assert.Contains(Map.Places, place => place.Id == Inn.PlaceId);
        Assert.Equal([NpcRole.Bartender, NpcRole.Merchant, NpcRole.Recruiter, NpcRole.Innkeeper, NpcRole.Gambler, NpcRole.Cowherder, NpcRole.Priest, NpcRole.Patron], Inn.Npcs.Select(npc => npc.Role));
        Assert.True(Inn.IsDoor(Inn.Start with { Y = Inn.Start.Y + 1 }), "the party starts just inside the door");
    }

    [Fact]
    public void FatherAmbroseSendsThePartyForTheRelicAndRewardsItsReturn()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ana = Hero("Ana", 0), bo = Hero("Bo", 0);

        Assert.True(TavernServices.Relic(world, Inn, Content, ana, [ana, bo]).Done);
        Assert.Equal(QuestStage.Taken, world.Quest(Quests.Relic));
        Assert.Contains("holy_key", ana.Backpack.Concat(ana.Belt));
        Assert.False(TavernServices.Relic(world, Inn, Content, ana, [ana, bo]).Done);

        // Bo brings it home; Ana, talking to him, takes the reward.
        bo.GiveCard("saints_relic", Content);
        int reputation = world.Reputation(Inn.Id);
        Assert.True(TavernServices.Relic(world, Inn, Content, ana, [ana, bo]).Done);

        Assert.Equal(QuestStage.Done, world.Quest(Quests.Relic));
        Assert.DoesNotContain("saints_relic", bo.Backpack.Concat(bo.Belt).Concat(bo.Carried));
        Assert.Equal(Money.Gold(TavernServices.RelicGold), ana.Coins);
        Assert.Equal(TavernServices.RelicHolyWater, ana.Backpack.Concat(ana.Belt).Count(id => id == "holy_water"));
        Assert.Equal(reputation + 2, world.Reputation(Inn.Id));
        Assert.Equal(QuestStage.Done, RaidWorld.FromJson(world.ToJson(), Map).Quest(Quests.Relic));
    }

    [Fact]
    public void AfterTheRelicFatherAmbroseSendsThePartyToStopTheRitual()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ana = Hero("Ana", 0);
        Assert.False(TavernServices.Ritual(world, Inn, Content, ana).Done);

        world.SetQuest(Quests.Relic, QuestStage.Done);
        Assert.True(TavernServices.Ritual(world, Inn, Content, ana).Done);
        Assert.Equal((Quests.RitualRounds, false), world.GraveyardOmens());

        world.SettleRitual(stopped: true, opened: false);
        Assert.Equal(QuestStage.Achieved, world.Quest(Quests.Ritual));
        Assert.True(TavernServices.Ritual(world, Inn, Content, ana).Done);
        Assert.Equal((QuestStage.Done, Money.Gold(TavernServices.RitualGold)), (world.Quest(Quests.Ritual), ana.Coins));
        Assert.Equal((0, false), world.GraveyardOmens());
    }

    [Fact]
    public void LetTheRitualSucceedAndTheGravesStayCursed()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        world.SetQuest(Quests.Ritual, QuestStage.Taken);

        world.SettleRitual(stopped: false, opened: true);

        Assert.Equal(QuestStage.Failed, world.Quest(Quests.Ritual));
        Assert.Equal((0, true), RaidWorld.FromJson(world.ToJson(), Map).GraveyardOmens());
    }

    [Fact]
    public void QuestThingsCantBeSold()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ana = Hero("Ana", 0);
        ana.GiveCard("holy_key", Content);

        Assert.False(TavernServices.Sell(world, Inn, Content, ana, "holy_key").Done);
        Assert.Contains("holy_key", ana.Backpack.Concat(ana.Belt));
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
    public void EatingCostsCoinAndBoostsTheNextRest()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", Money.Silver(2) + 5);

        // A stew is a silver and a half, an ale five copper.
        Assert.True(TavernServices.Eat(world, ash, Item("hearth_stew")).Done);
        Assert.True(TavernServices.Eat(world, ash, Item("woad_ale")).Done);
        Assert.Equal(5, ash.Coins);
        Assert.Equal(new RestBoost(1, 2), world.Fed(ash.Id));

        ServiceResult broke = TavernServices.Eat(world, ash, Item("hunters_pie"));
        Assert.False(broke.Done);
        Assert.Equal(5, ash.Coins);
    }

    [Fact]
    public void ToGoMealsAreEatenOneAtEachRest()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", Money.Gold(2));
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
        Character ash = Hero("Ash", Money.Gold(2));
        for (int i = 0; i < RaidWorld.MaxProvisions; i++)
        {
            Assert.True(TavernServices.BuyToGo(world, ash, Item("woad_ale")).Done);
        }

        Assert.False(TavernServices.BuyToGo(world, ash, Item("woad_ale")).Done);
        Assert.Equal(Money.Gold(2) - RaidWorld.MaxProvisions * Item("woad_ale").Price, ash.Coins);
    }

    [Fact]
    public void ARoundForThePartyBuildsBonds()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", Money.Gold(1)), bram = Hero("Bram", 0), cole = Hero("Cole", 0);

        ServiceResult round = TavernServices.RoundForTheParty(world, Inn, ash, [ash, bram, cole]);

        Assert.True(round.Done, round.Message);
        Assert.Equal(Money.Silver(5), ash.Coins);
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
        Character ash = Hero("Ash", Money.Gold(5));

        TavernServices.RoundForTheBar(world, Inn, ash);
        TavernServices.RoundForTheBar(world, Inn, ash);

        Assert.Equal(2, world.Reputation(Inn.Id));
        Assert.Equal(Money.Gold(1), ash.Coins);
        Assert.False(TavernServices.RoundForTheBar(world, Inn, ash).Done);
    }

    [Fact]
    public void TheMerchantSellsAtTwiceTheWorthAndKeepsWhatHeBuys()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", Money.Gold(20));
        int potions = world.Stock(Inn).Count(card => card == "health_potion");

        Assert.True(TavernServices.Buy(world, Inn, Content, ash, "health_potion").Done);
        Assert.Equal(Money.Gold(16), ash.Coins);
        Assert.Equal(potions - 1, world.Stock(Inn).Count(card => card == "health_potion"));
        Assert.Equal(2, ash.Belt.Count(card => card == "health_potion"));

        ash.GiveCard("wooden_sword", Content);
        Assert.True(TavernServices.Sell(world, Inn, Content, ash, "wooden_sword").Done);
        Assert.Equal(Money.Gold(18), ash.Coins);
        Assert.DoesNotContain("wooden_sword", ash.Equipped);
        Assert.Contains("wooden_sword", world.Stock(Inn));

        Assert.False(TavernServices.Buy(world, Inn, Content, ash, "golden_idol").Done);
        Assert.False(TavernServices.Sell(world, Inn, Content, ash, "golden_idol").Done);
    }

    [Fact]
    public void BonesPaysTheHighRoll()
    {
        Character ash = Hero("Ash", Money.Gold(10));

        Assert.Equal(Money.Gold(5), TavernServices.Bones(new ScriptedDice(6, 5, 2, 2), ash, 5).Winnings);
        Assert.Equal(Money.Gold(15), ash.Coins);
        Assert.Equal(-Money.Gold(5), TavernServices.Bones(new ScriptedDice(1, 1, 3, 3), ash, 5).Winnings);
        Assert.Equal(0, TavernServices.Bones(new ScriptedDice(4, 3, 5, 2), ash, 5).Winnings);
        Assert.Equal(Money.Gold(10), ash.Coins);
        Assert.Equal(0, TavernServices.Bones(new ScriptedDice(), ash, 11).Winnings);
        Assert.Equal(0, TavernServices.Bones(new ScriptedDice(), ash, TavernServices.MaxWager + 1).Winnings);
    }

    [Fact]
    public void BetterReputationBringsBetterRecruits()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        IReadOnlyList<RecruitOffer> plain = TavernServices.Recruits(world, Inn);
        Assert.Equal(TavernServices.RecruitsOffered, plain.Count);
        Assert.All(plain, offer => Assert.Equal((1, Money.Gold(10)), (offer.Level, offer.Price)));
        Assert.Equal(plain, TavernServices.Recruits(world, Inn));

        world.RaiseReputation(Inn.Id, 6);
        IReadOnlyList<RecruitOffer> famous = TavernServices.Recruits(world, Inn);
        Assert.All(famous, offer => Assert.Equal(3, offer.Level));
        Assert.All(famous, offer => Assert.Contains("knights_sword", offer.Gear));
    }

    [Fact]
    public void AHiredCompanionIsPaidAfterEveryRunWinOrLose()
    {
        Character ash = Hero("Ash", Money.Gold(25));
        (_, Character? clod) = TavernServices.Hire(ash, TavernServices.Clod, Content, DateTimeOffset.UnixEpoch);
        Assert.Equal((Money.Gold(25), Money.Gold(10)), (ash.Coins, clod!.Wage));

        IReadOnlyList<string> lost = TavernServices.SettleRun([(ash, null), (clod, ash)], won: false, world: null).News;
        Assert.Equal((Money.Gold(15), Money.Gold(10)), (ash.Coins, clod.Coins));
        Assert.Contains("pays Clod Hopper", Assert.Single(lost));

        TavernServices.SettleRun([(ash, null), (clod, ash)], won: true, world: null);
        // A purse run short pays what it holds.
        RunSettlement shortOf = TavernServices.SettleRun([(ash, null), (clod, ash)], won: false, world: null);
        Assert.Equal((0, Money.Gold(25)), (ash.Coins, clod.Coins));
        Assert.Contains("can only pay", shortOf.News[0]);
        Assert.Equal((clod, ash), Assert.Single(shortOf.Unpaid));
    }

    [Fact]
    public void APartyThatWinsARunTogetherGrowsCloser()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 0), bryn = Hero("Bryn", 0), cole = Hero("Cole", 0, companion: true);

        TavernServices.SettleRun([(ash, null), (bryn, null), (cole, ash)], won: false, world);
        Assert.Equal(0, world.Bond(ash.Id, bryn.Id));

        IReadOnlyList<string> news = TavernServices.SettleRun([(ash, null), (bryn, null), (cole, ash)], won: true, world).News;

        Assert.Equal((1, 1, 1), (world.Bond(ash.Id, bryn.Id), world.Bond(ash.Id, cole.Id), world.Bond(bryn.Id, cole.Id)));
        Assert.Contains(news, line => line.Contains("grows closer"));
    }

    [Fact]
    public void AnUnpaidCompanionGoesToTheBestOfferGoldAndBondTogether()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 0), bryn = Hero("Bryn", Money.Gold(30)), dara = Hero("Dara", Money.Gold(30)), cole = Hero("Cole", 0, companion: true);
        cole.SetWage(Money.Gold(10));
        world.RaiseBond(cole.Id, dara.Id, 3);
        Assert.Equal([bryn, dara], TavernServices.Bidders(ash, [ash, cole, bryn, dara], ash.Id));

        // 12 gold beats 10 gold, but not 10 gold and a bond of 3; an offer over the purse doesn't count.
        AuctionResult result = TavernServices.CloseAuction(cole, ash, [(bryn, Money.Gold(12)), (dara, Money.Gold(10)), (ash, Money.Gold(99))], [ash, cole, bryn, dara], world);

        Assert.Equal((AuctionOutcome.Hired, dara), (result.Outcome, result.NewLeader));
        Assert.Equal((Money.Gold(10), false), (cole.Wage, cole.Leaving));
    }

    [Fact]
    public void WithNoOfferABondKeepsThemAndWithoutOneTheyMeanToLeave()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        Character ash = Hero("Ash", 0), bryn = Hero("Bryn", Money.Gold(30)), cole = Hero("Cole", 0, companion: true);
        cole.SetWage(Money.Gold(10));

        Assert.Equal(AuctionOutcome.Leaving, TavernServices.CloseAuction(cole, ash, [(bryn, 0)], [ash, cole, bryn], world).Outcome);
        Assert.True(cole.Leaving);

        world.RaiseBond(cole.Id, bryn.Id);
        Assert.Equal(AuctionOutcome.StaysForFriendship, TavernServices.CloseAuction(cole, ash, [], [ash, cole, bryn], world).Outcome);
        Assert.False(cole.Leaving);
    }

    [Fact]
    public void HiringMakesAnOutfittedCompanion()
    {
        RaidWorld world = RaidWorld.Begin(Map);
        world.RaiseReputation(Inn.Id, 6);
        Character ash = Hero("Ash", Money.Gold(40));
        RecruitOffer offer = TavernServices.Recruits(world, Inn)[0];

        (ServiceResult result, Character? recruit) = TavernServices.Hire(ash, offer, Content, DateTimeOffset.UnixEpoch);

        Assert.True(result.Done, result.Message);
        Assert.Equal(Money.Gold(40), ash.Coins);
        Assert.Equal(offer.Price, recruit!.Wage);
        Assert.True(recruit!.IsCompanion);
        Assert.Equal(3, recruit.Level);
        Assert.Contains("knights_sword", recruit.Equipped);
        Assert.DoesNotContain("wooden_sword", recruit.Equipped);
        Assert.Equal(offer.Appearance, recruit.Appearance);
        // Nothing down: even an empty purse can hire (the wage comes due after the run).
        Assert.NotNull(TavernServices.Hire(Hero("Broke", 0), offer, Content, DateTimeOffset.UnixEpoch).Recruit);
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
        Character ash = Hero("Ash", Money.Gold(50)), bram = Hero("Bram", 0);
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
