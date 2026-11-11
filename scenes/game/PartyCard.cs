using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A hero's block in the Party menu, a button: their portrait in a frame, top left; their crest centered on the frame's
/// right edge, its top level with the frame's; their name in the display font beside it; under the name a hearts meter
/// of small hearts, as many lines as it takes (full, lost, black and blue); their level and gold; and their vital stats
/// as symbols and numbers. Click it to put up their sheet.
/// </summary>
public partial class PartyCard : Button
{
    public const float BlockHeight = 58;

    private const float Frame = 44;
    private const float CrestWidth = 14;
    private const float CrestHeight = 16;
    private const float Heart = 6;
    private const float HeartStep = 7;

    private readonly Hero _hero;
    private readonly Crest _crest;
    private readonly LookColors? _look;
    private readonly string _status;

    public PartyCard(Hero hero, Crest crest, string status)
    {
        _hero = hero;
        _crest = crest;
        _status = status;
        _look = hero.Guise is null ? Looks.Resolve(hero.Appearance, hero.Portrait) : null;
        Name = $"Block{hero.Id}";
        Flat = true;
        FocusMode = FocusModeEnum.None;
        TooltipText = $"{hero.Name}: click to put up their sheet.";
        var name = UiTheme.Heading(UiTheme.Label(new Vector2(NameX, 0), new Vector2(200, 14), hero.Name, 9, Palette.Seat(hero.Seat)), 1);
        name.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(name);
    }

    /// <summary>Where the name starts: just right of the crest.</summary>
    private static float NameX => 2 + Frame + CrestWidth / 2 + 3;

    public override void _Draw()
    {
        bool hover = GetGlobalRect().HasPoint(GetGlobalMousePosition());
        DrawRect(new Rect2(Vector2.Zero, Size), hover ? Palette.PanelHover : Palette.PanelDark);
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Seat(_hero.Seat).Darkened(0.3f), filled: false, width: 1);

        // The portrait in its frame.
        var frame = new Rect2(2, 2, Frame, Frame);
        DrawRect(frame, Palette.Ink);
        bool down = _status.Length > 0;
        if (_look is { } look)
        {
            Looks.Draw(this, frame.Grow(-3), look, down ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
        }

        DrawRect(frame, Palette.Trim, filled: false, width: 2);
        DrawRect(frame.Grow(-2), Palette.TrimDim, filled: false, width: 1);
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        if (down)
        {
            DrawString(font, new Vector2(frame.Position.X, frame.End.Y - 3), _status.ToUpperInvariant(), HorizontalAlignment.Center, frame.Size.X, UiTheme.FontSize, Palette.Danger);
        }

        // The crest, centered on the frame's right edge, its top level with the frame's.
        Crests.Draw(this, new Rect2(frame.End.X - CrestWidth / 2, frame.Position.Y, CrestWidth, CrestHeight), _crest);

        // Level and gold, at the right of the name's line.
        string levelGold = $"Lv {_hero.Level}   {_hero.Coins}g";
        DrawString(font, new Vector2(0, 10), levelGold, HorizontalAlignment.Right, Size.X - 4, UiTheme.FontSize, Palette.Loot);

        // The hearts meter: blue (breath) first, then full, then lost, then black (lost for good) at the end.
        float left = NameX;
        int perLine = Math.Max(1, (int)((Size.X - left - 4) / HeartStep));
        int black = Math.Min(_hero.Conditions[Condition.BlackHeart], _hero.MaxHearts);
        int blue = Math.Min(_hero.BlueHearts, _hero.Hearts);
        int lines = 0;
        for (int i = 0; i < _hero.MaxHearts; i++)
        {
            Color color = i < blue ? HeartsView.Blue
                : i < _hero.Hearts ? Palette.Heart
                : i >= _hero.MaxHearts - black ? new Color(0.08f, 0.06f, 0.09f)
                : Palette.HeartEmpty;
            var at = new Vector2(left + i % perLine * HeartStep, 16 + i / perLine * HeartStep);
            HeartsView.DrawHeart(this, at, Heart, color);
            if (i >= _hero.MaxHearts - black && i >= _hero.Hearts)
            {
                DrawRect(new Rect2(at - Vector2.One * 0.5f, new Vector2(Heart + 1, Heart)), Palette.TextDim with { A = 0.35f }, filled: false, width: 0.5f);
            }

            lines = i / perLine + 1;
        }

        // The vital stats, as their symbols and numbers.
        Stats stats = _hero.EffectiveStats;
        (string Icon, string Value, Color Color)[] vitals =
        [
            ("icon_rumble", $"{stats.AttackDice}", Palette.DieRumble),
            ("icon_wend", $"{stats.MoveDice}", Palette.DieWend),
            ("icon_odhand", $"{stats.OdHandDice}", Palette.DieOdHand),
            ("icon_defense", $"{stats.DefenseDice}", Palette.DieDefense),
            ("icon_luck", $"{_hero.LuckRemaining}/{stats.Luck}", Palette.Heal),
            ("icon_magic", $"{stats.Magic}", new Color("b07ce8")),
        ];
        float y = Math.Max(16 + lines * HeartStep + 2, CrestHeight + 20);
        float x = left;
        foreach ((string icon, string value, Color color) in vitals)
        {
            if (GameArt.Ui(icon) is { } texture)
            {
                DrawTextureRect(texture, new Rect2(x, y, 9, 9), tile: false);
            }

            DrawString(font, new Vector2(x + 10, y + 8), value, HorizontalAlignment.Left, 30, UiTheme.FontSize, color);
            x += 12 + font.GetStringSize(value, HorizontalAlignment.Left, -1, UiTheme.FontSize).X + 4;
        }

        string armor = $"{_hero.ArmorTotal}";
        DrawString(font, new Vector2(x, y + 8), $"Armor {armor}", HorizontalAlignment.Left, 60, UiTheme.FontSize, Palette.DieDefense);
    }

    /// <summary>How tall the block needs to be for the hero's hearts.</summary>
    public static float HeightFor(Hero hero, float width)
    {
        int perLine = Math.Max(1, (int)((width - NameX - 4) / HeartStep));
        int lines = (hero.MaxHearts + perLine - 1) / perLine;
        return Math.Max(BlockHeight, 16 + lines * HeartStep + 2 + 12);
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit)
        {
            QueueRedraw();
        }
    }
}
