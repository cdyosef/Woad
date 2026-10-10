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
        int wins)
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
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    /// <summary>Which portrait the player picked; the game maps it to art.</summary>
    public string Portrait { get; private set; }

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
    /// <exception cref="ArgumentException">The name, portrait or allocation isn't allowed.</exception>
    public static Character Create(string name, string portrait, StatAllocation allocation, ContentCatalog content, DateTimeOffset createdUtc)
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
            PointBuy.CreationPoints - PointBuy.Cost(allocation), [], [], [], gold: 0, runsPlayed: 0, wins: 0);

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
    public HeroSetup ToHeroSetup() => new(Name, BaseStats, [.. _equipped, .. _belt, .. _backpack], Id, Portrait);

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
