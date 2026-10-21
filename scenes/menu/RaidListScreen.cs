using Godot;
using Woad.Core.Saves;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>Raiding Party's saved raids: continue one, delete one (click twice), or begin a new raid.</summary>
public partial class RaidListScreen : MenuScreen
{
    private readonly List<Button> _continueButtons = [];
    private readonly List<Button> _deleteButtons = [];
    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private RaidSave? _confirmingDelete;

    /// <summary>The raids, the most recently played first.</summary>
    public List<RaidSave> Raids { get; init; } = [];

    /// <summary>A line about a raid: who's in it, the day, where they are.</summary>
    public Func<RaidSave, string> Describe { get; init; } = raid => raid.Name;

    /// <summary>Why a raid couldn't be read or continued, shown at the top.</summary>
    public string Problem { get; init; } = "";

    public event Action? BackRequested;

    public event Action? NewRaidRequested;

    public event Action<RaidSave>? ContinueRequested;

    public event Action<RaidSave>? DeleteRequested;

    internal Button NewRaidButton { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

    internal Button ContinueButton(int index) => _continueButtons[index];

    internal Button DeleteButton(int index) => _deleteButtons[index];

    internal int RaidCount => Raids.Count;

    public override void _Ready()
    {
        BuildFrame("RAIDING PARTY");
        Add(UiTheme.Label(new Vector2(20, 40), new Vector2(600, 14), "Everyone on this PC shares one Woarld. Each raid keeps its own party, hearts and place in it.", color: Palette.TextDim));
        if (Problem.Length > 0)
        {
            Add(UiTheme.Label(new Vector2(20, 54), new Vector2(600, 14), Problem, color: Palette.Danger));
        }

        Caption(new Vector2(20, 72), "YOUR RAIDS");
        var scroll = Add(new ScrollContainer { Position = new Vector2(20, 86), Size = new Vector2(600, 220), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        _list = new VBoxContainer { CustomMinimumSize = new Vector2(588, 0) };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);
        _empty = Add(UiTheme.Label(new Vector2(24, 92), new Vector2(580, 30), "No raids yet. Begin a new one: the party sets out from The Waylay Inn.", color: Palette.TextDim));

        BackButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();
        NewRaidButton = Add(UiTheme.Button(new Vector2(470, 316), new Vector2(150, 26), "New Raid"));
        NewRaidButton.Pressed += () => NewRaidRequested?.Invoke();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        _continueButtons.Clear();
        _deleteButtons.Clear();
        foreach (RaidSave raid in Raids)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            Label label = UiTheme.Label(Vector2.Zero, new Vector2(420, 30), $"{raid.Name}: {Describe(raid)}");
            label.CustomMinimumSize = new Vector2(420, 30);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(label);

            Button resume = UiTheme.Button(Vector2.Zero, new Vector2(84, 26), "Continue");
            resume.CustomMinimumSize = new Vector2(84, 26);
            resume.Pressed += () => ContinueRequested?.Invoke(raid);
            row.AddChild(resume);
            _continueButtons.Add(resume);

            Button delete = UiTheme.Button(Vector2.Zero, new Vector2(72, 26), raid == _confirmingDelete ? "Sure?" : "Delete");
            delete.CustomMinimumSize = new Vector2(72, 26);
            delete.TooltipText = "Deletes the raid (its party, hearts and place). The characters and the Woarld stay.";
            delete.Pressed += () =>
            {
                if (_confirmingDelete != raid)
                {
                    _confirmingDelete = raid;
                    Rebuild();
                    return;
                }

                _confirmingDelete = null;
                Raids.Remove(raid);
                DeleteRequested?.Invoke(raid);
                Rebuild();
            };
            row.AddChild(delete);
            _deleteButtons.Add(delete);
            _list.AddChild(row);
        }

        _empty.Visible = Raids.Count == 0;
    }
}
