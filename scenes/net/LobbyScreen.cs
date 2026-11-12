using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Net;

/// <summary>
/// The Woarld Tree: playing together online. Give a name; Host a table on this machine, or Join one by its address.
/// At the table everyone picks the party they bring from their own saved characters (a hero of their own first, and
/// any companions with them), and sees who else is there and what they've brought. The host picks the dungeon and
/// starts the run for everyone.
/// </summary>
public partial class LobbyScreen : MenuScreen
{
    public NetLink Link { get; init; } = null!;

    public ICharacterRepository Repository { get; init; } = null!;

    public ContentCatalog Content { get; init; } = null!;

    /// <summary>The dungeons the host can pick, in order: id and name.</summary>
    public IReadOnlyList<(string Id, string Name)> Dungeons { get; init; } = [];

    public event Action? BackRequested;

    /// <summary>The recorded sessions, to watch.</summary>
    public event Action? ReplaysRequested;

    /// <summary>The host pressed Start: the dungeon picked.</summary>
    public event Action<string>? StartRequested;

    private LineEdit _name = null!;
    private LineEdit _address = null!;
    private LineEdit _port = null!;
    private Label _status = null!;
    private VBoxContainer _mine = null!;
    private VBoxContainer _table = null!;
    private Button _dungeon = null!;
    private int _dungeonIndex;
    private readonly List<Character> _characters = [];
    private readonly List<Character> _picked = [];

    internal Button HostButton { get; private set; } = null!;

    internal Button JoinButton { get; private set; } = null!;

    internal Button StartButton { get; private set; } = null!;

    internal LineEdit NameEdit => _name;

    internal LineEdit AddressEdit => _address;

    internal LineEdit PortEdit => _port;

    internal string Status => _status.Text;

    /// <summary>The characters this player has picked to bring, in seat order.</summary>
    public IReadOnlyList<Character> Picked => _picked;

    internal Button? CharacterButton(string name) => _mine.FindChild($"Pick{name.Replace(" ", "")}", recursive: true, owned: false) as Button;

    public override async void _Ready()
    {
        BuildFrame("THE WOARLD TREE");
        Caption(new Vector2(20, 44), "YOUR NAME");
        _name = Add(new LineEdit { Position = new Vector2(20, 58), Size = new Vector2(150, 24), Text = OS.GetEnvironment("USERNAME") is { Length: > 0 } user ? user : "Raider", MaxLength = 24 });
        Caption(new Vector2(20, 90), "HOST'S ADDRESS");
        _address = Add(new LineEdit { Position = new Vector2(20, 104), Size = new Vector2(94, 24), Text = "127.0.0.1", MaxLength = 64 });
        _port = Add(new LineEdit { Position = new Vector2(118, 104), Size = new Vector2(52, 24), Text = NetLink.DefaultPort.ToString(), MaxLength = 5 });
        HostButton = Add(UiTheme.Button(new Vector2(20, 136), new Vector2(72, 24), "Host"));
        HostButton.TooltipText = "Host a table on this machine: friends join with its address.";
        HostButton.Pressed += () => Say(Link.Host(Port, _name.Text.Trim()));
        JoinButton = Add(UiTheme.Button(new Vector2(98, 136), new Vector2(72, 24), "Join"));
        JoinButton.TooltipText = "Join the table hosted at the address above.";
        JoinButton.Pressed += () => Say(Link.Join(_address.Text.Trim(), Port, _name.Text.Trim()));
        _status = Add(UiTheme.Label(new Vector2(20, 168), new Vector2(150, 110), "Host a table, or join a friend's.", color: Palette.TextDim));
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        Caption(new Vector2(190, 44), "YOUR PARTY");
        var mineScroll = Add(new ScrollContainer { Position = new Vector2(190, 58), Size = new Vector2(210, 246), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        _mine = new VBoxContainer { CustomMinimumSize = new Vector2(196, 0) };
        mineScroll.AddChild(_mine);

        Caption(new Vector2(420, 44), "AT THE TABLE");
        var tableScroll = Add(new ScrollContainer { Position = new Vector2(420, 58), Size = new Vector2(200, 246), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        _table = new VBoxContainer { CustomMinimumSize = new Vector2(186, 0) };
        tableScroll.AddChild(_table);

        Button back = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        back.Pressed += () =>
        {
            Link.Leave();
            BackRequested?.Invoke();
        };
        Button replays = Add(UiTheme.Button(new Vector2(116, 316), new Vector2(88, 26), "Replays"));
        replays.Name = "Replays";
        replays.TooltipText = "Watch the sessions played online (the host records each one).";
        replays.Pressed += () =>
        {
            Link.Leave();
            ReplaysRequested?.Invoke();
        };
        _dungeon = Add(UiTheme.Button(new Vector2(300, 316), new Vector2(200, 26), ""));
        _dungeon.Pressed += () =>
        {
            _dungeonIndex = (_dungeonIndex + 1) % Math.Max(1, Dungeons.Count);
            Refresh();
        };
        StartButton = Add(UiTheme.Button(new Vector2(516, 316), new Vector2(102, 26), "Start"));
        StartButton.Pressed += () => StartRequested?.Invoke(Dungeons[_dungeonIndex].Id);

        Link.StatusChanged += OnStatus;
        Link.TableChanged += Refresh;
        Link.Disconnected += OnStatus;
        TreeExiting += () =>
        {
            Link.StatusChanged -= OnStatus;
            Link.TableChanged -= Refresh;
            Link.Disconnected -= OnStatus;
        };

        CharacterLoadResult loaded = await Repository.LoadAllAsync();
        _characters.AddRange(loaded.Characters);
        Refresh();
    }

    private int Port => int.TryParse(_port.Text.Trim(), out int port) && port is > 0 and < 65536 ? port : NetLink.DefaultPort;

    private void Say(string? problem)
    {
        if (problem is not null)
        {
            OnStatus(problem);
        }

        Refresh();
        SendParty();
    }

    private void OnStatus(string text)
    {
        _status.Text = text;
        Refresh();
    }

    /// <summary>The party as it will sit: this player's first hero, the companions picked (all following them), then any other heroes of theirs.</summary>
    private IReadOnlyList<Character> Seated()
    {
        List<Character> players = [.. _picked.Where(character => !character.IsCompanion)];
        return players.Count == 0 ? [] : [players[0], .. _picked.Where(character => character.IsCompanion), .. players.Skip(1)];
    }

    private void SendParty()
    {
        if (!Link.IsOnline)
        {
            return;
        }

        IReadOnlyList<Character> seated = Seated();
        IReadOnlyList<HeroSetup> heroes = seated.Count == 0 ? [] : Party.ToHeroSetups(seated);
        Link.Offer(SavedRun.HeroesToJson(heroes), [.. seated.Select(character => character.Name)]);
    }

    private void Toggle(Character character)
    {
        if (!_picked.Remove(character))
        {
            _picked.Add(character);
        }

        Refresh();
        SendParty();
    }

    private void Refresh()
    {
        bool online = Link.IsOnline;
        HostButton.Disabled = JoinButton.Disabled = online;
        _name.Editable = _address.Editable = _port.Editable = !online;

        foreach (Node old in _mine.GetChildren().Concat(_table.GetChildren()))
        {
            old.GetParent().RemoveChild(old);
            old.QueueFree();
        }

        if (_characters.Count == 0)
        {
            _mine.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(196, 28), "No saved characters yet: make one under Characters first.", color: Palette.TextDim));
        }

        bool hasHero = _picked.Any(character => !character.IsCompanion);
        foreach (Character character in _characters)
        {
            Button pick = UiTheme.CompactButton(Vector2.Zero, new Vector2(196, 20), $"{character.Name}  Lv {character.Level}{(character.IsCompanion ? "  (companion)" : "")}");
            pick.CustomMinimumSize = new Vector2(196, 20);
            pick.Name = $"Pick{character.Name.Replace(" ", "")}";
            pick.ToggleMode = true;
            pick.SetPressedNoSignal(_picked.Contains(character));
            pick.Disabled = character.IsCompanion && !hasHero && !_picked.Contains(character);
            pick.TooltipText = pick.Disabled ? "A companion needs a hero of yours to follow: pick one first." : "Click to bring them (or leave them behind).";
            pick.Pressed += () => Toggle(character);
            _mine.AddChild(pick);
        }

        IReadOnlyList<TablePlayer> players = Link.Players;
        if (players.Count == 0)
        {
            _table.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(186, 14), "Nobody yet.", color: Palette.TextDim));
        }

        foreach (TablePlayer player in players)
        {
            Label line = UiTheme.Label(Vector2.Zero, new Vector2(186, 30),
                $"{player.Name}{(player.Peer == NetLink.HostPeer ? " (host)" : "")}{(player.Peer == Link.MyPeer ? " (you)" : "")}\n  {(player.Party.Count == 0 ? "choosing..." : string.Join(", ", player.Party))}",
                color: player.Party.Count == 0 ? Palette.TextDim : Palette.Text);
            line.CustomMinimumSize = new Vector2(186, 30);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _table.AddChild(line);
        }

        int heroes = players.Sum(player => player.Party.Count);
        _dungeon.Visible = StartButton.Visible = Link.IsHost;
        _dungeon.Text = Dungeons.Count > 0 ? Dungeons[_dungeonIndex].Name : "";
        string? wait = players.Any(player => player.Party.Count == 0) ? "Everyone needs to pick a party first."
            : heroes > GameRules.MaxHeroes ? $"That's {heroes} at the table: {GameRules.MaxHeroes} is the most."
            : null;
        StartButton.Disabled = wait is not null || Dungeons.Count == 0;
        StartButton.TooltipText = wait ?? "Start the run for everyone at the table.";
    }
}
