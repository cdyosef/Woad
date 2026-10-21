using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Characters;

/// <summary>
/// A saved player character: who they are, how they've grown and the cards they own between runs.
/// A run turns it into a <see cref="HeroSetup"/>.
/// </summary>
public sealed class Character
{
    public const int MaxNameLength = 16;

    private readonly List<string> _equipped;
    private readonly List<string> _belt;
    private readonly List<string> _backpack;

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
        Appearance? appearance = null)
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
        Gold = gold;
        RunsPlayed = runsPlayed;
        Wins = wins;
        IsCompanion = isCompanion;
        Appearance = appearance ?? Appearance.Default;
    }

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

    public int Gold { get; private set; }

    public int RunsPlayed { get; private set; }

    public int Wins { get; private set; }

    public Stats BaseStats => PointBuy.ToStats(Allocation);

    /// <summary>A new level 1 character with the starter kit. Points not spent are kept for later.</summary>
    /// <param name="isCompanion">Make a companion instead of a player's hero; it can't be changed later.</param>
    /// <exception cref="ArgumentException">The name, portrait or allocation isn't allowed.</exception>
    public static Character Create(
        string name, string portrait, StatAllocation allocation, ContentCatalog content, DateTimeOffset createdUtc, bool isCompanion = false,
        Appearance? appearance = null)
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

        var character = new Character(
            Guid.NewGuid(), trimmed, portrait, createdUtc, xp: 0, allocation,
            PointBuy.CreationPoints - PointBuy.Cost(allocation), [], [], [], gold: 0, runsPlayed: 0, wins: 0, isCompanion, appearance);

        foreach (string cardId in GameRules.StarterKit)
        {
            character.Place(content.GetCard(cardId), content);
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

    /// <summary>Spends unspent points. Points already spent can't be taken back.</summary>
    public void Allocate(StatAllocation allocation)
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

    /// <summary>Pays gold. False (and nothing paid) if the character hasn't that much.</summary>
    public bool SpendGold(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount > Gold)
        {
            return false;
        }

        Gold -= amount;
        return true;
    }

    public void EarnGold(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Gold += amount;
    }

    /// <summary>Gives the character a card (bought, won or handed over): equipped if its slot is free, else belted or packed.</summary>
    public void GiveCard(CardDefinition card, ContentCatalog content) => Place(card, content);

    /// <summary>
    /// Takes one copy of a card away (sold or given up): from the backpack first, then the belt,
    /// then what's equipped. False if the character hasn't got one.
    /// </summary>
    public bool TakeCard(string cardId) => _backpack.Remove(cardId) || _belt.Remove(cardId) || _equipped.Remove(cardId);

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

        List<string> before = [.. _equipped, .. _belt, .. _backpack];
        List<string> after = [.. result.Equipped, .. result.Belt, .. result.Backpack];

        int levels = GainXp(result.Xp);
        Gold += result.Gold;
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
    public HeroSetup ToHeroSetup(int? ownerSeat = null) => new(Name, BaseStats, [.. _equipped, .. _belt, .. _backpack], Id, Portrait, ownerSeat, Appearance);

    /// <summary>Puts a card where a new hero would: an empty matching slot, then the belt, then the backpack.</summary>
    private void Place(CardDefinition card, ContentCatalog content)
    {
        IEnumerable<EquipSlot> occupied = _equipped.SelectMany(id => Hero.SlotsFor(content.GetCard(id)));
        if (card.Slot is not null && !Hero.SlotsFor(card).Intersect(occupied).Any())
        {
            _equipped.Add(card.Id);
        }
        else if (card.Usage == CardUsage.SingleUse && _belt.Count < GameRules.BeltCapacity)
        {
            _belt.Add(card.Id);
        }
        else
        {
            _backpack.Add(card.Id);
        }
    }
}
