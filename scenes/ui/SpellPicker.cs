using Godot;
using Woad.Core.Content;

namespace Woad.Scenes.Ui;

/// <summary>
/// A small window of spells to pick one from, each in its dice's color: the basic spell a new rank of
/// Magic teaches, or a spell to cast. Spells that can't be picked show greyed out, saying why.
/// </summary>
public partial class SpellPicker : Panel
{
    private const float Width = 248;
    private const float Line = 18;

    private readonly List<Control> _lines = [];
    private readonly Dictionary<string, Button> _buttons = [];
    private Label _title = null!;
    private Button _cancel = null!;

    /// <summary>A spell was picked, by its id.</summary>
    public event Action<string>? Picked;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 40;
        MouseFilter = MouseFilterEnum.Stop;
        _title = UiTheme.Label(new Vector2(8, 5), new Vector2(Width - 16, 14), "", UiTheme.HeadingSize, Palette.Trim);
        AddChild(_title);
        _cancel = UiTheme.Button(Vector2.Zero, new Vector2(70, 18), "Cancel");
        _cancel.Pressed += () => Visible = false;
        AddChild(_cancel);
    }

    /// <summary>Shows the spells, each with its button text and, if it can't be picked, why.</summary>
    public void Open(string title, IEnumerable<(SpellDefinition Spell, string Text, string? Blocked)> spells)
    {
        foreach (Control line in _lines)
        {
            line.QueueFree();
        }

        _lines.Clear();
        _buttons.Clear();
        _title.Text = title;
        int i = 0;
        foreach ((SpellDefinition spell, string text, string? blocked) in spells)
        {
            var position = new Vector2(8 + i % 2 * 118, 24 + i / 2 * Line);
            Button button = UiTheme.Accent(UiTheme.CompactButton(position, new Vector2(114, 16), text), DieTypes.Tint(spell.DieType));
            button.Name = $"Spell_{spell.Id}";
            button.Disabled = blocked is not null;
            button.TooltipText = blocked ?? $"{spell.Name}: {spell.Text} Costs {spell.Cost} Power.";
            string id = spell.Id;
            button.Pressed += () =>
            {
                Visible = false;
                Picked?.Invoke(id);
            };
            AddChild(button);
            _lines.Add(button);
            _buttons[id] = button;
            i++;
        }

        float bottom = 24 + (i + 1) / 2 * Line + 4;
        _cancel.Position = new Vector2(Width - 78, bottom);
        Size = new Vector2(Width, bottom + 24);
        Visible = true;
    }

    /// <summary>For the click tests: the button for a spell, if it's listed.</summary>
    internal Button? ButtonFor(string spellId) => _buttons.GetValueOrDefault(spellId);
}
