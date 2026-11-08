namespace Woad.Core.Content;

/// <summary>How a language's words look written in a speech bubble.</summary>
public enum Script
{
    /// <summary>Plain letters (Common).</summary>
    Plain,

    /// <summary>Dots and dashes arranged into runes, as if carved into stone, time and space.</summary>
    Primordial,

    /// <summary>Primordial's runes gone watery: wavy dashes, curving lines joining the dots.</summary>
    Gurgle,

    /// <summary>Primordial's runes carved only into stone, rough-hewn.</summary>
    Gravel,
}

/// <summary>How well someone takes in what's said in a language.</summary>
public enum Understanding
{
    /// <summary>Not a word: it stays in its script.</summary>
    None,

    /// <summary>A few words of it (a Primordial speaker hearing Gurgle): those turn to English, the rest stays.</summary>
    Some,

    /// <summary>All of it, slowly (a Primordial speaker hearing Gravel).</summary>
    Slowly,

    /// <summary>All of it: the runes turn to English, letter by letter.</summary>
    Fully,
}

/// <summary>
/// A language: what it's called, how it looks written, and the language it's a branch of (<paramref name="Parent"/>),
/// whose speakers make out <paramref name="ParentKnows"/> of it (or all of it slowly, with <paramref name="ParentReadsSlowly"/>).
/// </summary>
public sealed record Language(string Id, string Name, Script Script, string? Parent = null, IReadOnlySet<string>? ParentKnows = null, bool ParentReadsSlowly = false);

/// <summary>The languages of Woad. Everyone speaks Common.</summary>
public static class Languages
{
    public const string CommonId = "common";

    public static Language Common { get; } = new(CommonId, "Common", Script.Plain);

    public static Language Primordial { get; } = new("primordial", "Primordial", Script.Primordial);

    /// <summary>Gurgle, the water's Primordial (Leviathan Jr.'s): Primordial speakers make out a few of its words.</summary>
    public static Language Gurgle { get; } = new("gurgle", "Gurgle", Script.Gurgle, "primordial",
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "I", "trying", "peace", "you", "long", "freaks", "creeps", "join", "water" });

    /// <summary>Gravel, the stone's Primordial: Primordial speakers make it all out, slowly.</summary>
    public static Language Gravel { get; } = new("gravel", "Gravel", Script.Gravel, "primordial", ParentReadsSlowly: true);

    public static IReadOnlyList<Language> All { get; } = [Common, Primordial, Gurgle, Gravel];

    public static Language Get(string id) => All.FirstOrDefault(language => language.Id == id) ?? Common;

    /// <summary>The languages known, from a comma-separated list: Common always, and the rest that exist.</summary>
    public static IReadOnlyList<Language> Known(string list) =>
        [Common, .. All.Where(language => language != Common && list.Split(',', StringSplitOptions.TrimEntries).Contains(language.Id))];

    /// <summary>How well someone knowing <paramref name="known"/> takes in <paramref name="language"/>.</summary>
    public static Understanding Of(Language language, IEnumerable<Language> known)
    {
        List<Language> knows = [.. known];
        if (language == Common || knows.Contains(language))
        {
            return Understanding.Fully;
        }

        if (language.Parent is { } parent && knows.Any(other => other.Id == parent))
        {
            return language.ParentReadsSlowly ? Understanding.Slowly : language.ParentKnows is { Count: > 0 } ? Understanding.Some : Understanding.None;
        }

        return Understanding.None;
    }

    /// <summary>The best any of these listeners makes of it.</summary>
    public static Understanding Best(Language language, IEnumerable<IEnumerable<Language>> listeners) =>
        listeners.Select(known => Of(language, known)).DefaultIfEmpty(Understanding.None).Max();

    /// <summary>Whether someone with <paramref name="understanding"/> of <paramref name="language"/> makes out <paramref name="word"/>.</summary>
    public static bool MakesOut(Language language, Understanding understanding, string word) =>
        understanding is Understanding.Fully or Understanding.Slowly
        || (understanding == Understanding.Some && language.ParentKnows?.Contains(word.Trim('!', '?', '.', ',', '"', '\'')) == true);
}
