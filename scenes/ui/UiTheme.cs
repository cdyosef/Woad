using Godot;

namespace Woad.Scenes.Ui;

/// <summary>The placeholder gothic look: dark panels with gold trim, square corners.</summary>
public static class UiTheme
{
    public const int FontSize = 8;
    public const int HeadingSize = 12;

    public static Theme Create()
    {
        var theme = new Theme { DefaultFontSize = FontSize };

        theme.SetColor("font_color", "Label", Palette.Text);
        theme.SetFontSize("normal_font_size", "RichTextLabel", FontSize);
        theme.SetColor("default_color", "RichTextLabel", Palette.Text);

        theme.SetStylebox("normal", "Button", Box(Palette.Panel, Palette.Trim));
        theme.SetStylebox("hover", "Button", Box(Palette.PanelHover, Palette.TrimBright));
        theme.SetStylebox("pressed", "Button", Box(Palette.PanelPressed, Palette.TrimBright, 2));
        theme.SetStylebox("hover_pressed", "Button", Box(Palette.PanelPressed, Palette.TrimBright, 2));
        theme.SetStylebox("disabled", "Button", Box(Palette.PanelDark, Palette.TrimDim));
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

        theme.SetStylebox("panel", "Panel", Box(Palette.Panel, Palette.Trim, 2));
        theme.SetStylebox("panel", "TooltipPanel", Box(Palette.PanelDark, Palette.Trim));
        theme.SetColor("font_color", "TooltipLabel", Palette.Text);
        theme.SetFontSize("font_size", "TooltipLabel", FontSize);

        return theme;
    }

    /// <summary>"1 die", "2 dice", "1 step", "3 steps".</summary>
    public static string Count(int amount, string singular, string plural) => $"{amount} {(amount == 1 ? singular : plural)}";

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
        return new Button
        {
            Position = position,
            Size = size,
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            ClipText = true,
        };
    }
}
