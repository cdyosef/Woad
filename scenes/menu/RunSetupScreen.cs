using Godot;
using Woad.Core.Characters;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>Pick 1-4 saved characters; the order they're picked in is the turn order.</summary>
public partial class RunSetupScreen : MenuScreen
{
    private readonly List<CharacterRow> _rosterRows = [];
    private readonly List<Character> _party = [];
    private readonly SeatButton[] _seats = new SeatButton[GameRules.MaxHeroes];
    private List<Character> _roster = [];

    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private Label _hint = null!;
    private LineEdit _seed = null!;

    public ICharacterRepository Repository { get; init; } = null!;

    public event Action? BackRequested;

    public event Action? NewCharacterRequested;

    /// <summary>The chosen characters in seat order, and the seed for the dungeon and dice.</summary>
    public event Action<IReadOnlyList<Character>, ulong>? StartRequested;

    internal Button StartButton { get; private set; } = null!;

    internal bool IsLoaded { get; private set; }

    internal int PartySize => _party.Count;

    internal CharacterRow RosterRow(int index) => _rosterRows[index];

    internal SeatButton Seat(int index) => _seats[index];

    public override void _Ready()
    {
        BuildFrame("CHOOSE YOUR PARTY");

        Caption(new Vector2(20, 40), "ROSTER");
        var scroll = Add(new ScrollContainer
        {
            Position = new Vector2(20, 52),
            Size = new Vector2(300, 238),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        });
        _list = new VBoxContainer { CustomMinimumSize = new Vector2(288, 0) };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);
        _empty = Add(UiTheme.Label(new Vector2(24, 60), new Vector2(290, 40), "You need a character to play. Create one first.", color: Palette.TextDim));

        Caption(new Vector2(338, 40), "SEATS, IN TURN ORDER", 260);
        for (int i = 0; i < _seats.Length; i++)
        {
            int seat = i;
            _seats[i] = Add(new SeatButton { Seat = i, Position = new Vector2(338, 52 + i * 44), Size = new Vector2(280, 40) });
            _seats[i].Pressed += () =>
            {
                if (seat < _party.Count)
                {
                    _party.RemoveAt(seat);
                    Refresh();
                }
            };
        }

        Caption(new Vector2(338, 234), "SEED");
        _seed = Add(new LineEdit { Position = new Vector2(338, 246), Size = new Vector2(150, 22), PlaceholderText = "random" });
        _hint = Add(UiTheme.Label(new Vector2(338, 274), new Vector2(280, 30), color: Palette.TextDim));

        Button back = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        back.Pressed += () => BackRequested?.Invoke();
        Button create = Add(UiTheme.Button(new Vector2(114, 316), new Vector2(130, 26), "New character"));
        create.Pressed += () => NewCharacterRequested?.Invoke();
        StartButton = Add(UiTheme.Button(new Vector2(508, 316), new Vector2(110, 26), "Start run"));
        StartButton.Pressed += Start;

        Load();
    }

    private async void Load()
    {
        CharacterLoadResult result = await Repository.LoadAllAsync();
        _roster = [.. result.Characters];
        foreach (Character character in _roster)
        {
            var row = new CharacterRow();
            row.Pressed += () => Toggle(character);
            _list.AddChild(row);
            _rosterRows.Add(row);
        }

        _empty.Visible = _roster.Count == 0;
        Refresh();
        IsLoaded = true;
    }

    private void Toggle(Character character)
    {
        if (_party.Contains(character))
        {
            _party.Remove(character);
        }
        else if (_party.Count < GameRules.MaxHeroes)
        {
            _party.Add(character);
        }

        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < _rosterRows.Count; i++)
        {
            _rosterRows[i].ShowCharacter(_roster[i], _party.Contains(_roster[i]));
        }

        for (int i = 0; i < _seats.Length; i++)
        {
            _seats[i].ShowCharacter(i < _party.Count ? _party[i] : null);
        }

        StartButton.Disabled = _party.Count == 0;
        _hint.Text = _party.Count == 0
            ? "Click characters to seat them. Players take turns at the same mouse."
            : $"{_party.Count} player{(_party.Count == 1 ? "" : "s")}. Click a seat to free it.";
    }

    private void Start()
    {
        ulong seed = (ulong)DateTime.UtcNow.Ticks;
        string text = _seed.Text.Trim();
        if (text.Length > 0 && !ulong.TryParse(text, out seed))
        {
            _hint.Text = "A seed must be a whole number, or leave it empty for a random run.";
            return;
        }

        StartRequested?.Invoke([.. _party], seed);
    }
}
