using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Trading with an ally beside you: your things on the left (Give), theirs on the right (Take), each
/// with gold and keys. For your own companion, its cards also have Equip, Belt and Stow, so you can
/// dress it. Loot lying under them can be opened from here too.
/// </summary>
public partial class TradePanel : Panel
{
    private const float Width = 288;
    private const float Column = 142;
    private const float Line = 16;

    private readonly List<Control> _lines = [];
    private Button _close = null!;
    private Button _loot = null!;

    public event Action<GameCommandFactory>? CommandRequested;

    /// <summary>Asks how many of something (gold) before trading it.</summary>
    public event Action<string, int, Action<int>>? CountRequested;

    public event Action? LootRequested;

    public event Action? CloseRequested;

    /// <summary>The ally being traded with, while the panel is open.</summary>
    public int? PartnerId { get; private set; }

    /// <summary>Builds a command for the active hero's id.</summary>
    public delegate GameCommand GameCommandFactory(int heroId);

    internal Button? Button(string name) => _lines.OfType<Button>().FirstOrDefault(button => button.Name == name);

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 30;
        _close = UiTheme.Button(Vector2.Zero, new Vector2(60, 16), "Close");
        _close.Pressed += () => CloseRequested?.Invoke();
        _loot = UiTheme.Button(Vector2.Zero, new Vector2(110, 16), "Loot under them");
        _loot.Pressed += () => LootRequested?.Invoke();
        AddChild(_close);
        AddChild(_loot);
    }

    public void ShowTrade(Hero you, Hero them, bool lootUnderThem)
    {
        PartnerId = them.Id;
        foreach (Control line in _lines)
        {
            RemoveChild(line);
            line.QueueFree();
        }

        _lines.Clear();
        bool yours = them.OwnerId == you.Id;
        float left = Side(you, them, 4, giving: true, dress: false);
        float right = Side(them, you, 4 + Column, giving: false, dress: yours);
        float y = Math.Max(left, right) + 4;
        _close.Position = new Vector2(Width - 68, y);
        _loot.Position = new Vector2(8, y);
        _loot.Visible = lootUnderThem;
        Size = new Vector2(Width, y + 22);
        Visible = true;
    }

    public void Close()
    {
        PartnerId = null;
        Visible = false;
    }

    /// <summary>One side: its owner's name, gold, keys, then every card (marked where it's held).</summary>
    private float Side(Hero owner, Hero other, float x, bool giving, bool dress)
    {
        float y = 6;
        Add(UiTheme.Label(new Vector2(x + 4, y), new Vector2(Column - 8, 14), owner.Name, UiTheme.HeadingSize, Palette.Seat(owner.Seat)));
        y += 18;
        string verb = giving ? "Give" : "Take";
        int ownerId = owner.Id, otherId = other.Id;

        if (owner.Gold > 0)
        {
            Row(x, y, $"{owner.Gold} gold", Palette.Loot, (verb, $"{verb}Gold", () => CountRequested?.Invoke($"{verb} how much gold?", owner.Gold,
                amount => CommandRequested?.Invoke(heroId => giving ? (GameCommand)new GiveGold(heroId, otherId, amount) : new TakeGold(heroId, ownerId, amount)))));
            y += Line;
        }

        if (owner.Keys > 0)
        {
            Row(x, y, owner.Keys == 1 ? "1 key" : $"{owner.Keys} keys", Palette.Text,
                ($"{verb} 1", $"{verb}Key", () => CommandRequested?.Invoke(heroId => giving ? (GameCommand)new GiveKey(heroId, otherId) : new TakeKey(heroId, ownerId))));
            y += Line;
        }

        if (owner.HasBossKey)
        {
            Row(x, y, "The Boss Key", Palette.Danger,
                (verb, $"{verb}BossKey", () => CommandRequested?.Invoke(heroId => giving ? (GameCommand)new GiveKey(heroId, otherId, BossKey: true) : new TakeKey(heroId, ownerId, BossKey: true))));
            y += Line;
        }

        foreach (CardInstance card in owner.AllCards)
        {
            CardLocation where = owner.FindCard(card.Id)!.Value.Location;
            string mark = where switch { CardLocation.Equipped => "E ", CardLocation.Belt => "B ", _ => "" };
            string name = mark + (card.Count > 1 ? $"{card.Definition.Name} x{card.Count}" : card.Definition.Name);
            int cardId = card.Id;
            var buttons = new List<(string, string, Action)>
            {
                (verb, $"{verb}{cardId}", () => CountRequested?.Invoke($"{verb} how many {card.Definition.Name}?", card.Count,
                    count => CommandRequested?.Invoke(heroId => giving ? (GameCommand)new GiveCard(heroId, otherId, cardId, count) : new TakeCard(heroId, ownerId, cardId, count)))),
            };
            if (dress)
            {
                (string label, OutfitAction action)? outfit = where switch
                {
                    CardLocation.Equipped => ("Stow", OutfitAction.Stow),
                    _ when card.Definition.Slot is not null => ("Equip", OutfitAction.Equip),
                    CardLocation.Backpack when card.Definition.Usage == CardUsage.SingleUse => ("Belt", OutfitAction.Belt),
                    CardLocation.Belt => ("Stow", OutfitAction.Stow),
                    _ => null,
                };
                if (outfit is { } dressing)
                {
                    buttons.Insert(0, (dressing.label, $"{dressing.label}{cardId}", () => CommandRequested?.Invoke(heroId => new OutfitCompanion(heroId, ownerId, cardId, dressing.action))));
                }
            }

            Row(x, y, name, Palette.Text, [.. buttons]);
            y += Line;
        }

        if (owner.AllCards.Count() == 0 && owner.Gold == 0 && owner.Keys == 0 && !owner.HasBossKey)
        {
            Add(UiTheme.Label(new Vector2(x + 4, y + 2), new Vector2(Column - 8, 12), "Nothing.", color: Palette.TextDim));
            y += Line;
        }

        return y;
    }

    private void Row(float x, float y, string text, Color color, params (string Label, string Name, Action Pressed)[] buttons)
    {
        float buttonsWidth = buttons.Length * 32;
        Label label = Add(UiTheme.Label(new Vector2(x + 4, y + 2), new Vector2(Column - 8 - buttonsWidth, 12), text, color: color));
        label.ClipText = true;
        label.TooltipText = text;
        label.MouseFilter = MouseFilterEnum.Pass;
        for (int i = 0; i < buttons.Length; i++)
        {
            (string caption, string name, Action pressed) = buttons[i];
            Button button = Add(UiTheme.CompactButton(new Vector2(x + Column - 4 - (buttons.Length - i) * 32, y), new Vector2(30, 15), caption));
            button.Name = name;
            button.Pressed += pressed;
        }
    }

    private T Add<T>(T control) where T : Control
    {
        AddChild(control);
        _lines.Add(control);
        return control;
    }
}
