using Godot;
using Woad.Core.Art;

namespace Woad.Scenes.Ui;

/// <summary>
/// Figures painted in the game's Pixel Paint screen. They join the built-in ones in <see cref="All"/>
/// (one with a built-in figure's id takes its place), from two folders: the ones that ship with the
/// game (<see cref="ShippedDir"/>) and the player's own (<see cref="PaintedDir"/>), which win.
/// </summary>
public static partial class Looks
{
    public const string ShippedDir = "res://assets/heroes/painted";

    public static readonly Color EyeColor = new("181420");

    /// <summary>A shade pixel of a player color is this grey in its mask, so tinting keeps the shading.</summary>
    public static readonly Color MaskShade = new("a8a8a8");

    private static string? _paintedDir;

    private static List<Look>? _all;

    private static readonly Dictionary<string, PaintedLook> PaintedLooks = [];

    private static readonly Dictionary<string, ImageTexture> PaintedStrips = [];

    /// <summary>
    /// Where Pixel Paint saves: the game's own folder when it runs from the Godot editor (so the
    /// figures ship with the game), otherwise the player's.
    /// </summary>
    public static string PaintedDir
    {
        get => _paintedDir ??= ProjectSettings.GlobalizePath(OS.HasFeature("editor") ? ShippedDir : "user://painted");
        set
        {
            _paintedDir = value;
            Reload();
        }
    }

    /// <summary>Every figure a player can pick: the built-in ones, then the painted ones.</summary>
    public static IReadOnlyList<Look> All => _all ??= LoadAll();

    /// <summary>The painting behind a figure, or null for a built-in one nobody has repainted.</summary>
    public static PaintedLook? Painted(string id)
    {
        _ = All;
        return PaintedLooks.GetValueOrDefault(id);
    }

    public static bool IsBuilt(string id) => Built.Any(look => look.Id == id);

    /// <summary>Whether a painting of this id is saved in <see cref="PaintedDir"/> (and so can be deleted).</summary>
    public static bool IsSaved(string id) => id.Length > 0 && File.Exists(PaintedPath(id));

    public static string PaintedPath(string id) => Path.Combine(PaintedDir, $"{id}.json");

    /// <summary>A figure that comes with the game's own painted figures (<see cref="ShippedDir"/>).</summary>
    public static bool IsShipped(string id) => id.Length > 0 && Godot.FileAccess.FileExists($"{ShippedDir}/{id}.json");

    /// <summary>
    /// A figure that comes with the game (built in, or painted for it) and isn't saved where Pixel Paint saves: it's never
    /// painted over. Saving it makes a new figure, so the original is always there (for everyone).
    /// </summary>
    public static bool IsOriginal(string id) => (IsBuilt(id) || IsShipped(id)) && !IsSaved(id);

    /// <summary>A name for a new figure painted from <paramref name="name"/>: "Footman 2", "Footman 3"... the first not taken.</summary>
    public static string FreshName(string name)
    {
        string trimmed = name.Trim();
        for (int n = 2; ; n++)
        {
            string candidate = $"{trimmed} {n}";
            string id = PaintedLook.IdFor(candidate);
            if (!IsBuilt(id) && !IsShipped(id) && !IsSaved(id) && All.All(look => look.Id != id))
            {
                return candidate;
            }
        }
    }

    /// <summary>Reads the painted figures again (after one is saved or deleted).</summary>
    public static void Reload()
    {
        _all = null;
        Release();
    }

    /// <summary>Lets go of the painted figures' textures, alongside <see cref="GameArt.Release"/>.</summary>
    public static void Release()
    {
        foreach (ImageTexture strip in PaintedStrips.Values)
        {
            strip.Dispose();
        }

        PaintedStrips.Clear();
    }

    public static void SavePainted(PaintedLook look)
    {
        if (look.Problem() is { } problem)
        {
            throw new FormatException(problem);
        }

        Directory.CreateDirectory(PaintedDir);
        File.WriteAllText(PaintedPath(look.Id), look.ToJson() + "\n");
        Reload();
    }

    public static void DeletePainted(string id)
    {
        if (IsSaved(id))
        {
            File.Delete(PaintedPath(id));
            Reload();
        }
    }

    /// <summary>
    /// A figure as a painting to work on: its own if it was painted, otherwise read back from the
    /// built-in figure's art (null if that can't be read).
    /// </summary>
    public static PaintedLook? Painting(Look look)
    {
        if (Painted(look.Id) is { } painted)
        {
            return painted;
        }

        string path = $"res://assets/heroes/looks/{look.Id}.png";
        using Image? image = ReadImage(path);
        return image is null || image.GetWidth() < 5 * PaintedLook.Size || image.GetHeight() < PaintedLook.Size ? null : FromStrip(look, image);
    }

    /// <summary>The strip of five frames a painting is drawn from: its fixed colors, then the masks of the four player colors.</summary>
    public static Image StripImage(PaintedLook look)
    {
        Image image = Image.CreateEmpty(5 * PaintedLook.Size, PaintedLook.Size, false, Image.Format.Rgba8);
        for (int y = 0; y < PaintedLook.Size; y++)
        {
            for (int x = 0; x < PaintedLook.Size; x++)
            {
                char pixel = look.Rows[y][x];
                if (pixel == PaintedLook.Empty)
                {
                    continue;
                }

                int channel = PaintedLook.Channel(pixel);
                Color color = channel > 0 ? (PaintedLook.IsShade(pixel) ? MaskShade : Godot.Colors.White)
                    : pixel == PaintedLook.Eye ? EyeColor
                    : new Color(look.Palette[pixel]);
                image.SetPixel(channel * PaintedLook.Size + x, y, color);
            }
        }

        return image;
    }

    private static Texture2D? PaintedStrip(Look look)
    {
        if (Painted(look.Id) is not { } painted)
        {
            return null;
        }

        if (!PaintedStrips.TryGetValue(look.Id, out ImageTexture? strip))
        {
            using Image image = StripImage(painted);
            PaintedStrips[look.Id] = strip = ImageTexture.CreateFromImage(image);
        }

        return strip;
    }

    private static List<Look> LoadAll()
    {
        PaintedLooks.Clear();
        foreach (string json in ShippedFiles().Concat(PlayerFiles()))
        {
            try
            {
                PaintedLook look = PaintedLook.FromJson(json);
                PaintedLooks[look.Id] = look;
            }
            catch (Exception problem) when (problem is FormatException or System.Text.Json.JsonException)
            {
                GD.PushWarning($"A painted figure couldn't be read: {problem.Message}");
            }
        }

        List<Look> all = [.. Built.Select(look => PaintedLooks.TryGetValue(look.Id, out PaintedLook? painted) ? AsLook(painted, look) : look)];
        all.AddRange(PaintedLooks.Values.Where(painted => !IsBuilt(painted.Id)).OrderBy(painted => painted.Name, StringComparer.OrdinalIgnoreCase).Select(painted => AsLook(painted, null)));
        return all;
    }

    private static Look AsLook(PaintedLook painted, Look? built) => new(
        painted.Id, painted.Name,
        painted.SkinSet is not null && Sets.ContainsKey(painted.SkinSet) ? painted.SkinSet : null,
        painted.HairSet is not null && Sets.ContainsKey(painted.HairSet) ? painted.HairSet : null,
        [.. painted.Eyes.Select(eye => new Vector2I(eye.X, eye.Y))],
        built?.SkinDefault, built?.HairDefault);

    /// <summary>The figures that ship with the game, read through Godot so they're found inside an exported game too.</summary>
    private static IEnumerable<string> ShippedFiles()
    {
        if (!DirAccess.DirExistsAbsolute(ShippedDir))
        {
            yield break;
        }

        foreach (string name in DirAccess.GetFilesAt(ShippedDir).Where(name => name.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            yield return Godot.FileAccess.GetFileAsString($"{ShippedDir}/{name}");
        }
    }

    private static IEnumerable<string> PlayerFiles() =>
        Directory.Exists(PaintedDir) ? Directory.GetFiles(PaintedDir, "*.json").Order(StringComparer.Ordinal).Select(File.ReadAllText) : [];

    /// <summary>A built-in figure's strip as an image: from its texture, or (where there's no renderer to ask) from its file.</summary>
    private static Image? ReadImage(string path)
    {
        Image? image = GameArt.Texture(path)?.GetImage();
        if (image is null || image.IsEmpty())
        {
            image?.Dispose();
            string file = ProjectSettings.GlobalizePath(path);
            image = File.Exists(file) ? Image.LoadFromFile(file) : null;
        }

        if (image is null || image.IsEmpty())
        {
            image?.Dispose();
            return null;
        }

        if (image.IsCompressed())
        {
            image.Decompress();
        }

        image.Convert(Image.Format.Rgba8);
        return image;
    }

    private static PaintedLook FromStrip(Look look, Image strip)
    {
        var palette = new Dictionary<string, char>();
        var rows = new List<string>();
        for (int y = 0; y < PaintedLook.Size; y++)
        {
            char[] row = new char[PaintedLook.Size];
            for (int x = 0; x < PaintedLook.Size; x++)
            {
                row[x] = PaintedLook.Empty;
                if (look.Eyes.Contains(new Vector2I(x, y)))
                {
                    row[x] = PaintedLook.Eye;
                    continue;
                }

                for (int channel = 4; channel >= 1; channel--)
                {
                    Color mask = strip.GetPixel(channel * PaintedLook.Size + x, y);
                    if (mask.A > 0.5f)
                    {
                        row[x] = PaintedLook.Channels[(channel - 1) * 2 + (mask.R < 0.9f ? 1 : 0)];
                        break;
                    }
                }

                Color fixedColor = strip.GetPixel(x, y);
                if (row[x] == PaintedLook.Empty && fixedColor.A > 0.5f)
                {
                    string hex = "#" + fixedColor.ToHtml(false);
                    if (!palette.TryGetValue(hex, out char letter))
                    {
                        if (palette.Count >= PaintedLook.Letters.Length)
                        {
                            continue;
                        }

                        palette[hex] = letter = PaintedLook.Letters[palette.Count];
                    }

                    row[x] = letter;
                }
            }

            rows.Add(new string(row));
        }

        return new PaintedLook(look.Id, look.Name, look.SkinSet, look.HairSet, rows, palette.ToDictionary(entry => entry.Value, entry => entry.Key));
    }
}
