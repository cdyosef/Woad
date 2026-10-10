using Godot;

namespace Woad.Scenes.Ui;

/// <summary>The gothic look: dark panels in gold-trimmed, riveted frames, and serif pixel headings.</summary>
public static class UiTheme
{
    public const int FontSize = 8;
    public const int HeadingSize = 12;

    /// <summary>The heading font is drawn 9 pixels tall; sizes are multiples of that.</summary>
    public const int HeadingPixelSize = 9;

    private static readonly List<Texture2D> DrawnTextures = [];

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = FontSize };

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
