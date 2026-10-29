using Godot;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>
/// A card on the character sheet: the card's picture (res://assets/cards/{id}.png) over its name, in a
/// gilt frame that brightens when it's selected. An empty slot shows the back of a card instead, with
/// the slot's mark and name. Stacks show their count, charged cards their charges.
/// </summary>
public partial class SheetCard : CardSlot
{
    private string _emptyName = "";
    private string? _picture;
    private string? _caption;
    private Color? _accent;

    /// <summary>The card back shown while the slot is empty (back_head, back_bag...).</summary>
    public string Back { get; set; } = "back_bag";

    /// <summary>Drawn small (the belt's fan): picture only, no name.</summary>
    public bool Small { get; set; }

    public SheetCard()
    {
        Flat = true;
        ClipText = false;
    }

    public void Show(CardInstance? card, string emptyName, bool selected)
    {
        ShowCard(card, emptyName, selected);
        Text = "";
        _emptyName = emptyName;
        _picture = null;
        _caption = null;
        _accent = null;
        QueueRedraw();
    }

    /// <summary>A card that isn't an item: an ability, or the Magic card. Clickable unless <paramref name="enabled"/> is false.</summary>
    public void ShowFace(string picture, string caption, Color accent, bool enabled, string tooltip)
    {
        ShowCard(null, "", selected: false);
        Disabled = !enabled;
        Text = "";
        TooltipText = tooltip;
        _picture = picture;
        _caption = caption;
        _accent = accent;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        float art = Small ? Mathf.Min(Size.X - 4, 24) : 32;
        var picture = new Rect2((Size.X - art) / 2, Small ? 2 : 3, art, art);
        bool hover = IsHovered() && !Disabled;
        if (Card is null && _picture is null)
        {
            // The back of a card: the slot it's for.
            DrawRect(rect, new Color("241d33"));
            DrawRect(rect, new Color("5a4a6a"), filled: false, width: 1);
            DrawRect(rect.Grow(-2), new Color("2e2540"), filled: false, width: 1);
            if (GameArt.Texture($"res://assets/cards/{Back}.png") is { } back)
            {
                DrawTextureRect(back, picture, tile: false, modulate: new Color(1, 1, 1, 0.8f));
            }

            if (!Small)
            {
                DrawString(font, new Vector2(0, Size.Y - 5), _emptyName, HorizontalAlignment.Center, Size.X, UiTheme.FontSize, new Color("7a6a90"));
            }

            return;
        }

        bool lit = !Disabled;
        Color accent = _accent ?? Palette.Trim;
        DrawRect(rect, hover ? Palette.PanelHover : Palette.PanelDark);
        DrawRect(new Rect2(2, 2, Size.X - 4, picture.End.Y - 1), new Color("221c30"));
        string id = Card?.Definition.Id ?? _picture!;
        if (GameArt.Texture($"res://assets/cards/{id}.png") is { } texture)
        {
            DrawTextureRect(texture, picture, tile: false, modulate: lit ? Colors.White : new Color(0.55f, 0.55f, 0.55f));
        }

        bool selected = ButtonPressed;
        DrawRect(rect, selected ? Palette.TrimBright : lit ? accent : Palette.TrimDim, filled: false, width: selected ? 2 : 1);
        if (!Small)
        {
            string name = _caption ?? Card!.Definition.Name;
            DrawString(font, new Vector2(1, Size.Y - 5), name, HorizontalAlignment.Center, Size.X - 2, UiTheme.FontSize, lit ? Palette.Text : Palette.TextDim);
        }

        // A stack's count, charges left, or a crossbow waiting to be reloaded, in the corner.
        string? badge = Card is null ? null
            : Card.Definition.Usage == Woad.Core.Content.CardUsage.Charges ? $"{Card.ChargesRemaining}/{Card.Definition.Charges}"
            : Card.Unloaded ? "!"
            : Card.Count > 1 ? $"x{Card.Count}"
            : null;
        if (badge is not null)
        {
            float width = font.GetStringSize(badge, fontSize: UiTheme.FontSize).X + 4;
            var corner = new Rect2(Size.X - width - 1, 1, width, 10);
            DrawRect(corner, Palette.Ink);
            DrawString(font, corner.Position + new Vector2(2, 8), badge, fontSize: UiTheme.FontSize, modulate: Card!.Unloaded ? Palette.Danger : Palette.TrimBright);
        }

        if (Card is { Wear: > 0 } worn)
        {
            DrawString(font, new Vector2(2, 10), $"-{worn.Wear}", fontSize: UiTheme.FontSize, modulate: Palette.Danger);
        }
    }
}
