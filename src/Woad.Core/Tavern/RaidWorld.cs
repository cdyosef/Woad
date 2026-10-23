using System.Text.Json;
using Woad.Core.Content;
using Woad.Core.Randomness;
using Woad.Core.Woarld;

namespace Woad.Core.Tavern;

/// <summary>
/// The Woarld every Raiding Party on this PC shares: the map and how far it's been explored, the
/// party's name in each tavern, the bonds between heroes, the merchants' stock, the rumors heard,
/// and what each hero has eaten (or packed To-Go) for their next Rest.
/// </summary>
public sealed class RaidWorld
{
    /// <summary>Most To-Go meals a hero can carry.</summary>
    public const int MaxProvisions = 3;

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Dictionary<string, int> _reputation = [];
    private readonly Dictionary<string, int> _bonds = [];
    private readonly Dictionary<string, List<string>> _stock = [];
    private readonly Dictionary<string, int> _rumorsHeard = [];
    private readonly Dictionary<Guid, RestBoost> _fed = [];
    private readonly Dictionary<Guid, List<string>> _provisions = [];
    private readonly List<string> _guildWares = [];
    private readonly List<RememberedMerchant> _remembered = [];

    private RaidWorld(WoarldJourney journey) => Journey = journey;

    public WoarldJourney Journey { get; }

    /// <summary>Traveling merchants freed from dungeons: each raises the Merchant Guild a level.</summary>
    public int MerchantsRescued { get; set; }

    /// <summary>Gold spent with the Guild's merchants: business raises the Guild too.</summary>
    public int GoldSpentWithMerchants { get; private set; }

    /// <summary>Gold spent with merchants that each Guild level past the first takes.</summary>
    public static IReadOnlyList<int> GuildGoldLevels { get; } = [50, 150, 300, 500, 750, 1050, 1400, 1800];

    /// <summary>
    /// The Merchant Guild's level, shared by every merchant: 1, plus a level for each rescue and for each
    /// step of gold spent with them. Each level brings every merchant a new ware.
    /// </summary>
    public int GuildLevel => 1 + MerchantsRescued + GuildGoldLevels.Count(threshold => GoldSpentWithMerchants >= threshold);

    /// <summary>Wares the Guild has gained by leveling up: every merchant stocks one of each.</summary>
    public IReadOnlyList<string> GuildWares => _guildWares;

    /// <summary>Merchants the party has rescued, and the heroes they remember.</summary>
    public IReadOnlyList<RememberedMerchant> RememberedMerchants => _remembered;

    /// <summary>Notes gold spent with a merchant; any levels the Guild gains bring new wares.</summary>
    public IReadOnlyList<string> SpendWithMerchants(int gold, ContentCatalog content)
    {
        int before = GuildLevel;
        GoldSpentWithMerchants += Math.Max(0, gold);
        return LevelUp(before, content);
    }

    /// <summary>
    /// Rescued merchants join the Guild (a level each, with its new ware) and remember who helped them:
    /// everyone in the party, and most of all whoever freed them.
    /// </summary>
    public IReadOnlyList<string> RescueMerchants(IEnumerable<RememberedMerchant> rescued, ContentCatalog content)
    {
        int before = GuildLevel;
        foreach (RememberedMerchant merchant in rescued)
        {
            _remembered.Add(merchant);
            MerchantsRescued++;
        }

        return LevelUp(before, content);
    }

    /// <summary>
    /// For each level gained, a new ware for every merchant, picked at random from the gear and items
    /// the dungeons hold: the higher the level, the deeper (better) it may come from.
    /// </summary>
    private List<string> LevelUp(int before, ContentCatalog content)
    {
        var added = new List<string>();
        for (int level = before + 1; level <= GuildLevel; level++)
        {
            var random = new Pcg32((ulong)(level * 7919 + _guildWares.Count), 11);
            int tier = Math.Min(3, 1 + (level - 1) / 2);
            List<string> wares = content.Themes.Values
                .SelectMany(theme => theme.LootDeck)
                .Where(entry => entry.MinFloor <= tier && content.GetCard(entry.Card) is { Type: not CardType.Treasure, Coins: false })
                .Select(entry => entry.Card)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
            if (wares.Count == 0)
            {
                continue;
            }

            string ware = random.Pick(wares);
            _guildWares.Add(ware);
            foreach (List<string> stock in _stock.Values)
            {
                stock.Add(ware);
            }

            added.Add(ware);
        }

        return added;
    }

    public static RaidWorld Begin(WoarldMap map) => new(WoarldJourney.Begin(map, 1));

    /// <summary>The party's name in a tavern: rounds for the bar raise it, and better recruits follow it.</summary>
    public int Reputation(string tavernId) => _reputation.GetValueOrDefault(tavernId);

    public void RaiseReputation(string tavernId, int amount = 1) => _reputation[tavernId] = Reputation(tavernId) + amount;

    /// <summary>How close two heroes are: rounds bought for each other raise it.</summary>
    public int Bond(Guid one, Guid other) => _bonds.GetValueOrDefault(BondKey(one, other));

    public void RaiseBond(Guid one, Guid other, int amount = 1) => _bonds[BondKey(one, other)] = Bond(one, other) + amount;

    /// <summary>A tavern merchant's stock: their first stock, plus all that's been sold to them, less what's been bought.</summary>
    public List<string> Stock(TavernDefinition tavern)
    {
        if (!_stock.TryGetValue(tavern.Id, out List<string>? stock))
        {
            _stock[tavern.Id] = stock = [.. tavern.Stock, .. _guildWares];
        }

        return stock;
    }

    /// <summary>How many of an NPC's rumors the party has heard (they repeat once all are told).</summary>
    public int RumorsHeard(string npcId) => _rumorsHeard.GetValueOrDefault(npcId);

    public void HearRumor(string npcId) => _rumorsHeard[npcId] = RumorsHeard(npcId) + 1;

    /// <summary>What a hero has eaten and drunk since their last Rest, waiting to boost the next one.</summary>
    public RestBoost Fed(Guid hero) => _fed.GetValueOrDefault(hero);

    public void Feed(Guid hero, RestBoost boost)
    {
        RestBoost fed = Fed(hero);
        _fed[hero] = new RestBoost(fed.Dice + boost.Dice, fed.Bonus + boost.Bonus);
    }

    /// <summary>The menu items a hero carries To-Go: one is eaten at each Rest.</summary>
    public IReadOnlyList<string> Provisions(Guid hero) => _provisions.TryGetValue(hero, out List<string>? packed) ? packed : [];

    public bool Pack(Guid hero, string itemId)
    {
        if (!_provisions.TryGetValue(hero, out List<string>? packed))
        {
            _provisions[hero] = packed = [];
        }

        if (packed.Count >= MaxProvisions)
        {
            return false;
        }

        packed.Add(itemId);
        return true;
    }

    /// <summary>
    /// What each hero (in walking order) brings to a Rest: everything eaten since the last, and the
    /// first To-Go meal they carry, which is eaten now. Both are used up.
    /// </summary>
    public IReadOnlyList<RestBoost> TakeRestBoosts(IReadOnlyList<Guid> heroes, TavernDefinition tavern)
    {
        var boosts = new List<RestBoost>();
        foreach (Guid hero in heroes)
        {
            RestBoost boost = Fed(hero);
            _fed.Remove(hero);
            if (_provisions.TryGetValue(hero, out List<string>? packed) && packed.Count > 0)
            {
                MenuItem? meal = tavern.Menu.FirstOrDefault(item => item.Id == packed[0]);
                packed.RemoveAt(0);
                if (meal is not null)
                {
                    boost = new RestBoost(boost.Dice + meal.RestDice, boost.Bonus + meal.RestBonus);
                }
            }

            boosts.Add(boost);
        }

        return boosts;
    }

    public string ToJson() => JsonSerializer.Serialize(new SaveData
    {
        Version = CurrentVersion,
        Journey = JsonDocument.Parse(Journey.ToJson()).RootElement,
        Reputation = _reputation,
        Bonds = _bonds,
        Stock = _stock,
        RumorsHeard = _rumorsHeard,
        Fed = _fed.ToDictionary(entry => entry.Key, entry => new[] { entry.Value.Dice, entry.Value.Bonus }),
        Provisions = _provisions,
        MerchantsRescued = MerchantsRescued,
        GoldSpentWithMerchants = GoldSpentWithMerchants,
        GuildWares = _guildWares,
        RememberedMerchants = _remembered,
    }, Options);

    /// <exception cref="InvalidDataException">Not a Woarld this version can read.</exception>
    public static RaidWorld FromJson(string json, WoarldMap map)
    {
        SaveData data;
        try
        {
            data = JsonSerializer.Deserialize<SaveData>(json, Options) ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The Raiding Party's Woarld is damaged: {exception.Message}", exception);
        }

        if (data.Version != CurrentVersion)
        {
            throw new InvalidDataException("The Raiding Party's Woarld is from another version of the game.");
        }

        var world = new RaidWorld(WoarldJourney.FromJson(data.Journey.GetRawText(), map))
        {
            MerchantsRescued = Math.Max(0, data.MerchantsRescued),
            GoldSpentWithMerchants = Math.Max(0, data.GoldSpentWithMerchants),
        };
        world._guildWares.AddRange(data.GuildWares);
        world._remembered.AddRange(data.RememberedMerchants);
        foreach ((string key, int value) in data.Reputation)
        {
            world._reputation[key] = value;
        }

        foreach ((string key, int value) in data.Bonds)
        {
            world._bonds[key] = value;
        }

        foreach ((string key, List<string> value) in data.Stock)
        {
            world._stock[key] = value;
        }

        foreach ((string key, int value) in data.RumorsHeard)
        {
            world._rumorsHeard[key] = value;
        }

        foreach ((Guid key, int[] value) in data.Fed.Where(entry => entry.Value.Length == 2))
        {
            world._fed[key] = new RestBoost(value[0], value[1]);
        }

        foreach ((Guid key, List<string> value) in data.Provisions)
        {
            world._provisions[key] = [.. value.Take(MaxProvisions)];
        }

        return world;
    }

    private static string BondKey(Guid one, Guid other) => one.CompareTo(other) < 0 ? $"{one}|{other}" : $"{other}|{one}";

    private sealed record SaveData
    {
        public int Version { get; init; }

        public JsonElement Journey { get; init; }

        public Dictionary<string, int> Reputation { get; init; } = [];

        public Dictionary<string, int> Bonds { get; init; } = [];

        public Dictionary<string, List<string>> Stock { get; init; } = [];

        public Dictionary<string, int> RumorsHeard { get; init; } = [];

        public Dictionary<Guid, int[]> Fed { get; init; } = [];

        public Dictionary<Guid, List<string>> Provisions { get; init; } = [];

        public int MerchantsRescued { get; init; }

        public int GoldSpentWithMerchants { get; init; }

        public List<string> GuildWares { get; init; } = [];

        public List<RememberedMerchant> RememberedMerchants { get; init; } = [];
    }
}

/// <summary>A merchant the party rescued: who freed them, and everyone who came out of the dungeon with them.</summary>
public sealed record RememberedMerchant(string Name, Guid? FreedBy, IReadOnlyList<Guid> Helpers, int Day);
