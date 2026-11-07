using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Characters;

/// <summary>
/// A saved player character: who they are, how they've grown and the cards they own between runs.
/// A run turns it into a <see cref="HeroSetup"/>.
/// </summary>
public sealed partial class Character : IPurse
{
    public const int MaxNameLength = 16;

    private readonly List<string> _equipped;
    private readonly List<string> _belt;
    private readonly List<string> _backpack;
    private readonly List<string> _carried;
    private readonly List<string> _spells;

    internal Character(
        Guid id,
        string name,
        string portrait,
        DateTimeOffset createdUtc,
        int xp,
        StatAllocation allocation,
        int unspentPoints,
        IEnumerable<string> equipped,
        IEnumerable<string> belt,
        IEnumerable<string> backpack,
        int gold,
        int runsPlayed,
        int wins,
        bool isCompanion = false,
        Appearance? appearance = null,
        int guildStanding = 0,
        int lawStanding = 0,
        int cowardStanding = 0,
        bool hasCowardsCloak = false,
        int powerDice = 0,
        CompanionPolicy? policy = null,
        IEnumerable<string>? spells = null,
        IReadOnlyDictionary<Condition, int>? lingering = null,
        IEnumerable<string>? carried = null)
    {
        Id = id;
        Name = name;
        Portrait = portrait;
        CreatedUtc = createdUtc;
        Xp = xp;
        Allocation = allocation;
        UnspentPoints = unspentPoints;
        _equipped = [.. equipped];
        _belt = [.. belt];
        _backpack = [.. backpack];
        _carried = [.. carried ?? []];
        Coins = gold;
        RunsPlayed = runsPlayed;
        Wins = wins;
        IsCompanion = isCompanion;
        Appearance = appearance ?? Appearance.Default;
        GuildStanding = guildStanding;
        LawStanding = lawStanding;
        CowardStanding = cowardStanding;
        HasCowardsCloak = hasCowardsCloak;
        PowerDice = Math.Clamp(powerDice, 0, GameRules.MaxBankedPowerDice + allocation.Magic);
        Policy = policy ?? CompanionPolicy.Default;
        _spells = [.. spells ?? []];
        _lingering = new Dictionary<Condition, int>((lingering ?? new Dictionary<Condition, int>()).Where(entry => entry.Value > 0 && Conditions.Lingering.Contains(entry.Key)));
    }

    private readonly Dictionary<Condition, int> _lingering;

    /// <summary>What the character carries from run to run until it's cured: Black Hearts and disease.</summary>
    public IReadOnlyDictionary<Condition, int> Lingering => _lingering;

    public Guid Id { get; }

    /// <summary>A companion joins a player's party and is played by the game, following that player's orders.</summary>
    public bool IsCompanion { get; }

    public string Name { get; private set; }

    /// <summary>Which portrait the player picked; the game maps it to art.</summary>
    public string Portrait { get; private set; }

    /// <summary>The character's look and colors; cosmetic, and can be changed any time.</summary>
    public Appearance Appearance { get; private set; }

    public DateTimeOffset CreatedUtc { get; }

    public int Xp { get; private set; }

    public int Level => Progression.LevelForXp(Xp);

    public StatAllocation Allocation { get; private set; }

    public int UnspentPoints { get; private set; }

    /// <summary>Card ids of equipped gear; each card's definition says which slot it fills.</summary>
    public IReadOnlyList<string> Equipped => _equipped;

    public IReadOnlyList<string> Belt => _belt;

    public IReadOnlyList<string> Backpack => _backpack;

    /// <summary>What the character carries in their arms (the Relic, or what found no other room).</summary>
    public IReadOnlyList<string> Carried => _carried;

    /// <summary>Ids of the spells they know: a basic one for each rank of Magic, and any learned from scrolls.</summary>
    public IReadOnlyList<string> Spells => _spells;

    /// <summary>The purse, in copper (see <see cref="Model.Money"/>).</summary>
    public int Coins { get; private set; }

    public int RunsPlayed { get; private set; }

    /// <summary>Their name with the Merchant Guild: rescuing merchants raises it, robbing or killing them lowers it. Better standing, better prices.</summary>
    public int GuildStanding { get; private set; }

    /// <summary>Their standing with the Law (guards, sheriffs and judges): crimes lower it.</summary>
    public int LawStanding { get; private set; }

    /// <summary>Their name among Cowards: every time they flee, it rises. (Craven the Cowherder takes note.)</summary>
    public int CowardStanding { get; private set; }

    /// <summary>Craven the Cowherder has given them the Coward's Cloak already.</summary>
    public bool HasCowardsCloak { get; private set; }

    /// <summary>Changes their standing with the Merchant Guild, the Law and Cowards (out on the Woarld Map, say).</summary>
    public void ChangeStanding(int guild = 0, int law = 0, int cowards = 0)
    {
        GuildStanding += guild;
        LawStanding += law;
        CowardStanding += cowards;
    }

    /// <summary>How it manages its own things on its turns, as a companion (see <see cref="CompanionPolicy"/>).</summary>
    public CompanionPolicy Policy { get; private set; }

    public void SetPolicy(CompanionPolicy policy) => Policy = policy;

    /// <summary>The rolls Luck is asked on when they're about to fail (the Luck menu's switches).</summary>
    public LuckRoll LuckAsks { get; private set; } = LuckRoll.All;

    public void SetLuckAsks(LuckRoll asks) => LuckAsks = asks & LuckRoll.All;

    /// <summary>Clover Luck points kept from Lucky Clovers, up to <see cref="GameRules.MaxCloverLuck"/>.</summary>
    public int CloverLuck { get; private set; }

    public void SetCloverLuck(int points) => CloverLuck = Math.Clamp(points, 0, GameRules.MaxCloverLuck);

    /// <summary>Power Dice banked and carried from one dungeon or fight to the next (up to the bank's limit).</summary>
    public int PowerDice { get; private set; }

    /// <summary>Notes that Craven gave them the Coward's Cloak (once a character).</summary>
    public void ReceiveCowardsCloak() => HasCowardsCloak = true;

    public int Wins { get; private set; }

    public Stats BaseStats => PointBuy.ToStats(Allocation);

    /// <summary>Hearts at full health: the base stats plus what equipped gear adds.</summary>
    public int MaxHearts(ContentCatalog content) => Math.Max(1, Stats(content).Hearts);

    /// <summary>The base stats plus what equipped gear adds.</summary>
    public Stats Stats(ContentCatalog content) => _equipped.Aggregate(BaseStats, (stats, key) => stats + CardKey.Modifiers(content.GetCard(key), CardKey.Wear(key)));

    /// <summary>Their worn gear (keys with wear), equipped first.</summary>
    public IEnumerable<string> WornGear => _equipped.Concat(_belt).Concat(_backpack).Concat(_carried).Where(key => CardKey.Wear(key) > 0);

    /// <summary>How many of a card (by id, whole) they hold anywhere: in hand, on the belt, in the bag or carried.</summary>
    public int CountOf(string cardId) => _equipped.Concat(_belt).Concat(_backpack).Concat(_carried).Count(key => key == cardId);

    /// <summary>Swaps one of their cards (by its key) for another where it is (a weapon the smith made finer). False if they haven't got it.</summary>
    public bool Replace(string key, string with)
    {
        foreach (List<string> place in new[] { _equipped, _belt, _backpack, _carried })
        {
            int at = place.IndexOf(key);
            if (at >= 0)
            {
                place[at] = with;
                return true;
            }
        }

        return false;
    }

    /// <summary>Mends one worn card (by its key) where it is. False if they haven't got it.</summary>
    public bool Mend(string key)
    {
        foreach (List<string> place in new[] { _equipped, _belt, _backpack })
        {
            int at = place.IndexOf(key);
            if (at >= 0)
            {
                place[at] = CardKey.Id(key);
                return true;
            }
        }

        return false;
    }

    /// <summary>A new level 1 character with the starter kit. Points not spent are kept for later.</summary>
    /// <param name="isCompanion">Make a companion instead of a player's hero; it can't be changed later.</param>
    /// <param name="spells">A basic spell for each rank of Magic bought.</param>
    /// <exception cref="ArgumentException">The name, portrait, allocation or spells aren't allowed.</exception>
    public static Character Create(
        string name, string portrait, StatAllocation allocation, ContentCatalog content, DateTimeOffset createdUtc, bool isCompanion = false,
        Appearance? appearance = null, IReadOnlyList<string>? spells = null)
    {
        string trimmed = name.Trim();
        if (CheckName(trimmed) is { } nameError)
        {
            throw new ArgumentException(nameError, nameof(name));
        }

        if (string.IsNullOrWhiteSpace(portrait))
        {
            throw new ArgumentException("Choose a portrait.", nameof(portrait));
        }

        if (PointBuy.Check(allocation, level: 1, PointBuy.CreationPoints) is { } allocationError)
        {
            throw new ArgumentException(allocationError, nameof(allocation));
        }

        if (Spellbook.Check(allocation.Magic, spells ?? [], content) is { } spellError)
        {
            throw new ArgumentException(spellError, nameof(spells));
        }

        var character = new Character(
            Guid.NewGuid(), trimmed, portrait, createdUtc, xp: 0, allocation,
            PointBuy.CreationPoints - PointBuy.Cost(allocation), [], [], [], gold: 0, runsPlayed: 0, wins: 0, isCompanion, appearance, spells: spells);

        foreach (string cardId in GameRules.StarterKit)
        {
            character.Place(cardId, content);
        }

        return character;
    }

    /// <summary>Why a (trimmed) name isn't allowed, or null if it is.</summary>
    public static string? CheckName(string name)
    {
        if (name.Length == 0)
        {
            return "Give your character a name.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Names can be at most {MaxNameLength} characters.";
        }

        if (!name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-'))
        {
            return "Names can use letters, numbers, spaces, apostrophes and hyphens.";
        }

        return null;
    }

    public void Rename(string name)
    {
        string trimmed = name.Trim();
        if (CheckName(trimmed) is { } error)
        {
            throw new ArgumentException(error, nameof(name));
        }

        Name = trimmed;
    }

    public void ChangePortrait(string portrait)
    {
        if (string.IsNullOrWhiteSpace(portrait))
        {
            throw new ArgumentException("Choose a portrait.", nameof(portrait));
        }

        Portrait = portrait;
    }

    public void ChangeAppearance(Appearance appearance) => Appearance = appearance;

    /// <summary>
    /// Spends unspent points. Points already spent can't be taken back. Each new rank of Magic needs a
    /// basic spell in <paramref name="learned"/> (checked against <paramref name="content"/>).
    /// </summary>
    public void Allocate(StatAllocation allocation, IReadOnlyList<string>? learned = null, ContentCatalog? content = null)
    {
        if (allocation.Magic != Allocation.Magic || learned is { Count: > 0 })
        {
            List<string> spells = [.. _spells, .. learned ?? []];
            string? spellError = content is null ? "Learning spells needs the game's content." : Spellbook.Check(allocation.Magic, spells, content);
            if (spellError is not null)
            {
                throw new ArgumentException(spellError, nameof(learned));
            }
        }

        Spend(allocation);
        _spells.AddRange(learned ?? []);
    }

    /// <summary>Learns a spell from a scroll (see <see cref="ScrollStudy"/>).</summary>
    internal void LearnFromScroll(string spellId) => _spells.Add(spellId);

    private void Spend(StatAllocation allocation)
    {
        if (PointBuy.AllStats.Any(stat => allocation[stat] < Allocation[stat]))
        {
            throw new ArgumentException("Points already spent can't be taken back.", nameof(allocation));
        }

        if (PointBuy.Check(allocation, Level, PointBuy.Cost(Allocation) + UnspentPoints) is { } error)
        {
            throw new ArgumentException(error, nameof(allocation));
        }

        UnspentPoints -= PointBuy.Cost(allocation) - PointBuy.Cost(Allocation);
        Allocation = allocation;
    }

    /// <summary>Adds XP and returns how many levels were gained; each grants points to spend.</summary>
    public int GainXp(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        int before = Level;
        Xp += amount;
        int gained = Level - before;
        UnspentPoints += gained * Progression.PointsPerLevel;
        return gained;
    }

    /// <summary>Pays <paramref name="amount"/> copper. False (and nothing paid) if the character hasn't that much.</summary>
    public bool Spend(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount > Coins)
        {
            return false;
        }

        Coins -= amount;
        return true;
    }

    /// <summary>Takes <paramref name="amount"/> copper into the purse.</summary>
    public void Earn(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Coins += amount;
    }

    /// <summary>Gives the character a card (bought, won or handed over): equipped if its slot is free, else belted or packed.</summary>
    public void GiveCard(CardDefinition card, ContentCatalog content) => Place(card.Id, content);

    /// <summary>Gives them a card they're handed (worn gear keeps its wear).</summary>
    public void GiveCard(string key, ContentCatalog content) => Place(key, content);

    /// <summary>
    /// Takes one copy of a card away (sold or given up): from the backpack first, then the belt,
    /// then what's equipped. False if the character hasn't got one.
    /// </summary>
    public bool TakeCard(string cardId) => _backpack.Remove(cardId) || _carried.Remove(cardId) || _belt.Remove(cardId) || _equipped.Remove(cardId);

    /// <summary>
    /// Records a finished run: XP (and any levels it brings), gold, runs played and wins, and the
    /// gear the hero came home with. Returns what changed, for the results screen.
    /// </summary>
    public RunReward ApplyRunResult(HeroRunResult result)
    {
        if (result.CharacterId != Id)
        {
            throw new ArgumentException($"That result belongs to another character, not {Name}.", nameof(result));
        }

        List<string> before = [.. _equipped, .. _belt, .. _backpack, .. _carried];
        List<string> after = [.. result.Equipped, .. result.Belt, .. result.Backpack, .. result.Carried];

        int levels = GainXp(result.Xp);
        // Points spent during the run (levels gained in the dungeon) are kept, and the spells learned there.
        if (result.Allocation is { } spent && spent != Allocation)
        {
            Spend(spent);
        }

        if (result.Spells is { } spells)
        {
            _spells.Clear();
            _spells.AddRange(spells);
        }

        Policy = result.Policy ?? Policy;
        LuckAsks = result.LuckAsks ?? LuckAsks;
        SetCloverLuck(result.CloverLuck);
        // A look changed in the run (the belt square's art, gear shown on the figure) is kept.
        Appearance = result.Appearance == Appearance.Default ? Appearance : result.Appearance;
        _lingering.Clear();
        foreach ((Condition condition, int stacks) in result.Lingering)
        {
            _lingering[condition] = stacks;
        }

        Coins += result.Coins;
        GuildStanding += result.GuildStandingChange;
        LawStanding += result.LawStandingChange;
        PowerDice = Math.Clamp(result.PowerDice, 0, GameRules.MaxBankedPowerDice + Allocation.Magic);
        RunsPlayed++;
        if (result.Won)
        {
            Wins++;
        }

        _equipped.Clear();
        _equipped.AddRange(result.Equipped);
        _belt.Clear();
        _belt.AddRange(result.Belt);
        _backpack.Clear();
        _backpack.AddRange(result.Backpack);
        _carried.Clear();
        _carried.AddRange(result.Carried);

        return new RunReward(levels, Without(after, before), Without(before, after));
    }

    /// <summary>The cards in <paramref name="cards"/> left over after taking out one of each in <paramref name="removed"/>.</summary>
    private static List<string> Without(IEnumerable<string> cards, IEnumerable<string> removed)
    {
        List<string> remaining = [.. cards];
        foreach (string card in removed)
        {
            remaining.Remove(card);
        }

        return remaining;
    }

    /// <summary>The hero this character plays as in a run, carrying everything they own.</summary>
    /// <param name="ownerSeat">For a companion, the seat of the player it belongs to.</param>
    public HeroSetup ToHeroSetup(int? ownerSeat = null) => new(Name, BaseStats, [.. _equipped, .. _belt, .. _backpack, .. _carried], Id, Portrait, ownerSeat, Appearance, GuildStanding: GuildStanding, PowerDice: PowerDice, Skills: Allocation.Skills,
        Allocation: Allocation, Xp: Xp, UnspentPoints: UnspentPoints, Policy: Policy, Spells: [.. _spells], Lingering: _lingering, LuckAsks: LuckAsks, CloverLuck: CloverLuck);

    /// <summary>
    /// Puts a card where a new hero would: in their arms if it's carry-only, a mod on the belt or buckle,
    /// an empty matching slot, then the belt, then the backpack.
    /// </summary>
    private void Place(string key, ContentCatalog content)
    {
        CardDefinition card = content.GetCard(key);
        List<CardDefinition> worn = [.. _equipped.Select(id => content.GetCard(id))];
        IEnumerable<EquipSlot> occupied = worn.SelectMany(Hero.SlotsFor);
        int beltSlots = worn.FirstOrDefault(other => other.Type == CardType.Belt)?.BeltSlots ?? 0;
        if (card.CarryOnly)
        {
            _carried.Add(key);
        }
        else if (CardKey.Unpack(key) is not null)
        {
            // A belt unbuckled whole stays that way, in the bag.
            _backpack.Add(key);
        }
        else if (card.ModFor is { } on && occupied.Contains(on) && (card.Repeats || !worn.Contains(card)))
        {
            _equipped.Add(key);
        }
        else if (card.Belts && _belt.Count < beltSlots)
        {
            // Belt tools ride on the belt, a torch too (unlit, till it's taken in hand).
            _belt.Add(key);
        }
        else if (card.Slot is not null && !Hero.SlotsFor(card).Intersect(occupied).Any())
        {
            _equipped.Add(key);
        }
        else if (Hero.CanBelt(card) && _belt.Count < beltSlots)
        {
            _belt.Add(key);
        }
        else
        {
            _backpack.Add(key);
        }
    }

    /// <summary>
    /// A character saved before belts were cards had a belt of 4 all the same: they get the Basic Belt
    /// and Basic Buckle that stand for it, worn first, so what's on their belt stays there.
    /// </summary>
    internal void GiveBasicBelt()
    {
        _equipped.Insert(0, "basic_buckle");
        _equipped.Insert(0, "basic_belt");
    }
}
