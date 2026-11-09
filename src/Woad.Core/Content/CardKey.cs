namespace Woad.Core.Content;

/// <summary>
/// A card as a character keeps it between runs: its id, and after a "~" the wear it has taken (acid eats
/// armor, and it stays eaten until a blacksmith or an Autosmith Hammer mends it). "chain_mail~2" is chain
/// mail worn by 2. A belt unbuckled whole carries its buckle and mods, then its cards, in braces:
/// "good_belt{basic_buckle,belt_studs;health_potion,torch}". A shield with its owner's crest painted on it ends
/// in a "*": "knights_shield*", "tower_shield~1*".
/// </summary>
public static class CardKey
{
    private const char Mark = '~';
    private const char Open = '{';
    private const char Close = '}';
    private const char Between = ';';
    private const char Comma = ',';
    private const char Paint = '*';

    /// <summary>The key without what a packed belt carries.</summary>
    public static string Bare(string key) => key.IndexOf(Open) is var at and >= 0 ? key[..at] : key;

    /// <summary>The key without what a packed belt carries, or its paint.</summary>
    private static string Plain(string key) => Bare(key).TrimEnd(Paint);

    /// <summary>The card's id, without its wear.</summary>
    public static string Id(string key) => Plain(key) is var bare && bare.IndexOf(Mark) is var at and >= 0 ? bare[..at] : Plain(key);

    /// <summary>The wear it has taken (0 when whole).</summary>
    public static int Wear(string key) => Plain(key) is var bare && bare.IndexOf(Mark) is var at and >= 0 && int.TryParse(bare[(at + 1)..], out int wear) ? Math.Max(0, wear) : 0;

    /// <summary>Whether it has its owner's crest painted on it (a shield, with Shield Paint).</summary>
    public static bool Painted(string key) => Bare(key).EndsWith(Paint);

    /// <summary>The key for a card with this much wear, painted or not.</summary>
    public static string Of(string id, int wear, bool painted = false) => (wear > 0 ? $"{id}{Mark}{wear}" : id) + (painted ? Paint.ToString() : "");

    /// <summary>The key for a belt unbuckled whole: what's attached to it (its buckle and mods), and the cards on it.</summary>
    public static string Packed(string key, IEnumerable<string> attached, IEnumerable<string> held) =>
        $"{Bare(key)}{Open}{string.Join(Comma, attached)}{Between}{string.Join(Comma, held)}{Close}";

    /// <summary>What a packed belt's key carries: its buckle and mods, and its cards; null for any other card.</summary>
    public static (IReadOnlyList<string> Attached, IReadOnlyList<string> Held)? Unpack(string key)
    {
        int open = key.IndexOf(Open);
        if (open < 0)
        {
            return null;
        }

        // Keys inside can carry braces of their own (a bag on a belt packed whole): split only at this level.
        string inside = key[(open + 1)..^(key.EndsWith(Close) ? 1 : 0)];
        var parts = new List<List<string>> { new() };
        int depth = 0, start = 0;
        for (int i = 0; i <= inside.Length; i++)
        {
            char c = i < inside.Length ? inside[i] : Between;
            if (c == Open)
            {
                depth++;
            }
            else if (c == Close)
            {
                depth--;
            }
            else if (depth == 0 && (c == Comma || c == Between))
            {
                if (i > start)
                {
                    parts[^1].Add(inside[start..i]);
                }

                if (c == Between && i < inside.Length)
                {
                    parts.Add([]);
                }

                start = i + 1;
            }
        }

        return (parts[0], parts.Count > 1 ? parts[1] : []);
    }

    /// <summary>The key for a bag with these cards in it (by their keys).</summary>
    public static string Bagged(string key, IEnumerable<string> contents) => Packed(key, [], contents);

    /// <summary>What a card adds worn: its modifiers, less its wear (off its armor, or a weapon's value).</summary>
    public static Stats Modifiers(CardDefinition card, int wear)
    {
        Stats modifiers = card.Modifiers;
        if (wear <= 0)
        {
            return modifiers;
        }

        return modifiers.Armor > 0
            ? modifiers with { Armor = Math.Max(0, modifiers.Armor - wear) }
            : modifiers with { Weapon = Math.Max(0, modifiers.Weapon - wear) };
    }

    /// <summary>The most wear a card can take: all its armor, or all of a weapon's value.</summary>
    public static int MaxWear(CardDefinition card) => card.Modifiers.Armor > 0 ? card.Modifiers.Armor : Math.Max(0, card.Modifiers.Weapon);
}
