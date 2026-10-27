using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Tavern;
using Woad.Core.Woarld;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// The blacksmith's shack on the Woarld Map: everyone's worn gear, each mended at its owner's cost
/// (2 gold a point of wear), and the forge's wares for the leader to buy.
/// </summary>
public partial class SmithyPanel : Panel
{
    private const float Width = 300;
    private const float Line = 18;

    private readonly List<Control> _lines = [];
    private Label _title = null!;
    private Label _note = null!;
    private Button _close = null!;
    private WoarldPlace _smithy = null!;
    private IReadOnlyList<Character> _party = [];
    private Character _leader = null!;
    private ContentCatalog _content = null!;

    /// <summary>A mend or a sale went through (or was refused): the message, and whose things changed (null if nobody's).</summary>
    public event Action<ServiceResult, Character?>? Traded;

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

    public void Open(WoarldPlace smithy, IReadOnlyList<Character> party, Character leader, ContentCatalog content)
    {
        (_smithy, _party, _leader, _content) = (smithy, party, leader, content);
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
        _title.Text = _smithy.Name.ToUpperInvariant();
        _note.Text = $"Mending costs {Smithy.GoldPerWear} gold a point of wear, paid by whoever owns it. {_leader.Name} buys with {_leader.Gold} gold.";
        float y = 54;
        Heading(ref y, "MEND");
        bool any = false;
        foreach (Character owner in _party)
        {
            foreach (string key in owner.WornGear.ToList())
            {
                CardDefinition card = _content.GetCard(key);
                int price = Smithy.RepairPrice(key);
                AddLine(y, $"{owner.Name}'s {card.Name} (worn -{CardKey.Wear(key)}): {price} gold", card.Text, "Mend", owner.Gold >= price,
                    () => Trade(Smithy.Repair(_content, owner, key), owner));
                y += Line;
                any = true;
            }
        }

        if (!any)
        {
            Label whole = UiTheme.Label(new Vector2(10, y + 3), new Vector2(Width - 20, 12), "Nothing the party carries needs mending.", color: Palette.TextDim);
            AddChild(whole);
            _lines.Add(whole);
            y += Line;
        }

        Heading(ref y, "FROM THE FORGE");
        foreach (string id in _smithy.Stock)
        {
            CardDefinition card = _content.GetCard(id);
            int price = TavernServices.BuyPrice(card, _leader.GuildStanding);
            AddLine(y, $"{card.Name}: {price} gold", card.Text, "Buy", _leader.Gold >= price,
                () => Trade(Smithy.Buy(_smithy, _content, _leader, id), _leader));
            y += Line;
        }

        y += 6;
        _close.Position = new Vector2(Width - 78, y);
        Size = new Vector2(Width, y + 26);
    }

    private void Heading(ref float y, string text)
    {
        Label heading = UiTheme.Label(new Vector2(10, y + 3), new Vector2(Width - 20, 12), text, color: Palette.TextDim);
        AddChild(heading);
        _lines.Add(heading);
        y += Line;
    }

    private void Trade(ServiceResult result, Character trader)
    {
        Traded?.Invoke(result, result.Done ? trader : null);
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
        button.Name = $"{action}{_lines.Count}";
        button.Disabled = !enabled;
        button.Pressed += pressed;
        AddChild(button);
        _lines.Add(button);
    }
}
