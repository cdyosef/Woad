using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Trading with a freed merchant in the dungeon: their wares, each with what they ask the hero (better
/// prices for better standing with the Merchant Guild), with Buy; and the hero's own belt and backpack,
/// each with what the merchant pays, with Sell. Paid in the gold found this run.
/// </summary>
public partial class MerchantPanel : Panel
{
    private const float Width = 260;
    private const float Line = 18;

    private readonly List<Control> _lines = [];
    private Label _title = null!;
    private Button _close = null!;

    public event Action<string>? BuyRequested;

    public event Action<int>? SellRequested;

    public event Action? CloseRequested;

    /// <summary>The merchant being traded with, while the panel is open.</summary>
    public int? MerchantId { get; private set; }

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 30;
        _title = UiTheme.Label(new Vector2(8, 6), new Vector2(Width - 16, 14), "", UiTheme.HeadingSize, Palette.Trim);
        AddChild(_title);
        _close = UiTheme.Button(Vector2.Zero, new Vector2(70, 18), "Close");
        _close.Pressed += () => CloseRequested?.Invoke();
        AddChild(_close);
    }

    public void ShowMerchant(Merchant merchant, Hero hero, ContentCatalog content)
    {
        MerchantId = merchant.Id;
        foreach (Control line in _lines)
        {
            line.QueueFree();
        }

        _lines.Clear();
        int standing = GameSession.GuildStandingOf(hero);
        _title.Text = merchant.Name.ToUpperInvariant();
        float y = 24;
        AddNote(y, $"{hero.Name}: {hero.Gold} gold, Guild standing {standing}.");
        y += Line;
        foreach (IGrouping<string, string> ware in merchant.Stock.GroupBy(id => id).OrderBy(group => content.GetCard(group.Key).Name))
        {
            CardDefinition card = content.GetCard(ware.Key);
            int price = GameSession.MerchantPrice(card, standing);
            string text = $"{card.Name}{(ware.Count() > 1 ? $" x{ware.Count()}" : "")}: {price} gold";
            AddLine(y, text, card.Text, "Buy", hero.Gold >= price && hero.CanCarry(card), () => BuyRequested?.Invoke(card.Id));
            y += Line;
        }

        if (merchant.Stock.Count == 0)
        {
            AddNote(y, "Sold out.");
            y += Line;
        }

        AddNote(y, "SELL");
        y += Line;
        foreach (CardInstance card in hero.Belt.Concat(hero.Backpack))
        {
            AddLine(y, $"{CardSlot.Title(card)}: {GameSession.MerchantPays(card.Definition)} gold", card.Definition.Text, "Sell", true, () => SellRequested?.Invoke(card.Id));
            y += Line;
        }

        y += 6;
        _close.Position = new Vector2(Width - 78, y);
        Size = new Vector2(Width, y + 26);
        Visible = true;
    }

    public void Close()
    {
        MerchantId = null;
        Visible = false;
    }

    private void AddNote(float y, string text)
    {
        Label note = UiTheme.Label(new Vector2(10, y + 3), new Vector2(Width - 20, 12), text, color: Palette.TextDim);
        AddChild(note);
        _lines.Add(note);
    }

    private void AddLine(float y, string text, string tooltip, string action, bool enabled, Action pressed)
    {
        Label name = UiTheme.Label(new Vector2(10, y + 3), new Vector2(Width - 76, 12), text);
        name.TooltipText = tooltip;
        name.MouseFilter = MouseFilterEnum.Pass;
        name.ClipText = true;
        AddChild(name);
        _lines.Add(name);
        Button button = UiTheme.Button(new Vector2(Width - 62, y), new Vector2(54, 16), action);
        button.Disabled = !enabled;
        button.Pressed += pressed;
        AddChild(button);
        _lines.Add(button);
    }
}
