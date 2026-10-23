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
/// <param name="Winnings">Gold the player gained (negative: lost; 0: a tie, wagers back).</param>
public sealed record BonesResult(IReadOnlyList<int> Yours, IReadOnlyList<int> Theirs, int Winnings, string Message);

/// <summary>Someone the recruiter can introduce: a companion to hire, already outfitted.</summary>
public sealed record RecruitOffer(string Name, Appearance Appearance, string Portrait, StatAllocation Allocation, IReadOnlyList<string> Gear, int Level, int Price);

/// <summary>The services of a tavern's folk. Every one checks first and changes nothing if refused.</summary>
public static class TavernServices
{
    public const int MaxWager = 20;

    /// <summary>A room at the inn, for a night or for as long as someone waits upstairs.</summary>
    public const int RoomPrice = 2;

    /// <summary>Recruits on offer at a time.</summary>
    public const int RecruitsOffered = 3;

    private static readonly string[] Looks = ["wanderer", "ranger", "rogue", "brute", "barbarian", "druid", "monk", "elf", "brave", "bard", "wild_woman", "elf_lady"];
    private static readonly string[] Colors = ["woad", "ember", "moss", "gilt", "violet", "bone", "blood", "frost"];
    private static readonly string[] Skins = ["fair", "pale", "tan", "olive", "brown", "deep"];
    private static readonly string[] Hair = ["brown", "black", "blonde", "ginger", "auburn", "white", "grey"];

    /// <summary>Eats or drinks at the bar: paid now, it boosts the hero's next Rest.</summary>
    public static ServiceResult Eat(RaidWorld world, Character buyer, MenuItem item)
    {
        if (!buyer.SpendGold(item.Price))
        {
            return NotEnough(buyer, item.Price);
        }

        world.Feed(buyer.Id, item.Boost);
        return new ServiceResult(true, $"{buyer.Name} {(item.Kind == "drink" ? "drinks" : "eats")} the {item.Name}. {item.Text}");
    }

    /// <summary>Wraps a meal To-Go: carried until a Rest, where it's eaten.</summary>
    public static ServiceResult BuyToGo(RaidWorld world, Character buyer, MenuItem item)
    {
        if (world.Provisions(buyer.Id).Count >= RaidWorld.MaxProvisions)
        {
            return ServiceResult.Refused($"{buyer.Name} can't carry more than {RaidWorld.MaxProvisions} meals To-Go.");
        }

        if (!buyer.SpendGold(item.Price))
        {
            return NotEnough(buyer, item.Price);
        }

        world.Pack(buyer.Id, item.Id);
        return new ServiceResult(true, $"{buyer.Name} packs the {item.Name} To-Go, for the next Rest.");
    }

    /// <summary>
    /// A round of the cheapest drink for everyone else in the party: each gets its boost, and the
    /// buyer's bond with each of them grows.
    /// </summary>
    public static ServiceResult RoundForTheParty(RaidWorld world, TavernDefinition tavern, Character buyer, IReadOnlyList<Character> party)
    {
        List<Character> others = [.. party.Where(member => member.Id != buyer.Id)];
        MenuItem? drink = tavern.Menu.Where(item => item.Kind == "drink").MinBy(item => item.Price);
        if (others.Count == 0 || drink is null)
        {
            return ServiceResult.Refused("There's nobody else to buy a round for.");
        }

        int price = drink.Price * others.Count;
        if (!buyer.SpendGold(price))
        {
            return NotEnough(buyer, price);
        }

        foreach (Character other in others)
        {
            world.Feed(other.Id, drink.Boost);
            world.RaiseBond(buyer.Id, other.Id);
        }

        return new ServiceResult(true, $"{buyer.Name} buys {drink.Name} for {string.Join(", ", others.Select(other => other.Name))}. The party grows closer.");
    }

    /// <summary>A round for everyone in the bar: the party's name in the tavern grows.</summary>
    public static ServiceResult RoundForTheBar(RaidWorld world, TavernDefinition tavern, Character buyer)
    {
        if (!buyer.SpendGold(tavern.RoundForTheBar))
        {
            return NotEnough(buyer, tavern.RoundForTheBar);
        }

        world.RaiseReputation(tavern.Id);
        return new ServiceResult(true, $"{buyer.Name} buys the whole bar a round! Cheers go up. Reputation in {tavern.Name}: {world.Reputation(tavern.Id)}.");
    }

    /// <summary>What the merchant asks a hero for a card: twice its worth, less for good standing with the Merchant Guild (more for bad).</summary>
    public static int BuyPrice(CardDefinition card, int guildStanding = 0) => Rules.GameSession.MerchantPrice(card, guildStanding);

    /// <summary>What the merchant pays for a card: its worth.</summary>
    public static int SellPrice(CardDefinition card) => Math.Max(1, card.Value);

    public static ServiceResult Buy(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Character buyer, string cardId)
    {
        List<string> stock = world.Stock(tavern);
        if (!stock.Contains(cardId) || content.FindCard(cardId) is not { } card)
        {
            return ServiceResult.Refused("The merchant hasn't got one of those.");
        }

        if (buyer.GuildStanding <= Rules.GameSession.ShunnedStanding)
        {
            return ServiceResult.Refused($"The merchant won't deal with {buyer.Name}: the Guild knows that name.");
        }

        int price = BuyPrice(card, buyer.GuildStanding);
        if (!buyer.SpendGold(price))
        {
            return NotEnough(buyer, price);
        }

        stock.Remove(cardId);
        buyer.GiveCard(card, content);
        IReadOnlyList<string> newWares = world.SpendWithMerchants(price, content);
        string news = newWares.Count == 0 ? ""
            : $" The Merchant Guild rises to level {world.GuildLevel}: new wares ({string.Join(", ", newWares.Select(ware => content.GetCard(ware).Name))})!";
        return new ServiceResult(true, $"{buyer.Name} buys the {card.Name} for {price} gold.{news}");
    }

    /// <summary>Sells a card the hero owns (from the backpack first); the merchant keeps it for sale.</summary>
    public static ServiceResult Sell(RaidWorld world, TavernDefinition tavern, ContentCatalog content, Character seller, string cardId)
    {
        if (content.FindCard(cardId) is not { } card || !seller.TakeCard(cardId))
        {
            return ServiceResult.Refused($"{seller.Name} hasn't got one to sell.");
        }

        seller.EarnGold(SellPrice(card));
        world.Stock(tavern).Add(cardId);
        return new ServiceResult(true, $"{seller.Name} sells the {card.Name} for {SellPrice(card)} gold.");
    }

    /// <summary>A game of Bones for <paramref name="wager"/> gold: two dice each, high total wins; a tie hands the wagers back.</summary>
    public static BonesResult Bones(IRandomSource random, Character player, int wager)
    {
        if (wager < 1 || wager > MaxWager)
        {
            return new BonesResult([], [], 0, $"Wagers run from 1 to {MaxWager} gold.");
        }

        if (player.Gold < wager)
        {
            return new BonesResult([], [], 0, NotEnough(player, wager).Message);
        }

        int[] yours = [random.NextInt(1, 7), random.NextInt(1, 7)];
        int[] theirs = [random.NextInt(1, 7), random.NextInt(1, 7)];
        int difference = yours.Sum() - theirs.Sum();
        if (difference > 0)
        {
            player.EarnGold(wager);
            return new BonesResult(yours, theirs, wager, $"{player.Name} rolls {yours[0]} + {yours[1]} against {theirs[0]} + {theirs[1]}, and wins {wager} gold!");
        }

        if (difference < 0)
        {
            player.SpendGold(wager);
            return new BonesResult(yours, theirs, -wager, $"{player.Name} rolls {yours[0]} + {yours[1]} against {theirs[0]} + {theirs[1]}, and loses {wager} gold.");
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
            offers.Add(new RecruitOffer(name, appearance, color, RandomAllocation(random), gear, Level: 1 + tier, Price: 10 + 15 * tier));
        }

        return offers;
    }

    /// <summary>Hires a recruit: paid by <paramref name="buyer"/>, they become a companion character.</summary>
    public static (ServiceResult Result, Character? Recruit) Hire(Character buyer, RecruitOffer offer, ContentCatalog content, DateTimeOffset now)
    {
        if (!buyer.SpendGold(offer.Price))
        {
            return (NotEnough(buyer, offer.Price), null);
        }

        Character recruit = Character.Create(offer.Name, offer.Portrait, offer.Allocation, content, now, isCompanion: true, offer.Appearance);
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

        return (new ServiceResult(true, $"{buyer.Name} hires {offer.Name} for {offer.Price} gold. {offer.Name} joins as {buyer.Name}'s companion."), recruit);
    }

    /// <summary>
    /// Rents a room for someone leaving the party to wait upstairs: they heal a heart for every day
    /// they rest there.
    /// </summary>
    public static ServiceResult RentRoom(RaidSave raid, int day, Character payer, Character guest)
    {
        if (!payer.SpendGold(RoomPrice))
        {
            return NotEnough(payer, RoomPrice);
        }

        raid.RentRoom(guest.Id, day);
        return new ServiceResult(true, $"{payer.Name} rents {guest.Name} a room for {RoomPrice} gold. {guest.Name} will heal a heart a day up there.");
    }

    /// <summary>
    /// Rooms for the whole party for the night: a day passes, and everyone heals a heart (and those
    /// resting upstairs, a heart for the day). <paramref name="maxHearts"/> gives any character's full health.
    /// </summary>
    public static ServiceResult SleepTheNight(RaidSave raid, WoarldJourney journey, Character payer, IReadOnlyList<Character> party, Func<Guid, int> maxHearts)
    {
        int price = RoomPrice * party.Count;
        if (!payer.SpendGold(price))
        {
            return NotEnough(payer, price);
        }

        journey.PassDay();
        raid.HealRooms(journey.Day, maxHearts);
        int healed = party.Sum(member => raid.Heal(member.Id, 1, maxHearts(member.Id)));
        return new ServiceResult(true, $"{payer.Name} pays {price} gold for rooms. Everyone sleeps soundly: day {journey.Day} dawns, and {UiCount(healed, "heart is", "hearts are")} healed.");
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
        0 => new StatAllocation { AttackDice = 1, Hearts = 3 },
        1 => new StatAllocation { DefenseDice = 1, Hearts = 2, Luck = 1 },
        2 => new StatAllocation { OdHandDice = 1, Luck = 3 },
        _ => new StatAllocation { Hearts = 4, Luck = 2 },
    };

    private static string UiCount(int amount, string singular, string plural) => $"{amount} {(amount == 1 ? singular : plural)}";

    private static ServiceResult NotEnough(Character who, int price) =>
        ServiceResult.Refused($"{who.Name} has {who.Gold} gold; that costs {price}.");
}
