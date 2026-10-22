using Godot;
using Woad.Core.Characters;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>Pick 1-8 saved characters; the order they're picked in is the turn order.</summary>
public partial class RunSetupScreen : MenuScreen
{
    private readonly List<CharacterRow> _rosterRows = [];
    private readonly List<Character> _party = [];

    /// <summary>The player most recently seated: new companions join them.</summary>
    private Character? _lastPlayer;
    private readonly SeatButton[] _seats = new SeatButton[GameRules.MaxHeroes];
    private List<Character> _roster = [];

    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private Label _hint = null!;
    private LineEdit _seed = null!;

    /// <summary>What Raid! starts: the party and seed from the latest Start.</summary>
    private Action? _raid;

    public ICharacterRepository Repository { get; init; } = null!;

    public event Action? BackRequested;

    public event Action? NewCharacterRequested;

    /// <summary>The chosen characters in seat order, the seed for the dungeon and dice, and whether friendly fire is on.</summary>
    public event Action<IReadOnlyList<Character>, ulong, bool, string>? StartRequested;

    /// <summary>The dungeon the run will be in, picked on the Dungeon Run list; empty to set out somewhere else (named by <see cref="DungeonName"/>).</summary>
    public string ThemeId { get; init; } = "";

    /// <summary>The dungeon's name (or the Woarld Map's), shown at the bottom.</summary>
    public string DungeonName { get; init; } = "";

    internal Label DungeonLabel { get; private set; } = null!;

    /// <summary>Starting a run replaces the saved one, so Begin Raid? says so.</summary>
    public bool ReplacesSavedRun { get; init; }

    internal BeginRaidDialog BeginRaid { get; private set; } = null!;

    /// <summary>Heroes can hurt each other this run (see <see cref="Woad.Core.Rules.RunSetup.FriendlyFire"/>).</summary>
    public bool FriendlyFire { get; set; }

    internal Button FriendlyFireButton { get; private set; } = null!;

    internal Button StartButton { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

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
            // Two columns of four: seats 1-4 on the left, 5-8 on the right.
            var position = new Vector2(338 + i / 4 * 142, 52 + i % 4 * 44);
            _seats[i] = Add(new SeatButton { Seat = i, Position = position, Size = new Vector2(138, 40) });
            _seats[i].Pressed += () =>
            {
                if (seat < _party.Count)
                {
                    Unseat(_party[seat]);
                    Refresh();
                }
            };
        }

        Caption(new Vector2(338, 234), "SEED");
        _seed = Add(new LineEdit { Position = new Vector2(338, 246), Size = new Vector2(150, 22), PlaceholderText = "random" });
        FriendlyFireButton = Add(UiTheme.Button(new Vector2(496, 246), new Vector2(122, 22), ""));
        FriendlyFireButton.ToggleMode = true;
        FriendlyFireButton.TooltipText = "With friendly fire on, blasts catch your allies, and you can attack and shove them.";
        FriendlyFireButton.Pressed += () =>
        {
            FriendlyFire = !FriendlyFire;
            Refresh();
        };
        _hint = Add(UiTheme.Label(new Vector2(338, 274), new Vector2(280, 30), color: Palette.TextDim));

        BackButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();
        Button create = Add(UiTheme.Button(new Vector2(114, 316), new Vector2(130, 26), "New character"));
        create.Pressed += () => NewCharacterRequested?.Invoke();
        DungeonLabel = Add(UiTheme.Label(new Vector2(256, 316), new Vector2(246, 26), (ThemeId.Length > 0 ? "Dungeon: " : "Off to: ") + DungeonName, color: Palette.Trim));
        DungeonLabel.VerticalAlignment = VerticalAlignment.Center;
        StartButton = Add(UiTheme.Button(new Vector2(508, 316), new Vector2(110, 26), "Start run"));
        StartButton.Pressed += Start;
        BeginRaid = Add(new BeginRaidDialog());
        BeginRaid.Raided += () => _raid?.Invoke();

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
            Unseat(character);
        }
        else if (_party.Count >= GameRules.MaxHeroes)
        {
            _hint.Text = $"The party is full: at most {GameRules.MaxHeroes}.";
            return;
        }
        else if (!character.IsCompanion)
        {
            _party.Add(character);
            _lastPlayer = character;
        }
        else if (_lastPlayer is null)
        {
            _hint.Text = "Seat a player first: a companion joins the player seated most recently.";
            return;
        }
        else
        {
            // Right after its owner and the owner's other companions, so it takes its turn after theirs.
            int owner = _party.IndexOf(_lastPlayer);
            int end = owner + 1;
            while (end < _party.Count && _party[end].IsCompanion)
            {
                end++;
            }

            _party.Insert(end, character);
        }

        Refresh();
    }

    /// <summary>Frees a seat. A player leaving takes their companions along.</summary>
    private void Unseat(Character character)
    {
        int index = _party.IndexOf(character);
        int count = 1;
        while (!character.IsCompanion && index + count < _party.Count && _party[index + count].IsCompanion)
        {
            count++;
        }

        _party.RemoveRange(index, count);
        if (character == _lastPlayer)
        {
            _lastPlayer = _party.LastOrDefault(seated => !seated.IsCompanion);
        }
    }

    /// <summary>The player a seated companion belongs to: the nearest player seated before it.</summary>
    private Character? OwnerOf(Character companion) =>
        _party.Take(_party.IndexOf(companion)).LastOrDefault(seated => !seated.IsCompanion);

    private void Refresh()
    {
        for (int i = 0; i < _rosterRows.Count; i++)
        {
            _rosterRows[i].ShowCharacter(_roster[i], _party.Contains(_roster[i]));
        }

        for (int i = 0; i < _seats.Length; i++)
        {
            Character? seated = i < _party.Count ? _party[i] : null;
            int playerNumber = _party.Take(i + 1).Count(member => !member.IsCompanion);
            _seats[i].ShowCharacter(seated, seated is { IsCompanion: true } companion ? OwnerOf(companion)?.Name : null, playerNumber);
        }

        StartButton.Disabled = _party.Count == 0;
        FriendlyFireButton.SetPressedNoSignal(FriendlyFire);
        FriendlyFireButton.Text = FriendlyFire ? "Friendly fire: on" : "Friendly fire: off";
        int companions = _party.Count(seated => seated.IsCompanion);
        int players = _party.Count - companions;
        _hint.Text = _party.Count == 0
            ? "Click characters to seat them. Players take turns at the same mouse."
            : $"{UiTheme.Count(players, "player", "players")}{(companions > 0 ? ", " + UiTheme.Count(companions, "companion", "companions") : "")}. " +
              "A companion joins the player seated most recently. Click a seat to free it.";
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

        List<Character> party = [.. _party];
        _raid = () => StartRequested?.Invoke(party, seed, FriendlyFire, ThemeId);
        BeginRaid.Ask($"{DungeonName}, with {UiTheme.Count(party.Count, "hero", "heroes")}." + (ReplacesSavedRun ? " This replaces your saved run." : ""));
    }
}
