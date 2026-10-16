using Godot;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>
/// A hero figure players can pick. <paramref name="SkinSet"/> and <paramref name="HairSet"/> name the
/// <see cref="Looks.Sets"/> its skin and hair channels are colored from (null: it has none);
/// <paramref name="Eyes"/> are the sprite pixels crossed out while Keeling Over. A new look starts with
/// <paramref name="SkinDefault"/> and <paramref name="HairDefault"/> (null: the first of the set).
/// </summary>
public sealed record Look(string Id, string Name, string? SkinSet, string? HairSet, Vector2I[] Eyes, string? SkinDefault = null, string? HairDefault = null);

/// <summary>A named color a player can pick.</summary>
public sealed record Swatch(string Id, string Name, Color Color);

/// <summary>A look with every color picked: what to draw.</summary>
public sealed record LookColors(Look Look, Color Main, Color Trim, Color Skin, Color Hair);

/// <summary>
/// The hero looks (art from tools/build-assets.py: a strip of five 16x16 frames per look, the fixed
/// colors then white masks for the main, trim, skin and hair colors) and the colors to pick from.
/// </summary>
public static partial class Looks
{
    public static IReadOnlyList<Swatch> Colors { get; } =
    [
        new("woad", "Woad", new Color("3f6fd8")),
        new("ember", "Ember", new Color("d8613f")),
        new("moss", "Moss", new Color("5fae5a")),
        new("gilt", "Gilt", new Color("d8b13f")),
        new("violet", "Violet", new Color("9a5fd8")),
        new("bone", "Bone", new Color("d8d0bc")),
        new("blood", "Blood", new Color("b8323f")),
        new("frost", "Frost", new Color("5fc8d8")),
        new("rose", "Rose", new Color("e07db8")),
        new("teal", "Teal", new Color("2f9a8a")),
        new("umber", "Umber", new Color("7a5230")),
        new("coal", "Coal", new Color("46424e")),
    ];

    /// <summary>The colors a look's skin or hair can take, by set: what the set is called and its swatches.</summary>
    public static IReadOnlyDictionary<string, (string Name, IReadOnlyList<Swatch> Swatches)> Sets { get; } =
        new Dictionary<string, (string, IReadOnlyList<Swatch>)>
        {
            ["human"] = ("Skin", [
                new("fair", "Fair", new Color("e8b890")),
                new("pale", "Pale", new Color("f4d8c0")),
                new("tan", "Tan", new Color("c89068")),
                new("olive", "Olive", new Color("b08050")),
                new("brown", "Brown", new Color("8a5a3a")),
                new("deep", "Deep", new Color("5e3c28")),
            ]),
            ["scales"] = ("Scales", [
                new("green", "Green", new Color("5aa050")),
                new("teal", "Teal", new Color("3a9a8a")),
                new("sand", "Sand", new Color("c8a860")),
                new("crimson", "Crimson", new Color("b04a3a")),
                new("ash", "Ash", new Color("6a6a74")),
            ]),
            ["stone"] = ("Stone", [
                new("granite", "Granite", new Color("8a8a90")),
                new("sandstone", "Sandstone", new Color("c0a070")),
                new("slate", "Slate", new Color("5a6070")),
                new("marble", "Marble", new Color("d8d4cc")),
                new("mossy", "Mossy", new Color("7a8a6a")),
            ]),
            ["wood"] = ("Wood", [
                new("oak", "Oak", new Color("9a6a3a")),
                new("birch", "Birch", new Color("d0c4a8")),
                new("pine", "Pine", new Color("7a5232")),
                new("redwood", "Redwood", new Color("a04a32")),
                new("ebony", "Ebony", new Color("4a3a32")),
            ]),
            ["darkelf"] = ("Skin", [
                new("ash", "Ash", new Color("7a7488")),
                new("lavender", "Lavender", new Color("9a88b8")),
                new("slate", "Slate", new Color("56608a")),
                new("obsidian", "Obsidian", new Color("3a3444")),
            ]),
            ["demon"] = ("Skin", [
                new("red", "Red", new Color("c03a32")),
                new("crimson", "Crimson", new Color("8a2030")),
                new("purple", "Purple", new Color("7a3a9a")),
                new("charcoal", "Charcoal", new Color("3e3640")),
            ]),
            ["hair"] = ("Hair", [
                new("brown", "Brown", new Color("7a4a2a")),
                new("black", "Black", new Color("2e2630")),
                new("blonde", "Blonde", new Color("e0c060")),
                new("ginger", "Ginger", new Color("d0702a")),
                new("auburn", "Auburn", new Color("8a3a2a")),
                new("white", "White", new Color("e8e4dc")),
                new("grey", "Grey", new Color("9a96a0")),
                new("blue", "Blue", new Color("4a6ad8")),
                new("pink", "Pink", new Color("e080b0")),
            ]),
            ["fur"] = ("Fur", [
                new("grey", "Grey", new Color("8a8078")),
                new("brown", "Brown", new Color("7a5a3a")),
                new("black", "Black", new Color("3a3436")),
                new("white", "White", new Color("dcd8d0")),
                new("russet", "Russet", new Color("a85a30")),
            ]),
            ["plumage"] = ("Plumage", [
                new("jay", "Jay", new Color("3a5ab8")),
                new("crow", "Crow", new Color("2e2a36")),
                new("parrot", "Parrot", new Color("d0402e")),
                new("hawk", "Hawk", new Color("8a5a32")),
                new("dove", "Dove", new Color("e4e0dc")),
            ]),
            ["leaf"] = ("Leaves", [
                new("green", "Green", new Color("5aa040")),
                new("spring", "Spring", new Color("9ad050")),
                new("autumn", "Autumn", new Color("d08030")),
                new("red", "Red", new Color("b83a2a")),
                new("gold", "Gold", new Color("e0c040")),
            ]),
        };

    public const string DefaultTrim = "gilt";

    public static Look Get(string id) => All.FirstOrDefault(look => look.Id == id) ?? All[0];

    /// <summary>The swatches for a set, or none for a look without that channel.</summary>
    public static IReadOnlyList<Swatch> SetSwatches(string? set) => set is not null && Sets.TryGetValue(set, out var entry) ? entry.Swatches : [];

    /// <summary>
    /// The colors to draw an appearance in. Unknown or empty choices fall back: the main color to the
    /// character's crest color (as before looks existed), skin and hair to the look's own starting color.
    /// </summary>
    public static LookColors Resolve(Appearance appearance, string portraitId = "")
    {
        Look look = Get(appearance.Look);
        Color main = Find(Colors, appearance.Main)?.Color ?? Find(Colors, portraitId)?.Color ?? Colors[0].Color;
        Color trim = Find(Colors, appearance.Trim)?.Color ?? Find(Colors, DefaultTrim)!.Color;
        Color skin = Shown(SetSwatches(look.SkinSet), appearance.Skin, look.SkinDefault)?.Color ?? Godot.Colors.White;
        Color hair = Shown(SetSwatches(look.HairSet), appearance.Hair, look.HairDefault)?.Color ?? Godot.Colors.White;
        return new LookColors(look, main, trim, skin, hair);
    }

    /// <summary>The swatch shown for a skin or hair choice: the chosen one, else the look's starting one, else the first (null if the set is empty).</summary>
    public static Swatch? Shown(IReadOnlyList<Swatch> swatches, string id, string? fallback) =>
        Find(swatches, id) ?? (fallback is null ? null : Find(swatches, fallback)) ?? swatches.FirstOrDefault();

    /// <summary>The main color id an appearance shows (its own, else the crest's, else the first).</summary>
    public static string MainId(Appearance appearance, string portraitId = "") =>
        Find(Colors, appearance.Main)?.Id ?? Find(Colors, portraitId)?.Id ?? Colors[0].Id;

    public static Swatch? Find(IReadOnlyList<Swatch> swatches, string id) => swatches.FirstOrDefault(swatch => swatch.Id == id);

    public static Texture2D? Strip(Look look) => GameArt.Texture($"res://assets/heroes/looks/{look.Id}.png");

    /// <summary>Draws the figure into <paramref name="rect"/> (under the canvas's current transform). False if there's no art.</summary>
    public static bool Draw(CanvasItem canvas, Rect2 rect, LookColors colors, Color? modulate = null)
    {
        if (Strip(colors.Look) is not { } strip)
        {
            return false;
        }

        Color shade = modulate ?? Godot.Colors.White;
        Color[] tints = [Godot.Colors.White, colors.Main, colors.Trim, colors.Skin, colors.Hair];
        for (int frame = 0; frame < tints.Length; frame++)
        {
            canvas.DrawTextureRectRegion(strip, rect, new Rect2(frame * 16, 0, 16, 16), tints[frame] * shade);
        }

        return true;
    }
}
