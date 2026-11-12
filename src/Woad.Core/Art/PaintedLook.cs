using System.Text.Json;

namespace Woad.Core.Art;

/// <summary>
/// A hero figure painted in the game's Pixel Paint screen: a 16x16 grid of characters, a row a string, in
/// the same form tools/build-assets.py draws the built-in figures from. '.' is empty; T/t, R/r, S/s and
/// H/h are the main, trim, skin and hair colors the player picks (the small letter its shade); 'e' is an
/// eye (dark, crossed out while Keeling Over); any other character is a fixed color from
/// <paramref name="Palette"/> ("#rrggbb"). <paramref name="SkinSet"/> and <paramref name="HairSet"/> name
/// the color sets its skin and hair are picked from (null: it has none).
/// </summary>
public sealed record PaintedLook(string Id, string Name, string? SkinSet, string? HairSet, IReadOnlyList<string> Rows, IReadOnlyDictionary<char, string> Palette)
{
    public const int Size = 16;

    public const char Empty = '.';

    public const char Eye = 'e';

    /// <summary>The player-colored paints: main, trim, skin and hair, each then its shade.</summary>
    public const string Channels = "TtRrSsHh";

    /// <summary>The characters a fixed color can be filed under.</summary>
    public const string Letters = "abcdfgijklmnopquvwxyzABCDEFGIJKLMNOPQUVWXYZ0123456789";

    public static PaintedLook Blank(string name = "New Figure") =>
        new(IdFor(name), name, "human", "hair", [.. Enumerable.Repeat(new string(Empty, Size), Size)], new Dictionary<char, string>());

    /// <summary>A file and look id from a name: lower case, anything but letters and digits an underscore.</summary>
    public static string IdFor(string name)
    {
        var id = new System.Text.StringBuilder();
        foreach (char each in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(each))
            {
                id.Append(each);
            }
            else if (id.Length > 0 && id[^1] != '_')
            {
                id.Append('_');
            }
        }

        return id.ToString().Trim('_');
    }

    /// <summary>Which player color a character takes (1 main, 2 trim, 3 skin, 4 hair), or 0 for none.</summary>
    public static int Channel(char pixel) => Channels.IndexOf(pixel) is >= 0 and int at ? at / 2 + 1 : 0;

    /// <summary>Whether a player-colored character is the shade of its color.</summary>
    public static bool IsShade(char pixel) => char.IsAsciiLetterLower(pixel) && Channels.Contains(pixel);

    /// <summary>The eye pixels, left to right then top to bottom.</summary>
    public IEnumerable<(int X, int Y)> Eyes =>
        from y in Enumerable.Range(0, Rows.Count)
        from x in Enumerable.Range(0, Rows[y].Length)
        where Rows[y][x] == Eye
        select (x, y);

    public bool IsBlank => Rows.All(row => row.All(pixel => pixel == Empty));

    /// <summary>What's wrong with the figure, or null if it can be saved and drawn.</summary>
    public string? Problem()
    {
        if (Id.Length == 0)
        {
            return "The figure needs a name.";
        }

        if (Rows.Count != Size || Rows.Any(row => row.Length != Size))
        {
            return $"The figure isn't {Size} by {Size}.";
        }

        foreach (char pixel in Rows.SelectMany(row => row).Distinct())
        {
            if (pixel != Empty && pixel != Eye && !Channels.Contains(pixel) && !Palette.ContainsKey(pixel))
            {
                return $"'{pixel}' has no color.";
            }
        }

        foreach ((char letter, string color) in Palette)
        {
            if (letter == Empty || letter == Eye || Channels.Contains(letter))
            {
                return $"'{letter}' can't be a fixed color.";
            }

            if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit))
            {
                return $"'{color}' isn't a color.";
            }
        }

        return IsBlank ? "Nothing is painted yet." : null;
    }

    public string ToJson() => JsonSerializer.Serialize(
        new LookFile(Id, Name, SkinSet, HairSet, [.. Rows], Palette.OrderBy(entry => entry.Key).ToDictionary(entry => entry.Key.ToString(), entry => entry.Value)), Options);

    public static PaintedLook FromJson(string json)
    {
        LookFile file = JsonSerializer.Deserialize<LookFile>(json, Options) ?? throw new FormatException("The figure file is empty.");
        if (file.Palette?.Keys.FirstOrDefault(key => key.Length != 1) is { } bad)
        {
            throw new FormatException($"'{bad}' isn't one character.");
        }

        var look = new PaintedLook(file.Id ?? "", file.Name ?? file.Id ?? "", file.Skin, file.Hair, file.Rows ?? [],
            (file.Palette ?? []).ToDictionary(entry => entry.Key[0], entry => entry.Value.ToLowerInvariant()));
        return look.Problem() is { } problem ? throw new FormatException(problem) : look;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record LookFile(string? Id, string? Name, string? Skin, string? Hair, List<string>? Rows, Dictionary<string, string>? Palette);
}
