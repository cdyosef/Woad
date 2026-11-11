using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Saves;
using Woad.Core.Woarld;

namespace Woad.Core.Tavern;

/// <summary>What a tavern service did, or why it couldn't (<see cref="Done"/> false: nothing changed).</summary>
public sealed record ServiceResult(bool Done, string Message)
{
    public static ServiceResult Refused(string why) => new(false, why);
}

/// <summary>A game of Bones: both sides roll two dice, high total takes the pot.</summary>
/// <param name="Winnings">Copper the player gained (negative: lost; 0: a tie, wagers back).</param>
public sealed record BonesResult(IReadOnlyList<int> Yours, IReadOnlyList<int> Theirs, int Winnings, string Message);

/// <summary>
/// Who's buying in a shop: the purse that pays, the Guild standing the price goes by, and where what's
/// bought goes (a character's things, or the Party Chest).
/// </summary>
public sealed record Shopper(IPurse Purse, int Standing, Action<string> Receive, Guid? Id = null)
{
    /// <summary>A character shopping for themself.</summary>
    public static Shopper Of(Character character, ContentCatalog content) =>
        new(character, character.GuildStanding, key => character.GiveCard(key, content), character.Id);
}

/// <summary>How a run was settled: what to tell the players, and the companions not paid in full (with who owed them).</summary>
public sealed record RunSettlement(IReadOnlyList<string> News, IReadOnlyList<(Character Companion, Character Owner)> Unpaid);

/// <summary>How the auction for an unpaid companion came out.</summary>
public enum AuctionOutcome
{
    /// <summary>Someone took them on: they follow the winner, at the offer as their wage.</summary>
    Hired,

    /// <summary>No offer, but a bond in the party keeps them.</summary>
    StaysForFriendship,

    /// <summary>No offer and no bond: they start the next run leaving.</summary>
    Leaving,
}

public sealed record AuctionResult(AuctionOutcome Outcome, Character? NewLeader, string Message);

/// <summary>Someone the recruiter can introduce: a companion to hire, already outfitted, for <c>Price</c> copper.</summary>
public sealed record RecruitOffer(string Name, Appearance Appearance, string Portrait, StatAllocation Allocation, IReadOnlyList<string> Gear, int Level, int Price);

/// <summary>The services of a tavern's folk. Every one checks first and changes nothing if refused.</summary>
public static class TavernServices
{
    /// <summary>The most gold a game of Bones can be played for.</summary>
    public const int MaxWager = 20;

    /// <summary>A room upstairs, in copper, for someone leaving the party, for as long as they wait there.</summary>
    public const int RoomPrice = 1;

    /// <summary>Rooms for the whole party for a night, in copper.</summary>
    public static readonly int NightPrice = Money.Gold(1);

    /// <summary>A round of the cheapest drink for the rest of the party, in copper.</summary>
    public static readonly int RoundForThePartyPrice = Money.Silver(5);

    /// <summary>Recruits on offer at a time.</summary>
    public const int RecruitsOffered = 3;

    private static readonly string[] Looks = ["wanderer", "ranger", "rogue", "brute", "barbarian", "druid", "monk", "elf", "brave", "bard", "wild_woman", "elf_lady"];
    private static readonly string[] Colors = ["woad", "ember", "moss", "gilt", "violet", "bone", "blood", "frost"];
    private static readonly string[] Skins = ["fair", "pale", "tan", "olive", "brown", "deep"];
    private static readonly string[] Hair = ["brown", "black", "blonde", "ginger", "auburn", "white", "grey"];

    /// <summary>Eats or drinks at the bar: paid now (by them, or from <paramref name="purse"/>), it boosts the hero's next Rest.</summary>
    public static ServiceResult Eat(RaidWorld world, Character buyer, MenuItem item, IPurse? purse = null)
    {
        purse ??= buyer;
        if (!purse.Spend(item.Price))
        {
            return NotEnough(purse, item.Price);
        }

        world.Feed(buyer.Id, item.Boost);
        return new ServiceResult(true, $"{buyer.Name} {(item.Kind == "drink" ? "drinks" : "eats")} the {item.Name}. {item.Text}");
    }

    /// <summary>Wraps a meal To-Go: carried until a Rest, where it's eaten.</summary>
    public static ServiceResult BuyToGo(RaidWorld world, Character buyer, MenuItem item, IPurse? purse = null)
    {
        purse ??= buyer;
        if (world.Provisions(buyer.Id).Count >= RaidWorld.MaxProvisions)
        {
            return ServiceResult.Refused($"{buyer.Name} can't carry more than {RaidWorld.MaxProvisions} meals To-Go.");
        }

        if (!purse.Spend(item.Price))
        {
            return NotEnough(purse, item.Price);
        }

        world.Pack(buyer.Id, item.Id);
        return new ServiceResult(true, $"{buyer.Name} packs the {item.Name} To-Go, for the next Rest.");
    }

    /// <summary>
    /// A round of the cheapest drink for everyone else in the party: each gets its boost, and the
    /// buyer's bond with each of them grows.
    /// </summary>
    public static ServiceResult RoundForTheParty(RaidWorld world, TavernDefinition tavern, Character buyer, IReadOnlyList<Character> party, IPurse? purse = null)
    {
        purse ??= buyer;
        List<Character> others = [.. party.Where(member => member.Id != buyer.Id)];
        MenuItem? drink = tavern.Menu.Where(item => item.Kind == "drink").MinBy(item => item.Price);
        if (others.Count == 0 || drink is null)
        {
            return ServiceResult.Refused("There's nobody else to buy a round for.");
        }

        int price = RoundForThePartyPrice;
        if (!purse.Spend(price))
        {
            return NotEnough(purse, price);
        }

        foreach (Character other in others)
        {
            world.Feed(other.Id, drink.Boost);
            world.RaiseBond(buyer.Id, other.Id);
        }

        return new ServiceResult(true, $"{buyer.Name} buys {drink.Name} for {string.Join(", ", others.Select(other => other.Name))}. The party grows closer.");
    }

    /// <summary>A round for everyone in the bar: the party's name in the tavern grows.</summary>
    public static ServiceResult RoundForTheBar(RaidWorld world, TavernDefinition tavern, IPurse buyer)
    {
        if (!buyer.Spend(tavern.RoundForTheBar))
        {
            return NotEnough(buyer, tavern.RoundForTheBar);
        }

        world.RaiseReputation(tavern.Id);
        return new ServiceResult(true, $"{Capital(buyer.Name)} buys the whole bar a round! Cheers go up. Reputation in {tavern.Name}: {world.Reputation(tavern.Id)}.");
    }

    /// <summary>What the merchant asks a hero for a card, in copper: twice its worth, less for good standing with the Merchant Guild (more for bad).</summary>
    public static int BuyPrice(CardDefinition card, int guildStanding = 0) => Money.Gold(Rules.GameSession.MerchantPrice(card, guildStanding));

    /// <summary>What the merchant pays for a card, in copper: its worth.</summary>
    public static int SellPrice(CardDefinition card) => Money.Gold(Math.Max(1, card.Value));

    public static ServiceResult Buy(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Character buyer, string cardId) =>
        Buy(world, tavern, content, Shopper.Of(buyer, content), cardId);

    /// <summary>Buys a card from the inn's merchant: paid from the shopper's purse, at their standing, and handed to them.</summary>
    public static ServiceResult Buy(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Shopper buyer, string cardId)
    {
        List<string> stock = world.Stock(tavern);
        if (!stock.Contains(cardId) || content.FindCard(cardId) is not { } card)
        {
            return ServiceResult.Refused("The merchant hasn't got one of those.");
        }

        if (buyer.Standing <= Rules.GameSession.ShunnedStanding)
        {
            return ServiceResult.Refused($"The merchant won't deal with {buyer.Purse.Name}: the Guild knows that name.");
        }

        int price = BuyPrice(card, buyer.Standing);
        if (!buyer.Purse.Spend(price))
        {
            return NotEnough(buyer.Purse, price);
        }

        stock.Remove(cardId);
        buyer.Receive(card.Id);
        IReadOnlyList<string> newWares = world.SpendWithMerchants(price / Money.CopperPerGold, content);
        string news = newWares.Count == 0 ? ""
            : $" The Merchant Guild rises to level {world.GuildLevel}: new wares ({string.Join(", ", newWares.Select(ware => content.GetCard(ware).Name))})!";
        return new ServiceResult(true, $"{Capital(buyer.Purse.Name)} buys the {card.Name} for {Money.Long(price)}.{news}");
    }

    /// <summary>What a wandering merchant asks a hero: their price by the hero's Guild standing, kinder still if they remember the hero's help.</summary>
    public static int WandererPrice(RaidWorld world, WanderingMerchant wanderer, CardDefinition card, Character buyer) =>
        BuyPrice(card, buyer.GuildStanding + world.GratitudeOf(wanderer, buyer.Id));

    /// <summary>What a wandering merchant asks a shopper: by their standing, kinder still if they remember a character's help.</summary>
    public static int WandererPrice(RaidWorld world, WanderingMerchant wanderer, CardDefinition card, Shopper buyer) =>
        BuyPrice(card, buyer.Standing + (buyer.Id is { } id ? world.GratitudeOf(wanderer, id) : 0));

    /// <summary>Buys a card from a wandering merchant on the Woarld Map; the business raises the Guild too.</summary>
    public static ServiceResult BuyFromWanderer(RaidWorld world, WanderingMerchant wanderer, ContentCatalog content, Character buyer, string cardId) =>
        BuyFromWanderer(world, wanderer, content, Shopper.Of(buyer, content), cardId);

    public static ServiceResult BuyFromWanderer(RaidWorld world, WanderingMerchant wanderer, ContentCatalog content, Shopper buyer, string cardId)
    {
        if (!wanderer.Stock.Contains(cardId) || content.FindCard(cardId) is not { } card)
        {
            return ServiceResult.Refused($"{wanderer.Name} hasn't got one of those.");
        }

        if (buyer.Standing <= Rules.GameSession.ShunnedStanding && (buyer.Id is not { } id || world.GratitudeOf(wanderer, id) == 0))
        {
            return ServiceResult.Refused($"{wanderer.Name} won't deal with {buyer.Purse.Name}: the Guild knows that name.");
        }

        int price = WandererPrice(world, wanderer, card, buyer);
        if (!buyer.Purse.Spend(price))
        {
            return NotEnough(buyer.Purse, price);
        }

        wanderer.Stock.Remove(cardId);
        buyer.Receive(card.Id);
        IReadOnlyList<string> newWares = world.SpendWithMerchants(price / Money.CopperPerGold, content);
        string news = newWares.Count == 0 ? "" : $" The Merchant Guild rises to level {world.GuildLevel}!";
        return new ServiceResult(true, $"{Capital(buyer.Purse.Name)} buys the {card.Name} from {wanderer.Name} for {Money.Long(price)}.{news}");
    }

    /// <summary>Sells a card the hero owns to a wandering merchant, who carries it on.</summary>
    public static ServiceResult SellToWanderer(WanderingMerchant wanderer, ContentCatalog content, Character seller, string cardId) =>
        SellToWanderer(wanderer, content, seller, cardId, () => seller.TakeCard(cardId));

    /// <summary>Sells a card to a wandering merchant: <paramref name="take"/> takes it from whoever has it, and their purse is paid.</summary>
    public static ServiceResult SellToWanderer(WanderingMerchant wanderer, ContentCatalog content, IPurse seller, string cardId, Func<bool> take)
    {
        if (content.FindCard(cardId) is not { } card || !take())
        {
            return ServiceResult.Refused($"{Capital(seller.Name)} hasn't got one to sell.");
        }

        seller.Earn(SellPrice(card));
        wanderer.Stock.Add(cardId);
        return new ServiceResult(true, $"{Capital(seller.Name)} sells {wanderer.Name} the {card.Name} for {Money.Long(SellPrice(card))}.");
    }

    /// <summary>Sells a card the hero owns (from the backpack first); the merchant keeps it for sale.</summary>
    public static ServiceResult Sell(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Character seller, string cardId) =>
        Sell(world, tavern, content, seller, cardId, () => seller.TakeCard(cardId));

    /// <summary>Sells a card to the inn's merchant: <paramref name="take"/> takes it from whoever has it, and their purse is paid.</summary>
    public static ServiceResult Sell(RaidWorld world, TavernDefinition tavern, ContentCatalog content, IPurse seller, string cardId, Func<bool> take)
    {
        if (content.FindCard(cardId) is { Quest: true } quest)
        {
            return ServiceResult.Refused($"Nobody here would buy the {quest.Name}. Someone wants it, though.");
        }

        if (content.FindCard(cardId) is not { } card || !take())
        {
            return ServiceResult.Refused($"{Capital(seller.Name)} hasn't got one to sell.");
        }

        seller.Earn(SellPrice(card));
        world.Stock(tavern).Add(cardId);
        return new ServiceResult(true, $"{Capital(seller.Name)} sells the {card.Name} for {Money.Long(SellPrice(card))}.");
    }

    /// <summary>What Father Ambrose gives for the relic: gold, and Holy Water.</summary>
    public const int RelicGold = 60;

    public const int RelicHolyWater = 2;

    /// <summary>
    /// Father Ambrose and his relic: the first time, he gives the Holy Key to the reliquary in the ruined
    /// temple of the Unhallowed Graves (again, if the party has lost it). Brought the relic, he takes it and
    /// pays in gold and Holy Water, and the inn thinks better of the party.
    /// </summary>
    public static ServiceResult Relic(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Character hero, IReadOnlyList<Character> party)
    {
        QuestStage stage = world.Quest(Quests.Relic);
        if (stage == QuestStage.Done)
        {
            return ServiceResult.Refused("\"Saint Wyn rests easy again, thanks to you. But the Graves do not.\"");
        }

        if (party.FirstOrDefault(member => member.Equipped.Concat(member.Belt).Concat(member.Backpack).Concat(member.Carried).Contains("saints_relic")) is { } bearer)
        {
            bearer.TakeCard("saints_relic");
            hero.Earn(Money.Gold(RelicGold));
            for (int i = 0; i < RelicHolyWater; i++)
            {
                hero.GiveCard("holy_water", content);
            }

            world.SetQuest(Quests.Relic, QuestStage.Done);
            world.RaiseReputation(tavern.Id, 2);
            return new ServiceResult(true, $"Father Ambrose takes the relic from {bearer.Name} with shaking hands. \"Bless you. Take this, and this.\" {hero.Name} gets {RelicGold} gold and {RelicHolyWater} Holy Water.");
        }

        if (stage == QuestStage.Taken && party.Any(member => member.Equipped.Concat(member.Belt).Concat(member.Backpack).Contains("holy_key")))
        {
            return ServiceResult.Refused("\"The key opens the reliquary in the ruined temple, at the very heart of the Graves. Go with God, and a torch.\"");
        }

        hero.GiveCard("holy_key", content);
        world.SetQuest(Quests.Relic, QuestStage.Taken);
        return new ServiceResult(true, stage == QuestStage.Taken
            ? $"\"Lost it? Here: the last one I have.\" Father Ambrose gives {hero.Name} another Holy Key."
            : $"\"The Relic of Saint Wyn lies in the ruined temple at the heart of the Unhallowed Graves, locked in its reliquary. Bring it home.\" Father Ambrose gives {hero.Name} the Holy Key.");
    }

    /// <summary>What Father Ambrose gives for stopping the ritual.</summary>
    public const int RitualGold = 150;

    public const int RitualHolyWater = 3;

    /// <summary>
    /// Father Ambrose's second quest, once the relic is home: back to the Graves to stop the cult's ritual
    /// before the portal opens. Stopped, he rewards the party; opened, nothing can be done.
    /// </summary>
    public static ServiceResult Ritual(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Character hero)
    {
        switch (world.Quest(Quests.Ritual))
        {
            case QuestStage.NotTaken when world.Quest(Quests.Relic) != QuestStage.Done:
                return ServiceResult.Refused("\"Bring Saint Wyn home first.\"");
            case QuestStage.NotTaken:
                world.SetQuest(Quests.Ritual, QuestStage.Taken);
                return new ServiceResult(true, $"\"The relic kept them in check. Now the cult gathers in the ruined temple to open a door to the realm of death. Kill the cabal round the circle and the leader at the pulpit, all of them. You'll have {Quests.RitualRounds} rounds once you're through the gate, no more.\"");
            case QuestStage.Taken:
                return ServiceResult.Refused($"\"The cult is waiting in the temple. {Quests.RitualRounds} rounds, from the gate. Go.\"");
            case QuestStage.Achieved:
                hero.Earn(Money.Gold(RitualGold));
                for (int i = 0; i < RitualHolyWater; i++)
                {
                    hero.GiveCard("holy_water", content);
                }

                world.SetQuest(Quests.Ritual, QuestStage.Done);
                world.RaiseReputation(tavern.Id, 3);
                return new ServiceResult(true, $"\"The door stays shut. The Saint watches over you.\" {hero.Name} gets {RitualGold} gold and {RitualHolyWater} Holy Water.");
            case QuestStage.Failed:
                return ServiceResult.Refused("\"The door is open. God help us all. Keep out of the Graves, if you value your soul.\"");
            default:
                return ServiceResult.Refused("\"You've done all that could be asked. More than that.\"");
        }
    }

    /// <summary>How much Cowards' standing earns Craven's respect, and his Cloak.</summary>
    public const int CowardsForTheCloak = 10;

    /// <summary>Craven the Cowherder honors a hero who's fled often enough with the Coward's Cloak (once).</summary>
    public static ServiceResult CowardsCloak(Character hero, ContentCatalog content)
    {
        if (hero.HasCowardsCloak)
        {
            return ServiceResult.Refused($"\"You've had your cloak, {hero.Name}. Wear it with pride. Or, better, run in it.\"");
        }

        if (hero.CowardStanding < CowardsForTheCloak)
        {
            return ServiceResult.Refused($"\"Not yet, {hero.Name}. You've run off {hero.CowardStanding} times; come back when it's {CowardsForTheCloak}.\"");
        }

        hero.GiveCard(content.GetCard("cowards_cloak"), content);
        hero.ReceiveCowardsCloak();
        return new ServiceResult(true, $"Craven drapes the Coward's Cloak over {hero.Name}'s shoulders: \"A sign of respect, from one coward to another.\"");
    }

    /// <summary>A game of Bones for <paramref name="wager"/> gold: two dice each, high total wins; a tie hands the wagers back.</summary>
    /// <param name="purse">Who stakes the wager, if not the player (the Party Chest).</param>
    public static BonesResult Bones(IRandomSource random, Character player, int wager, IPurse? purse = null)
    {
        purse ??= player;
        if (wager < 1 || wager > MaxWager)
        {
            return new BonesResult([], [], 0, $"Wagers run from 1 to {MaxWager} gold.");
        }

        int stake = Money.Gold(wager);
        if (purse.Coins < stake)
        {
            return new BonesResult([], [], 0, NotEnough(purse, stake).Message);
        }

        int[] yours = [random.NextInt(1, 7), random.NextInt(1, 7)];
        int[] theirs = [random.NextInt(1, 7), random.NextInt(1, 7)];
        int difference = yours.Sum() - theirs.Sum();
        if (difference > 0)
        {
            purse.Earn(stake);
            return new BonesResult(yours, theirs, stake, $"{player.Name} rolls {yours[0]} + {yours[1]} against {theirs[0]} + {theirs[1]}, and wins {Money.Long(stake)}!");
        }

        if (difference < 0)
        {
            purse.Spend(stake);
            return new BonesResult(yours, theirs, -stake, $"{player.Name} rolls {yours[0]} + {yours[1]} against {theirs[0]} + {theirs[1]}, and loses {Money.Long(stake)}.");
        }

        return new BonesResult(yours, theirs, 0, $"{yours.Sum()} each: a tie. The wagers go back.");
    }

    /// <summary>
    /// Who the recruiter can introduce today. The better the party's reputation in the tavern, the
    /// better the recruits: more seasoned, and better armed (and dearer).
    /// </summary>
    public static IReadOnlyList<RecruitOffer> Recruits(RaidWorld world, TavernDefinition tavern)
    {
        int reputation = world.Reputation(tavern.Id);
        int tier = reputation >= 6 ? 2 : reputation >= 3 ? 1 : 0;
        // The same folk wait all day; a new day brings new faces.
        var random = new Pcg32((ulong)world.Journey.Day * 7919UL + (ulong)tier, sequence: 11);
        var offers = new List<RecruitOffer>();
        var names = tavern.RecruitNames.ToList();
        for (int i = 0; i < RecruitsOffered && names.Count > 0; i++)
        {
            string name = names[random.NextInt(0, names.Count)];
            names.Remove(name);
            string color = random.Pick(Colors);
            var appearance = new Appearance(random.Pick(Looks), color, random.Pick(Colors), random.Pick(Skins), random.Pick(Hair));
            IReadOnlyList<string> gear = tier switch
            {
                2 => ["knights_sword", "chain_mail", "leather_cap"],
                1 => ["chain_mail"],
                _ => [],
            };
            offers.Add(new RecruitOffer(name, appearance, color, RandomAllocation(random), gear, Level: 1 + tier, Price: Money.Gold(10 + 15 * tier)));
        }

        return offers;
    }

    /// <summary>
    /// Clod Hopper, frog hunter of the bog country, in his hat and waders with his gig and lantern: always at the
    /// recruiter's elbow, and keen. He speaks a little Gurgle, and picks up whatever's lying about.
    /// </summary>
    public static RecruitOffer Clod { get; } = new(
        "Clod Hopper",
        new Appearance("frog_hunter", "moss", "umber", "tan", "brown", CrestField: "bordure", CrestCharge: "frog", CrestColor: "teal", CrestInk: "moss",
            Languages: "gurgle", Persona: Rules.GameSession.ClodPersona),
        "moss",
        new StatAllocation { Hearts = 2, Speed = 1, OdHandDice = 1, Skills = Skill.Search | Skill.Aid },
        ["frog_gig", "bog_waders", "bog_lantern", "sack"],
        Level: 1,
        Price: Money.Gold(10));

    /// <summary>
    /// Hires a recruit: they become <paramref name="buyer"/>'s companion, for nothing down. Their price is their wage,
    /// paid after every run they finish (see <see cref="SettleRun"/>).
    /// </summary>
    public static (ServiceResult Result, Character? Recruit) Hire(Character buyer, RecruitOffer offer, ContentCatalog content, DateTimeOffset now, IPurse? purse = null)
    {
        Character recruit = Character.Create(offer.Name, offer.Portrait, offer.Allocation, content, now, isCompanion: true, offer.Appearance);
        if (offer.Appearance.Persona == Rules.GameSession.ClodPersona)
        {
            // A frog hunter never walks past anything lying in the mud.
            recruit.SetPolicy(recruit.Policy with { PicksUpLoot = true });
        }

        recruit.GainXp(Progression.XpForLevel(offer.Level));
        foreach (string cardId in offer.Gear)
        {
            CardDefinition card = content.GetCard(cardId);
            // Better gear replaces the starter piece in the same slot.
            if (card.Slot is not null && recruit.Equipped.FirstOrDefault(owned => content.GetCard(owned).Slot == card.Slot) is { } starter)
            {
                recruit.TakeCard(starter);
            }

            recruit.GiveCard(card, content);
        }

        recruit.SetWage(offer.Price);
        return (new ServiceResult(true, $"{buyer.Name} hires {offer.Name} for {Money.Long(offer.Price)} a run, paid after each. {offer.Name} joins as {buyer.Name}'s companion."), recruit);
    }

    /// <summary>
    /// The end of a run in a dungeon. Every hired companion who was on it is paid their wage by whoever they follow,
    /// win or lose (as much of it as that purse holds). And a party that won it together grows closer: +1 bond
    /// between each two of them.
    /// </summary>
    /// <param name="party">Everyone who went, each with whoever they follow (null for a player's own hero).</param>
    /// <returns>What happened, to tell the players, and the companions who weren't paid in full (with who owed them).</returns>
    public static RunSettlement SettleRun(IReadOnlyList<(Character Member, Character? Owner)> party, bool won, RaidWorld? world)
    {
        var news = new List<string>();
        var unpaid = new List<(Character Companion, Character Owner)>();
        foreach ((Character companion, Character? owner) in party.Where(entry => entry.Member.Wage > 0 && entry.Owner is not null))
        {
            int paid = Math.Min(companion.Wage, owner!.Coins);
            if (paid > 0)
            {
                owner.Spend(paid);
                companion.Earn(paid);
            }

            if (paid < companion.Wage)
            {
                unpaid.Add((companion, owner));
            }

            news.Add(paid == companion.Wage ? $"{owner.Name} pays {companion.Name} {Money.Long(paid)}."
                : paid > 0 ? $"{owner.Name} can only pay {companion.Name} {Money.Long(paid)} of {Money.Long(companion.Wage)}."
                : $"{owner.Name} has nothing to pay {companion.Name}.");
        }

        if (won && world is not null && party.Count > 1)
        {
            for (int i = 0; i < party.Count; i++)
            {
                for (int j = i + 1; j < party.Count; j++)
                {
                    world.RaiseBond(party[i].Member.Id, party[j].Member.Id);
                }
            }

            news.Add("The party grows closer (+1 bond).");
        }

        return new RunSettlement(news, unpaid);
    }

    /// <summary>Who may bid for an unpaid companion: the party's other players (not who failed to pay them), the raid's leader first.</summary>
    public static IReadOnlyList<Character> Bidders(Character owner, IReadOnlyList<Character> party, Guid leader) =>
        [.. party.Where(member => !member.IsCompanion && member.Id != owner.Id).OrderBy(member => member.Id == leader ? 0 : 1)];

    /// <summary>What an offer is worth to the companion: its gold, and a point for each point of bond with whoever makes it.</summary>
    public static int OfferWorth(Character companion, Character bidder, int copper, RaidWorld? world) =>
        copper / Money.CopperPerGold + (world?.Bond(companion.Id, bidder.Id) ?? 0);

    /// <summary>
    /// The auction for an unpaid companion closes. The best offer wins (gold and bond together; a tie goes to the
    /// earlier bidder, the raid's leader first): it becomes their wage, paid after each run as before, and they follow
    /// whoever made it. No offer worth having: with a bond in the party they stay on regardless; without one, they'll
    /// start the next run leaving.
    /// </summary>
    /// <param name="offers">Each bidder's offer in copper (0 or less: no offer), in bidding order. An offer over the bidder's purse doesn't count.</param>
    public static AuctionResult CloseAuction(Character companion, Character owner, IReadOnlyList<(Character Bidder, int Copper)> offers, IReadOnlyList<Character> party, RaidWorld? world)
    {
        (Character Bidder, int Copper)? best = offers.Where(offer => offer.Copper > 0 && offer.Copper <= offer.Bidder.Coins && !offer.Bidder.IsCompanion && offer.Bidder.Id != owner.Id)
            .Select(offer => ((Character Bidder, int Copper)?)offer)
            .MaxBy(offer => OfferWorth(companion, offer!.Value.Bidder, offer.Value.Copper, world));
        if (best is { } won)
        {
            companion.SetWage(won.Copper);
            companion.SetLeaving(false);
            return new AuctionResult(AuctionOutcome.Hired, won.Bidder, $"{won.Bidder.Name} takes {companion.Name} on for {Money.Long(won.Copper)} a run.");
        }

        if (world is not null && party.Any(member => member.Id != companion.Id && world.Bond(companion.Id, member.Id) > 0))
        {
            companion.SetLeaving(false);
            return new AuctionResult(AuctionOutcome.StaysForFriendship, null, $"{companion.Name} goes unpaid, but stays for the company.");
        }

        companion.SetLeaving(true);
        return new AuctionResult(AuctionOutcome.Leaving, null, $"Nobody takes {companion.Name} on: they mean to head back to the inn when the party next sets out.");
    }

    /// <summary>
    /// Rents a room for someone leaving the party to wait upstairs: each night there they heal a heart
    /// and make a Rest roll.
    /// </summary>
    public static ServiceResult RentRoom(RaidSave raid, int day, IPurse payer, Character guest)
    {
        if (!payer.Spend(RoomPrice))
        {
            return NotEnough(payer, RoomPrice);
        }

        raid.RentRoom(guest.Id, day);
        return new ServiceResult(true, $"{Capital(payer.Name)} rents {guest.Name} a room for {Money.Long(RoomPrice)}. {guest.Name} will heal a heart a night up there, and rest.");
    }

    /// <summary>Someone leaving the party sleeps in the pig sty out back: free, and a Rest roll a night, no more.</summary>
    public static ServiceResult SleepInSty(RaidSave raid, int day, Character guest)
    {
        raid.SleepInSty(guest.Id, day);
        return new ServiceResult(true, $"{guest.Name} beds down in the pig sty out back. It's free, and it smells like it.");
    }

    /// <summary>
    /// Rooms for the whole party for the night, for <see cref="NightPrice"/>: a day passes, and everyone
    /// heals a heart and makes a Rest roll (with what they've eaten, <paramref name="boosts"/>, by party
    /// index); those waiting upstairs or in the sty get their night too. <paramref name="maxHearts"/> gives
    /// any character's full health.
    /// </summary>
    public static ServiceResult SleepTheNight(RaidSave raid, WoarldJourney journey, IRandomSource random, IPurse payer, IReadOnlyList<Character> party,
        Func<Guid, int> maxHearts, IReadOnlyList<RestBoost>? boosts = null)
    {
        if (!payer.Spend(NightPrice))
        {
            return NotEnough(payer, NightPrice);
        }

        journey.PassDay();
        raid.HealRooms(journey.Day, maxHearts);
        var rests = new List<string>();
        int healed = 0;
        for (int i = 0; i < party.Count; i++)
        {
            Character member = party[i];
            RestBoost boost = boosts is not null && i < boosts.Count ? boosts[i] : default;
            RestRoll roll = RestRoll.Roll(random, boost.Dice, boost.Bonus);
            healed += raid.Heal(member.Id, 1 + roll.Hearts, maxHearts(member.Id));
            rests.Add($"{member.Name} rolls {string.Join(", ", roll.Dice)}{(roll.Bonus > 0 ? $" +{roll.Bonus}" : "")}");
        }

        return new ServiceResult(true, $"{Capital(payer.Name)} pays {Money.Long(NightPrice)} for rooms. Everyone sleeps soundly: day {journey.Day} dawns. {string.Join("; ", rests)}. {UiCount(healed, "heart is", "hearts are")} healed.");
    }

    /// <summary>The next rumor an NPC tells: one that marks the map clears the clouds there.</summary>
    public static Rumor? NextRumor(RaidWorld world, TavernNpc npc)
    {
        if (npc.Rumors.Count == 0)
        {
            return null;
        }

        Rumor rumor = npc.Rumors[world.RumorsHeard(npc.Id) % npc.Rumors.Count];
        world.HearRumor(npc.Id);
        if (rumor.Reveal is { } reveal)
        {
            world.Journey.Reveal(new Grid.GridPosition(reveal.X, reveal.Y), reveal.Radius);
        }

        return rumor;
    }

    /// <summary>Spends a level 1 character's points at random: a few hearts or Luck, or a die.</summary>
    private static StatAllocation RandomAllocation(IRandomSource random) => random.NextInt(0, 4) switch
    {
        0 => new StatAllocation { AttackDice = 1, Hearts = 3, Skills = Skill.Shove },
        1 => new StatAllocation { DefenseDice = 1, Hearts = 2, Skills = Skill.Guard },
        2 => new StatAllocation { OdHandDice = 2, Luck = 1, Skills = Skill.Search | Skill.PickLock | Skill.Aid },
        _ => new StatAllocation { Hearts = 4, Luck = 1, Skills = Skill.Shove },
    };

    private static string UiCount(int amount, string singular, string plural) => $"{amount} {(amount == 1 ? singular : plural)}";

    private static ServiceResult NotEnough(IPurse who, int price) =>
        ServiceResult.Refused($"{Capital(who.Name)} has {Money.Long(who.Coins)}; that costs {Money.Long(price)}.");

    private static string Capital(string name) => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
}
