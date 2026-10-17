using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// Finds the generated art under res://assets (see tools/build-assets.py). Every lookup may return
/// null, and callers then draw their simple shape fallback, so missing art never breaks the game.
/// </summary>
public static class GameArt
{
    private static readonly Dictionary<string, Resource?> Cache = [];

    public static Texture2D? Tile(string themeId, string name) => Texture($"res://assets/themes/{themeId}/tiles/{name}.png");

    /// <summary>Monsters drawn with another monster's sprite (at a different size).</summary>
    private static readonly Dictionary<string, string> SpriteStandIns = new() { ["slimeling"] = "slime" };

    public static Texture2D? Monster(string themeId, string monsterId) =>
        Texture($"res://assets/themes/{themeId}/monsters/{SpriteStandIns.GetValueOrDefault(monsterId, monsterId)}.png");

    /// <summary>Bosses loom larger; monsters borrowing a relative's sprite are its smaller form.</summary>
    public static float MonsterScale(Woad.Core.Content.MonsterDefinition monster) =>
        monster.Boss ? 1.25f : SpriteStandIns.ContainsKey(monster.Id) ? 0.75f : 1f;

    public static Texture2D? Ui(string name) => Texture($"res://assets/ui/{name}.png");

    /// <summary>
    /// The body text font: Pixelify Sans (SIL Open Font License, see assets/fonts/PixelifySans-OFL.txt)
    /// at its bold weight, drawn crisp with no smoothing to match the pixel art. Null if it's missing.
    /// </summary>
    public static Font? BodyFont
    {
        get
        {
            if (Cache.TryGetValue(BodyFontKey, out Resource? cached))
            {
                return cached as Font;
            }

            Font? body = null;
            if (Load("res://assets/fonts/PixelifySans.ttf") is FontFile file)
            {
                file.Antialiasing = TextServer.FontAntialiasing.Gray;
                file.Hinting = TextServer.Hinting.None;
                file.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
                TextServer text = TextServerManager.GetPrimaryInterface();
                body = new FontVariation
                {
                    BaseFont = file,
                    VariationOpentype = new Godot.Collections.Dictionary { [text.NameToTag("wght")] = 700 },

                    // Its "fi" and "fl" ligatures look like an "A" ("JellyAsh"): letters stay separate.
                    OpentypeFeatures = new Godot.Collections.Dictionary { [text.NameToTag("liga")] = 0, [text.NameToTag("clig")] = 0 },
                };
            }

            Cache[BodyFontKey] = body;
            return body;
        }
    }

    private const string BodyFontKey = "body-font";

    /// <summary>The serif capitals pixel font for headings (uppercase, digits and a little punctuation).</summary>
    public static FontFile? HeadingFont
    {
        get
        {
            if (Load("res://assets/fonts/woad_gothic.fnt") is not FontFile font)
            {
                return null;
            }

            font.FixedSizeScaleMode = TextServer.FixedSizeScaleMode.IntegerOnly;
            if (font.Fallbacks.Count == 0)
            {
                font.Fallbacks = [ThemeDB.FallbackFont];
            }

            return font;
        }
    }

    public static Texture2D? Texture(string path) => Load(path) as Texture2D;

    /// <summary>Lets go of cached art right away, so Godot doesn't report it as still in use when the app quits.</summary>
    public static void Release()
    {
        foreach (Resource? resource in Cache.Values)
        {
            resource?.Dispose();
        }

        Cache.Clear();
    }

    private static Resource? Load(string path)
    {
        if (!Cache.TryGetValue(path, out Resource? resource))
        {
            resource = ResourceLoader.Exists(path) ? GD.Load<Resource>(path) : null;
            Cache[path] = resource;
        }

        return resource;
    }
}
