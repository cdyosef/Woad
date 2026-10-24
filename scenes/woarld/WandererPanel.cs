using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Tavern;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// Trading with a wandering merchant on the Woarld Map: their wares at the leader's price (kinder for
/// good standing with the Guild, and kinder still if they remember the leader's help), and the leader's
/// own things to sell. Paid in the leader's own gold.
/// </summary>
public partial class WandererPanel : Panel
{
    private const float Width = 300;
    private const float Line = 18;

    private readonly List<Control> _lines = [];
    private Label _title = null!;
    private Label _note = null!;
    private Button _close = null!;
    private RaidWorld _world = null!;
    private WanderingMerchant _wanderer = null!;
    private Character _trader = null!;
    private ContentCatalog _content = null!;

    /// <summary>A trade went through (or was refused): the message, and whether anything changed.</summary>
    public event Action<ServiceResult>? Traded;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 30;
        _title = UiTheme.Label(new Vector2(8, 6), new Vector2(Width - 16, 14), "", UiTheme.HeadingSize, Palette.Trim);
        AddChild(_title);
        _note = UiTheme.Label(new Vector2(10, 24), new Vector2(Width - 20, 26), "", color: Palette.TextDim);
        AddChild(_note);
        _close = UiTheme.Button(Vector2.Zero, new Vector2(70, 18), "Close");
        _close.Pressed += () => Visible = false;
        AddChild(_close);
    }

    public void Open(RaidWorld world, WanderingMerchant wanderer, Character trader, ContentCatalog content)
    {
        (_world, _wanderer, _trader, _content) = (world, wanderer, trader, content);
        Rebuild();
        Visible = true;
    }

    private void Rebuild()
    {
        foreach (Control line in _lines)
        {
            line.QueueFree();
        }

        _lines.Clear();
        bool remembers = _world.GratitudeOf(_wanderer, _trader.Id) > 0;
        _title.Text = _wanderer.Name.ToUpperInvariant() + (_world.Remembers(_wanderer) ? ", RESCUED BY THE PARTY" : ", A WANDERING MERCHANT");
        _note.Text = (remembers ? $"\"{_trader.Name}! I'd not be here but for you. Something off, for an old friend.\" " : "") +
                     $"{_trader.Name}: {_trader.Gold} gold, Guild standing {_trader.GuildStanding}. Guild level {_world.GuildLevel}.";
        float y = 54;
        foreach (IGrouping<string, string> ware in _wanderer.Stock.GroupBy(id => id).OrderBy(group => _content.GetCard(group.Key).Name).ToList())
        {
            CardDefinition card = _content.GetCard(ware.Key);
            int price = TavernServices.WandererPrice(_world, _wanderer, card, _trader);
            AddLine(y, $"{card.Name}{(ware.Count() > 1 ? $" x{ware.Count()}" : "")}: {price} gold", card.Text, "Buy", _trader.Gold >= price,
                () => Trade(TavernServices.BuyFromWanderer(_world, _wanderer, _content, _trader, card.Id)));
            y += Line;
        }

        Label sell = UiTheme.Label(new Vector2(10, y + 3), new Vector2(Width - 20, 12), "SELL", color: Palette.TextDim);
        AddChild(sell);
        _lines.Add(sell);
        y += Line;
        foreach (string id in _trader.Backpack.Concat(_trader.Belt).Distinct().ToList())
        {
            CardDefinition card = _content.GetCard(id);
            AddLine(y, $"{card.Name}: {TavernServices.SellPrice(card)} gold", card.Text, "Sell", true,
                () => Trade(TavernServices.SellToWanderer(_wanderer, _content, _trader, id)));
            y += Line;
        }

        y += 6;
        _close.Position = new Vector2(Width - 78, y);
        Size = new Vector2(Width, y + 26);
    }

    private void Trade(ServiceResult result)
    {
        Traded?.Invoke(result);
        Rebuild();
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
