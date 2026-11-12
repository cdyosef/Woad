using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>What the hover box shows: a picture (a sprite, or a hero's look), a name, a short description, and how to interact.</summary>
public sealed record HoverInfo(string Title, string Text, string How = "", Texture2D? Picture = null, LookColors? Look = null, Color? TitleColor = null);

/// <summary>
/// At the top of the right panel: whatever the mouse is over, its picture beside its name, a short description and a
/// line on how to interact with it. With nothing under the mouse, what to do next. It grows to fit: nothing is cut off.
/// </summary>
public partial class HoverBox : Control
{
    private const float Picture = 24;

    /// <summary>It never gets shorter than this, so what's below doesn't jump about as the mouse moves (only long text grows it).</summary>
    public const float MinHeight = 62;

    private RichTextLabel _text = null!;
    private PictureFrame _frame = null!;
    private HoverInfo? _shown;

    /// <summary>Its height changed (with what it says): the panel below it moves.</summary>
    public event Action? Grew;

    internal string Shown => _text.Text;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _frame = new PictureFrame { Position = new Vector2(0, 1), Size = new Vector2(Picture, Picture), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_frame);
        _text = UiTheme.RichLabel(new Vector2(Picture + 4, 0), new Vector2(Size.X - Picture - 4, 10));
        _text.FitContent = true;
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.MouseFilter = MouseFilterEnum.Ignore;
        _text.Resized += Fit;
        AddChild(_text);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _text is not null)
        {
            _text.Size = new Vector2(Size.X - Picture - 4, _text.Size.Y);
            _text.CustomMinimumSize = new Vector2(Size.X - Picture - 4, 0);
        }
    }

    public void ShowInfo(HoverInfo info)
    {
        if (info == _shown)
        {
            return;
        }

        _shown = info;
        bool picture = info.Picture is not null || info.Look is not null;
        _frame.Visible = picture;
        _frame.Show(info.Picture, info.Look);
        float left = picture ? Picture + 4 : 0;
        _text.Position = new Vector2(left, 0);
        _text.Size = new Vector2(Size.X - left, 10);
        _text.CustomMinimumSize = new Vector2(Size.X - left, 0);
        string title = info.Title.Length > 0 ? $"[color={Palette.Hex(info.TitleColor ?? Palette.TrimBright)}]{Escape(info.Title)}[/color]\n" : "";
        string how = info.How.Length > 0 ? $"\n[color={Palette.Hex(Palette.Heal)}]{Escape(info.How)}[/color]" : "";
        _text.Text = $"{title}[color={Palette.Hex(Palette.Text)}]{Escape(info.Text)}[/color]{how}";
        Fit();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    private void Fit()
    {
        float height = Mathf.Max(MinHeight, Mathf.Max(_frame.Visible ? Picture + 2 : 0, _text.GetContentHeight()));
        if (!Mathf.IsEqualApprox(height, Size.Y))
        {
            Size = new Vector2(Size.X, height);
            Grew?.Invoke();
        }
    }

    /// <summary>The picture, in a frame.</summary>
    private sealed partial class PictureFrame : Control
    {
        private Texture2D? _sprite;
        private LookColors? _look;

        public void Show(Texture2D? sprite, LookColors? look)
        {
            _sprite = sprite;
            _look = look;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, Palette.PanelDark);
            if (_sprite is not null)
            {
                DrawTextureRect(_sprite, rect.Grow(-2), tile: false);
            }
            else if (_look is { } look)
            {
                Looks.Draw(this, rect.Grow(-2), look);
            }

            DrawRect(rect, Palette.TrimDim, filled: false, width: 1);
        }
    }
}
