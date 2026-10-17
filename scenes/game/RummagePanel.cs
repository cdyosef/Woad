using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A loot bag or an opened chest, rummaged through: what's in it, one line each (stacks with their
/// count), with Take (and Take 1 for a stack); the gold and keys there; and Take all, Put in (the card
/// selected in your belt or backpack) and Close.
/// </summary>
public partial class RummagePanel : Panel
{
    private const float Width = 232;
    private const float Line = 18;

    private readonly List<Control> _lines = [];
    private Label _title = null!;
    private Button _takeAll = null!;
    private Button _putIn = null!;
    private Button _close = null!;

    /// <summary>Take a card (or the gold and keys, for null); with a count, that many off a stack.</summary>
    public event Action<int?, int?>? TakeRequested;

    public event Action? TakeAllRequested;

    public event Action? PutInRequested;

    public event Action? CloseRequested;

    /// <summary>The tile being rummaged, while the panel is open.</summary>
    public GridPosition? Tile { get; private set; }

    internal Button TakeAllButton => _takeAll;

    internal Button PutInButton => _putIn;

    /// <summary>The Take button on the line for a card (or the gold and keys, for null).</summary>
    internal Button? TakeButton(int? cardId) =>
        _lines.OfType<Button>().FirstOrDefault(button => button.HasMeta("card") && (int)button.GetMeta("card") == (cardId ?? -1) && !button.HasMeta("one"));

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 30;
        _title = UiTheme.Label(new Vector2(8, 6), new Vector2(Width - 16, 14), "", UiTheme.HeadingSize, Palette.Trim);
        AddChild(_title);
        _takeAll = UiTheme.Button(Vector2.Zero, new Vector2(70, 18), "Take all");
        _takeAll.Pressed += () => TakeAllRequested?.Invoke();
        _putIn = UiTheme.Button(Vector2.Zero, new Vector2(70, 18), "Put in");
        _putIn.TooltipText = "Put the card selected in your belt or backpack in here.";
        _putIn.Pressed += () => PutInRequested?.Invoke();
        _close = UiTheme.Button(Vector2.Zero, new Vector2(70, 18), "Close");
        _close.Pressed += () => CloseRequested?.Invoke();
        AddChild(_takeAll);
        AddChild(_putIn);
        AddChild(_close);
    }

    /// <summary>Shows (or refreshes) what's on a tile: an opened chest's contents, or a pile on the floor.</summary>
    public void ShowPile(Room room, GridPosition tile, bool canPutIn)
    {
        Tile = tile;
        foreach (Control line in _lines)
        {
            line.QueueFree();
        }

        _lines.Clear();
        _title.Text = room.Layout[tile] == TileKind.Chest ? "CHEST" : "LOOT";
        float y = 26;
        foreach (CardInstance card in room.LootAt(tile))
        {
            AddLine(y, card.Count > 1 ? $"{card.Definition.Name} x{card.Count}" : card.Definition.Name, card.Id, card.Definition.Text, stack: card.Count > 1);
            y += Line;
        }

        int gold = room.GoldAt(tile), keys = room.KeysAt(tile);
        bool bossKey = room.BossKeyAt == tile;
        if (gold > 0 || keys > 0 || bossKey)
        {
            var parts = new List<string>();
            if (gold > 0)
            {
                parts.Add($"{gold} gold");
            }

            if (keys > 0)
            {
                parts.Add(keys == 1 ? "a key" : $"{keys} keys");
            }

            if (bossKey)
            {
                parts.Add("the Boss Key");
            }

            AddLine(y, string.Join(", ", parts), null, "", stack: false);
            y += Line;
        }

        if (room.BombAt(tile) is { Lit: false })
        {
            Label bomb = UiTheme.Label(new Vector2(10, y + 3), new Vector2(150, 12), "An unlit bomb (Take all)", color: Palette.TextDim);
            AddChild(bomb);
            _lines.Add(bomb);
            y += Line;
        }

        if (_lines.Count == 0)
        {
            Label empty = UiTheme.Label(new Vector2(10, y + 3), new Vector2(150, 12), "Empty.", color: Palette.TextDim);
            AddChild(empty);
            _lines.Add(empty);
            y += Line;
        }

        y += 6;
        _takeAll.Position = new Vector2(8, y);
        _putIn.Position = new Vector2(81, y);
        _putIn.Disabled = !canPutIn;
        _close.Position = new Vector2(154, y);
        Size = new Vector2(Width, y + 26);
        Visible = true;
    }

    public void Close()
    {
        Tile = null;
        Visible = false;
    }

    private void AddLine(float y, string text, int? cardId, string tooltip, bool stack)
    {
        Label name = UiTheme.Label(new Vector2(10, y + 3), new Vector2(stack ? 118 : 160, 12), text);
        name.TooltipText = tooltip;
        name.MouseFilter = MouseFilterEnum.Pass;
        name.ClipText = true;
        AddChild(name);
        _lines.Add(name);

        Button take = UiTheme.Button(new Vector2(Width - 56, y), new Vector2(48, 16), "Take");
        take.SetMeta("card", cardId ?? -1);
        take.Pressed += () => TakeRequested?.Invoke(cardId, null);
        AddChild(take);
        _lines.Add(take);
        if (stack)
        {
            Button one = UiTheme.Button(new Vector2(Width - 104, y), new Vector2(44, 16), "Take 1");
            one.SetMeta("card", cardId ?? -1);
            one.SetMeta("one", true);
            one.Pressed += () => TakeRequested?.Invoke(cardId, 1);
            AddChild(one);
            _lines.Add(one);
        }
    }
}

/// <summary>"How many?" for part of a stack: - and + around the count, then OK, All or Cancel.</summary>
public partial class CountPrompt : Panel
{
    private Label _question = null!;
    private Label _value = null!;
    private int _count;
    private int _max;
    private Action<int>? _chosen;

    internal Button OkButton { get; private set; } = null!;

    internal Button PlusButton { get; private set; } = null!;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 40;
        Size = new Vector2(200, 64);
        _question = UiTheme.Label(new Vector2(8, 6), new Vector2(184, 12), "");
        AddChild(_question);
        Button minus = UiTheme.Button(new Vector2(8, 22), new Vector2(18, 16), "-");
        minus.Pressed += () => SetCount(_count - 1);
        _value = UiTheme.Label(new Vector2(28, 24), new Vector2(24, 12), "");
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        PlusButton = UiTheme.Button(new Vector2(54, 22), new Vector2(18, 16), "+");
        PlusButton.Pressed += () => SetCount(_count + 1);
        OkButton = UiTheme.Button(new Vector2(8, 42), new Vector2(56, 16), "OK");
        OkButton.Pressed += () => Choose(_count);
        Button all = UiTheme.Button(new Vector2(70, 42), new Vector2(56, 16), "All");
        all.Pressed += () => Choose(_max);
        Button cancel = UiTheme.Button(new Vector2(132, 42), new Vector2(60, 16), "Cancel");
        cancel.Pressed += () => Visible = false;
        foreach (Control control in new Control[] { minus, _value, PlusButton, OkButton, all, cancel })
        {
            AddChild(control);
        }
    }

    /// <summary>Asks how many of up to <paramref name="max"/>, starting at 1.</summary>
    public void Ask(string question, int max, Action<int> chosen)
    {
        _question.Text = question;
        _max = max;
        _chosen = chosen;
        SetCount(1);
        Visible = true;
    }

    private void SetCount(int count)
    {
        _count = Math.Clamp(count, 1, _max);
        _value.Text = _count.ToString();
    }

    private void Choose(int count)
    {
        Visible = false;
        _chosen?.Invoke(count);
    }
}
