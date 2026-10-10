using System.Text.Json;

namespace Woad.Core.Content;

/// <summary>
/// Every card, monster and dungeon theme in the game. Loaded from JSON files embedded in
/// Woad.Core (Content/Data), so the game and a future server share the same definitions.
/// </summary>
public sealed class ContentCatalog
{
    private const string ResourcePrefix = "Woad.Core.Content.Data.";
    private const string ThemeResourcePrefix = ResourcePrefix + "Themes.";

    /// <summary>Characters with a fixed meaning in room templates; monsters can't use them as glyphs.</summary>
    private const string ReservedGlyphs = ".#~C0123456789";

    private static readonly Lazy<ContentCatalog> Default = new(LoadEmbedded);

    private readonly Dictionary<string, CardDefinition> _cards;
    private readonly Dictionary<string, MonsterDefinition> _monsters;
    private readonly Dictionary<string, ThemeDefinition> _themes;
    private readonly Dictionary<char, MonsterDefinition> _monstersByGlyph;

    private ContentCatalog(
        IEnumerable<CardDefinition> cards,
        IEnumerable<MonsterDefinition> monsters,
        IEnumerable<ThemeDefinition> themes)
    {
        _cards = cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        _monsters = monsters.ToDictionary(monster => monster.Id, StringComparer.Ordinal);
        _themes = themes.ToDictionary(theme => theme.Id, StringComparer.Ordinal);
        _monstersByGlyph = _monsters.Values.ToDictionary(monster => monster.Glyph);
    }

    public IReadOnlyDictionary<string, CardDefinition> Cards => _cards;

    public IReadOnlyDictionary<string, MonsterDefinition> Monsters => _monsters;

    public IReadOnlyDictionary<string, ThemeDefinition> Themes => _themes;

    /// <summary>The content shipped with the game. Parsed once and cached.</summary>
    public static ContentCatalog LoadDefault() => Default.Value;

    /// <summary>Parses and validates content from JSON text.</summary>
    /// <exception cref="InvalidContentException">The JSON is malformed or breaks a content rule.</exception>
    public static ContentCatalog Parse(string cardsJson, string monstersJson, IEnumerable<string> themesJson)
    {
        var errors = new List<string>();
        List<CardDefinition> cards = Deserialize<List<CardDefinition>>(cardsJson, "cards", errors) ?? [];
        List<MonsterDefinition> monsters = Deserialize<List<MonsterDefinition>>(monstersJson, "monsters", errors) ?? [];
        List<ThemeDefinition> themes = themesJson
            .Select((json, index) => Deserialize<ThemeDefinition>(json, $"theme #{index + 1}", errors))
            .OfType<ThemeDefinition>()
            .ToList();

        if (errors.Count == 0)
        {
            Validate(cards, monsters, themes, errors);
        }

        if (errors.Count > 0)
        {
            throw new InvalidContentException(errors);
        }

        return new ContentCatalog(cards, monsters, themes);
    }

    public CardDefinition GetCard(string id) =>
        _cards.TryGetValue(id, out CardDefinition? card) ? card : throw new KeyNotFoundException($"Unknown card '{id}'.");

    public MonsterDefinition GetMonster(string id) =>
        _monsters.TryGetValue(id, out MonsterDefinition? monster) ? monster : throw new KeyNotFoundException($"Unknown monster '{id}'.");

    public ThemeDefinition GetTheme(string id) =>
        _themes.TryGetValue(id, out ThemeDefinition? theme) ? theme : throw new KeyNotFoundException($"Unknown theme '{id}'.");

    public MonsterDefinition? FindMonsterByGlyph(char glyph) => _monstersByGlyph.GetValueOrDefault(glyph);

    private static ContentCatalog LoadEmbedded()
    {
        var assembly = typeof(ContentCatalog).Assembly;

        string Read(string resourceName)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidContentException([$"Missing embedded content file '{resourceName}'."]);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        IEnumerable<string> themeFiles = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ThemeResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        return Parse(Read(ResourcePrefix + "cards.json"), Read(ResourcePrefix + "monsters.json"), themeFiles.Select(Read));
    }

    private static T? Deserialize<T>(string json, string source, List<string> errors)
        where T : class
    {
        try
        {
            T? result = JsonSerializer.Deserialize<T>(json, ContentJson.Options);
            if (result is null)
            {
                errors.Add($"{source}: file is empty.");
            }

            return result;
        }
        catch (JsonException exception)
        {
            errors.Add($"{source}: {exception.Message}");
            return null;
        }
    }

    private static void Validate(
        List<CardDefinition> cards,
        List<MonsterDefinition> monsters,
        List<ThemeDefinition> themes,
        List<string> errors)
    {
        ReportDuplicates(cards.Select(card => card.Id), "card id", errors);
        ReportDuplicates(monsters.Select(monster => monster.Id), "monster id", errors);
        ReportDuplicates(monsters.Select(monster => monster.Glyph.ToString()), "monster glyph", errors);
        ReportDuplicates(themes.Select(theme => theme.Id), "theme id", errors);

        foreach (CardDefinition card in cards)
        {
            ValidateCard(card, errors);
        }

        var monsterIds = monsters.Select(monster => monster.Id).ToHashSet(StringComparer.Ordinal);
        foreach (MonsterDefinition monster in monsters)
        {
            ValidateMonster(monster, monsterIds, errors);
        }

        var cardIds = cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        foreach (ThemeDefinition theme in themes)
        {
            if (theme.LootDeck.Count == 0)
            {
                errors.Add($"Theme '{theme.Id}' has an empty loot deck.");
            }

            foreach (LootDeckEntry entry in theme.LootDeck)
            {
                if (!cardIds.Contains(entry.Card))
                {
                    errors.Add($"Theme '{theme.Id}' loot deck references unknown card '{entry.Card}'.");
                }

                if (entry.Copies < 1)
                {
                    errors.Add($"Theme '{theme.Id}' loot deck entry '{entry.Card}' needs at least 1 copy.");
                }
            }
        }
    }

    private static void ValidateCard(CardDefinition card, List<string> errors)
    {
        string name = $"Card '{card.Id}'";
        bool isGear = card.Type is CardType.Weapon or CardType.Shield or CardType.Armor or CardType.Boots;

        if (isGear && card.Slot is null)
        {
            errors.Add($"{name} is {card.Type} gear but has no slot.");
        }

        if (card.TwoHanded && card.Slot != EquipSlot.MainHand)
        {
            errors.Add($"{name} is two-handed, so its slot must be mainHand.");
        }

        if (card.AttackRange < 1)
        {
            errors.Add($"{name} needs an attackRange of at least 1.");
        }

        if (card.Usage is CardUsage.SingleUse && card.Slot is not null)
        {
            errors.Add($"{name} is single-use, so it can't have a slot (it goes on the belt or in the backpack).");
        }

        if (card.Usage is not CardUsage.Persistent && card.Effect is null)
        {
            errors.Add($"{name} is {card.Usage} but has no effect.");
        }

        if (card.Usage is CardUsage.Charges && card.Charges < 1)
        {
            errors.Add($"{name} uses charges but has fewer than 1.");
        }

        if (card.Usage is not CardUsage.SingleUse && card.Effect is not null && card.Slot is null)
        {
            errors.Add($"{name} is reusable with an effect, so it needs a slot to be equipped into.");
        }

        if (card.Type is CardType.Treasure && card.Value < 1)
        {
            errors.Add($"{name} is treasure but has no value.");
        }

        if (card.Effect is { } effect)
        {
            if (effect.Amount < 1)
            {
                errors.Add($"{name} effect needs an amount of at least 1.");
            }

            if (effect.Range < 0 || effect.AreaRadius < 0)
            {
                errors.Add($"{name} effect range and areaRadius can't be negative.");
            }

            if (effect.Kind is CardEffectKind.Stun && effect.Range < 1)
            {
                errors.Add($"{name} stun effect needs a range of at least 1.");
            }
        }
    }

    private static void ValidateMonster(MonsterDefinition monster, HashSet<string> monsterIds, List<string> errors)
    {
        string name = $"Monster '{monster.Id}'";

        if (ReservedGlyphs.Contains(monster.Glyph) || char.IsWhiteSpace(monster.Glyph))
        {
            errors.Add($"{name} glyph '{monster.Glyph}' is reserved for room templates.");
        }

        if (monster.Hearts < 1)
        {
            errors.Add($"{name} needs at least 1 heart.");
        }

        if (monster.Move < 0 || monster.AttackDice < 0 || monster.DefenseDice < 0)
        {
            errors.Add($"{name} move, attackDice and defenseDice can't be negative.");
        }

        if (monster.AttackRange < 1)
        {
            errors.Add($"{name} needs an attackRange of at least 1.");
        }

        if (monster.SplitsInto is { } splitId)
        {
            if (!monsterIds.Contains(splitId))
            {
                errors.Add($"{name} splits into unknown monster '{splitId}'.");
            }

            if (monster.SplitCount < 1)
            {
                errors.Add($"{name} splits but has a splitCount below 1.");
            }
        }

        if (monster.Sweep is { Radius: < 1 } or { Every: < 1 })
        {
            errors.Add($"{name} sweep needs a radius and every of at least 1.");
        }
    }

    private static void ReportDuplicates(IEnumerable<string> values, string what, List<string> errors)
    {
        foreach (string duplicate in values.GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add($"Duplicate {what} '{duplicate}'.");
        }
    }
}
