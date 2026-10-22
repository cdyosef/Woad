using Godot;

namespace Woad.Scenes.Ui;

/// <summary>The gothic look: dark panels in gold-trimmed, riveted frames, and serif pixel headings.</summary>
public static class UiTheme
{
    /// <summary>The body font's size: Jersey 10 is drawn on a 10-pixel grid, so this keeps it crisp.</summary>
    public const int FontSize = 10;
    /// <summary>Names and titles in the body font: one and a half times its grid, still crisp.</summary>
    public const int HeadingSize = 15;

    /// <summary>The heading font is drawn 9 pixels tall; sizes are multiples of that.</summary>
    public const int HeadingPixelSize = 9;

    private static readonly List<Texture2D> DrawnTextures = [];

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = FontSize };
        if (GameArt.BodyFont is { } body)
        {
            // Text drawn by hand (on the board, in the dice bar) uses the fallback font: the same one.
            theme.DefaultFont = body;
            ThemeDB.FallbackFont = body;
        }

        theme.SetColor("font_color", "Label", Palette.Text);
        theme.SetFontSize("normal_font_size", "RichTextLabel", FontSize);
        theme.SetColor("default_color", "RichTextLabel", Palette.Text);

        theme.SetStylebox("normal", "Button", Frame("frame", Box(Palette.Panel, Palette.Trim)));
        theme.SetStylebox("hover", "Button", Frame("frame_hover", Box(Palette.PanelHover, Palette.TrimBright)));
        theme.SetStylebox("pressed", "Button", Frame("frame_pressed", Box(Palette.PanelPressed, Palette.TrimBright, 2)));
        theme.SetStylebox("hover_pressed", "Button", Frame("frame_pressed", Box(Palette.PanelPressed, Palette.TrimBright, 2)));
        theme.SetStylebox("disabled", "Button", Frame("frame_disabled", Box(Palette.PanelDark, Palette.TrimDim)));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Palette.Text);
        theme.SetColor("font_hover_color", "Button", Palette.TrimBright);
        theme.SetColor("font_pressed_color", "Button", Palette.TrimBright);
        theme.SetColor("font_hover_pressed_color", "Button", Palette.TrimBright);
        theme.SetColor("font_disabled_color", "Button", Palette.TextDim);

        theme.SetStylebox("normal", "LineEdit", Box(Palette.PanelDark, Palette.Trim));
        theme.SetStylebox("focus", "LineEdit", Box(Palette.PanelDark, Palette.TrimBright));
        theme.SetColor("font_color", "LineEdit", Palette.Text);
        theme.SetColor("font_placeholder_color", "LineEdit", Palette.TextDim);
        theme.SetColor("caret_color", "LineEdit", Palette.TrimBright);
        theme.SetFontSize("font_size", "LineEdit", 10);

        theme.SetStylebox("panel", "Panel", Frame("frame", Box(Palette.Panel, Palette.Trim, 2)));
        theme.SetStylebox("panel", "TooltipPanel", Box(Palette.PanelDark, Palette.Trim));
        theme.SetColor("font_color", "TooltipLabel", Palette.Text);
        theme.SetFontSize("font_size", "TooltipLabel", FontSize);

        theme.SetStylebox("slider", "HSlider", SliderTrack(Palette.PanelDark));
        theme.SetStylebox("grabber_area", "HSlider", SliderTrack(Palette.Trim));
        theme.SetStylebox("grabber_area_highlight", "HSlider", SliderTrack(Palette.TrimBright));
        theme.SetIcon("grabber", "HSlider", Grabber(Palette.Trim));
        theme.SetIcon("grabber_highlight", "HSlider", Grabber(Palette.TrimBright));

        return theme;
    }

    /// <summary>Lets go of the textures drawn here, alongside <see cref="GameArt.Release"/>.</summary>
    public static void Release()
    {
        foreach (Texture2D texture in DrawnTextures)
        {
            texture.Dispose();
        }

        DrawnTextures.Clear();
    }

    private static StyleBoxFlat SliderTrack(Color fill)
    {
        StyleBoxFlat track = Box(fill, Palette.TrimDim);
        track.SetContentMarginAll(3);
        return track;
    }

    /// <summary>A small gold slider knob with a dark outline, drawn in code.</summary>
    private static Texture2D Grabber(Color fill)
    {
        const int width = 7, height = 13;
        Image image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        image.Fill(Palette.Ink);
        image.FillRect(new Rect2I(1, 1, width - 2, height - 2), fill);
        image.FillRect(new Rect2I(1, 1, width - 2, 1), fill.Lightened(0.35f));
        ImageTexture texture = ImageTexture.CreateFromImage(image);
        DrawnTextures.Add(texture);
        return texture;
    }

    /// <summary>"1 die", "2 dice", "1 step", "3 steps".</summary>
    public static string Count(int amount, string singular, string plural) => $"{amount} {(amount == 1 ? singular : plural)}";

    /// <summary>Dice with a fixed bonus: "2d6+1", "1d6", "1d6-1".</summary>
    public static string Dice(int dice, int bonus) =>
        $"{dice}d6{(bonus > 0 ? $"+{bonus}" : bonus < 0 ? bonus.ToString() : "")}";

    /// <summary>A nine-patch from a generated frame texture, or the flat fallback when the art is missing.</summary>
    public static StyleBox Frame(string textureName, StyleBox fallback)
    {
        if (GameArt.Ui(textureName) is not { } texture)
        {
            return fallback;
        }

        var frame = new StyleBoxTexture { Texture = texture };
        frame.SetTextureMarginAll(4);
        frame.SetContentMarginAll(3);
        frame.ContentMarginLeft = 5;
        return frame;
    }

    /// <summary>Switches a label to the serif pixel font (falls back to the normal font without the art).</summary>
    /// <param name="scale">Multiples of the font's 9-pixel height.</param>
    public static T Heading<T>(T control, int scale) where T : Control
    {
        if (GameArt.HeadingFont is { } font)
        {
            control.AddThemeFontOverride("font", font);
            control.AddThemeFontSizeOverride("font_size", HeadingPixelSize * scale);
        }

        return control;
    }

    public static StyleBoxFlat Box(Color fill, Color border, int borderWidth = 1)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        box.SetBorderWidthAll(borderWidth);
        box.SetContentMarginAll(2);
        box.ContentMarginLeft = 4;
        return box;
    }

    public static Label Label(Vector2 position, Vector2 size, string text = "", int fontSize = FontSize, Color? color = null)
    {
        var label = new Label
        {
            Position = position,
            Size = size,
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        if (color is { } tint)
        {
            label.AddThemeColorOverride("font_color", tint);
        }

        return label;
    }

    /// <summary>A small text label that can hold inline icons (see <see cref="Icon"/>) and colors.</summary>
    public static RichTextLabel RichLabel(Vector2 position, Vector2 size)
    {
        var label = new RichTextLabel
        {
            Position = position,
            Size = size,
            BbcodeEnabled = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("normal_font_size", FontSize);
        label.AddThemeConstantOverride("line_separation", 2);
        return label;
    }

    /// <summary>An inline icon from assets/ui for a <see cref="RichLabel"/>, or the fallback word without the art.</summary>
    public static string Icon(string name, string fallback)
    {
        string path = $"res://assets/ui/{name}.png";
        return ResourceLoader.Exists(path) ? $"[img=11x11]{path}[/img]" : fallback;
    }

    /// <summary>Gold for a <see cref="RichLabel"/>: a coin and the amount in gold, easy to spot.</summary>
    public static string Gold(int amount) => $"{Icon("icon_gold", "")} [color={Palette.Hex(Palette.Loot)}]{amount} gold[/color]";

    /// <summary>Hearts for a <see cref="RichLabel"/>: a heart and how many are left of how many.</summary>
    public static string Hearts(int now, int max)
    {
        string heart = ResourceLoader.Exists("res://assets/ui/heart_full.png") ? "[img=7x7]res://assets/ui/heart_full.png[/img] " : "";
        Color color = now < max ? Palette.Danger : Palette.Heal;
        return $"{heart}[color={Palette.Hex(color)}]{now}/{max}[/color]";
    }

    /// <summary>A button with almost no side padding, for short labels in a tight row.</summary>
    /// <summary>
    /// Marks a button with the color of the dice it uses: a stripe down its left edge and tinted text
    /// (Rumble red, Wend green, Od Hand yellow, Power purple), keeping the gold-trimmed frame.
    /// </summary>
    public static T Accent<T>(T button, Color color) where T : Button
    {
        float height = button.Size.Y;
        button.AddChild(new ColorRect { Position = new Vector2(3, 3), Size = new Vector2(2, Math.Max(2, height - 6)), Color = color, MouseFilter = Control.MouseFilterEnum.Ignore });
        button.AddThemeColorOverride("font_color", color.Lerp(Palette.Text, 0.35f));
        button.AddThemeColorOverride("font_hover_color", color.Lightened(0.35f));
        button.AddThemeColorOverride("font_pressed_color", color.Lightened(0.35f));
        button.AddThemeColorOverride("font_hover_pressed_color", color.Lightened(0.35f));
        return button;
    }

    public static Button CompactButton(Vector2 position, Vector2 size, string text)
    {
        Button button = Button(position, size, text);
        (string State, string Texture, StyleBox Fallback)[] looks =
        [
            ("normal", "frame", Box(Palette.Panel, Palette.Trim)),
            ("hover", "frame_hover", Box(Palette.PanelHover, Palette.TrimBright)),
            ("pressed", "frame_pressed", Box(Palette.PanelPressed, Palette.TrimBright, 2)),
            ("hover_pressed", "frame_pressed", Box(Palette.PanelPressed, Palette.TrimBright, 2)),
            ("disabled", "frame_disabled", Box(Palette.PanelDark, Palette.TrimDim)),
        ];
        foreach ((string state, string texture, StyleBox fallback) in looks)
        {
            StyleBox box = Frame(texture, fallback);
            box.ContentMarginLeft = box.ContentMarginRight = 1;
            button.AddThemeStyleboxOverride(state, box);
        }

        return button;
    }

    public static Button Button(Vector2 position, Vector2 size, string text)
    {
        var button = new Button
        {
            Position = position,
            Size = size,
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            ClipText = true,
        };
        button.Pressed += () => Sfx.Play("click");
        return button;
    }
}
