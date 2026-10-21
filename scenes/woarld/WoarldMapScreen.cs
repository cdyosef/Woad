using Godot;
using Woad.Core.Characters;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Woarld;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// The Woarld Map: the party walks it in a line behind their leader (arrow keys or WASD, or click
/// where to go), clears the clouds as they explore, rests, and raids the dungeons they've opened.
/// </summary>
public partial class WoarldMapScreen : Control
{
    /// <summary>Seconds between steps while a direction key is held.</summary>
    private const double HeldStepSeconds = 0.13;

    /// <summary>Seconds between steps of a walk to a clicked tile.</summary>
    private const double PathStepSeconds = 0.08;

    private const float BarHeight = 32;
    private const float ButtonsTop = 330;

    private static readonly (Key Key, Direction Direction)[] Keys =
    [
        (Key.Up, Direction.North), (Key.W, Direction.North),
        (Key.Right, Direction.East), (Key.D, Direction.East),
        (Key.Down, Direction.South), (Key.S, Direction.South),
        (Key.Left, Direction.West), (Key.A, Direction.West),
    ];

    private readonly Queue<Direction> _path = new();
    private readonly IRandomSource _random = new Pcg32((ulong)DateTime.UtcNow.Ticks, sequence: 7);
    private WoarldMapView _view = null!;
    private Label _status = null!;
    private Label _message = null!;
    private double _stepCooldown;

    /// <summary>The journey to walk. Set before adding the screen to the tree.</summary>
    public WoarldJourney Journey { get; init; } = null!;

    /// <summary>The party in seat order.</summary>
    public IReadOnlyList<Character> Party { get; init; } = [];

    /// <summary>Who leads (an index into <see cref="Party"/>): a player, not a companion.</summary>
    public int Leader { get; set; }

    /// <summary>Raiding starts a new Dungeon Run, replacing the saved one, so Begin Raid? says so.</summary>
    public bool ReplacesSavedRun { get; init; }

    /// <summary>Shown when the map opens, such as news of a place that just opened.</summary>
    public string Greeting { get; init; } = "";

    public event Action? BackRequested;

    /// <summary>The party set out to raid a dungeon.</summary>
    public event Action<WoarldPlace>? RaidRequested;

    /// <summary>The journey moved on (a step, a rest, a new leader): time to save it.</summary>
    public event Action? JourneyChanged;

    internal Button BackButton { get; private set; } = null!;

    internal Button LeaderButton { get; private set; } = null!;

    internal Button RestButton { get; private set; } = null!;

    internal Button EnterButton { get; private set; } = null!;

    internal BeginRaidDialog BeginRaid { get; private set; } = null!;

    internal WoarldMapView View => _view;

    internal string Message => _message.Text;

    internal bool IsWalking => _path.Count > 0;

    /// <summary>The party in walking order: the leader, then everyone else in seat order.</summary>
    private IEnumerable<Character> WalkingOrder => Party.Skip(Leader).Take(1).Concat(Party.Where((_, index) => index != Leader));

    public override void _Ready()
    {
        Size = new Vector2(MenuScreen.Width, MenuScreen.Height);
        AddChild(new ColorRect { Color = Palette.Background, Size = Size, MouseFilter = MouseFilterEnum.Ignore });

        _view = new WoarldMapView { Position = new Vector2(0, BarHeight), Size = new Vector2(Size.X, ButtonsTop - 4 - BarHeight) };
        _view.TileClicked += WalkTo;
        AddChild(_view);

        AddChild(new Panel { Size = new Vector2(Size.X, BarHeight), MouseFilter = MouseFilterEnum.Ignore });
        _status = UiTheme.Label(new Vector2(8, 2), new Vector2(Size.X - 16, 14), color: Palette.TrimBright);
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        AddChild(_status);
        _message = UiTheme.Label(new Vector2(8, 16), new Vector2(Size.X - 16, 14), Greeting, color: Palette.Text);
        _message.AutowrapMode = TextServer.AutowrapMode.Off;
        _message.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(_message);

        AddChild(new Panel { Position = new Vector2(0, ButtonsTop - 4), Size = new Vector2(Size.X, Size.Y - ButtonsTop + 4), MouseFilter = MouseFilterEnum.Ignore });
        BackButton = AddButton(new Vector2(8, ButtonsTop), 80, "Back", "Back to the Dungeon Run list. Your travels are saved.");
        BackButton.Pressed += () => BackRequested?.Invoke();
        LeaderButton = AddButton(new Vector2(94, ButtonsTop), 150, "", "Who walks at the head of the line. Click for the next player.");
        LeaderButton.Pressed += NextLeader;
        RestButton = AddButton(new Vector2(250, ButtonsTop), 80, "Rest", $"Rest for {WoarldJourney.RestSteps} steps: everyone rolls a d6, and a 6 heals a heart and rolls again. Every 10 rolled in all heals a heart too.");
        RestButton.Pressed += Rest;
        EnterButton = AddButton(new Vector2(336, ButtonsTop), 296, "", "");
        EnterButton.Pressed += Enter;

        BeginRaid = new BeginRaidDialog();
        BeginRaid.Raided += () =>
        {
            if (Journey.PlaceHere is { Kind: PlaceKind.Dungeon } place)
            {
                RaidRequested?.Invoke(place);
            }
        };
        AddChild(BeginRaid);

        if (Greeting.Length == 0)
        {
            Say(Journey.PlaceHere is { } here ? $"You're at {here.Name}. {here.Description}" : "Walk with the arrow keys or WASD, or click where to go.");
        }

        Refresh();
    }

    public override void _Process(double delta)
    {
        _stepCooldown -= delta;
        if (_stepCooldown > 0 || BeginRaid.Visible)
        {
            return;
        }

        if (_path.Count > 0)
        {
            TakeStep(_path.Dequeue());
            _stepCooldown = PathStepSeconds;
            return;
        }

        foreach ((Key key, Direction direction) in Keys)
        {
            if (Input.IsKeyPressed(key))
            {
                _view.Destination = null;
                TakeStep(direction);
                _stepCooldown = HeldStepSeconds;
                return;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space } && !BeginRaid.Visible && !EnterButton.Disabled && EnterButton.Visible)
        {
            Enter();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Walks a set route, a step at a time (the click tests follow the road this way).</summary>
    internal void Walk(IEnumerable<Direction> route)
    {
        foreach (Direction step in route)
        {
            _path.Enqueue(step);
        }
    }

    /// <summary>Walks the shortest cleared way to a tile, a step at a time.</summary>
    internal void WalkTo(GridPosition tile)
    {
        if (BeginRaid.Visible)
        {
            return;
        }

        _path.Clear();
        _view.Destination = null;
        if (tile == Journey.Leader)
        {
            return;
        }

        if (Journey.PathTo(tile) is not { } path)
        {
            Say(!Journey.IsRevealed(tile) ? "The clouds hide that way. Explore nearer to clear them." : "There's no way there.");
            return;
        }

        foreach (Direction step in path)
        {
            _path.Enqueue(step);
        }

        _view.Destination = tile;
        _view.QueueRedraw();
    }

    private void TakeStep(Direction direction)
    {
        StepResult result = Journey.Step(direction);
        if (!result.Moved)
        {
            _path.Clear();
            _view.Destination = null;
            Say(result.Error!);
            Refresh();
            return;
        }

        Sfx.Play("step");
        if (Journey.Leader == _view.Destination)
        {
            _view.Destination = null;
        }

        if (result.Arrived is { } place)
        {
            _path.Clear();
            _view.Destination = null;
            Say($"{place.Name}. {place.Description}");
        }
        else if (result.NewDay)
        {
            Say($"Dawn breaks on day {Journey.Day}.");
        }
        else if (result.NightFell)
        {
            Say("Night falls.");
        }

        JourneyChanged?.Invoke();
        Refresh();
    }

    private void Rest()
    {
        _path.Clear();
        _view.Destination = null;
        List<Character> walkers = [.. WalkingOrder];
        (IReadOnlyList<RestRoll> rolls, bool newDay, _) = Journey.Rest(_random, walkers.Count);
        Sfx.Play("dice");
        IEnumerable<string> lines = walkers.Zip(rolls, (hero, roll) =>
            $"{hero.Name} {string.Join(" ", roll.Dice)}{(roll.Hearts > 0 ? $" (+{UiTheme.Count(roll.Hearts, "heart", "hearts")})" : "")}");
        Say($"{(newDay ? $"Rested into day {Journey.Day}" : "Rested")}: {string.Join(", ", lines)}.");
        JourneyChanged?.Invoke();
        Refresh();
    }

    private void NextLeader()
    {
        List<int> players = [.. Party.Select((hero, index) => (hero, index)).Where(seat => !seat.hero.IsCompanion).Select(seat => seat.index)];
        if (players.Count < 2)
        {
            return;
        }

        Leader = players[(players.IndexOf(Leader) + 1) % players.Count];
        Say($"{Party[Leader].Name} leads the way.");
        JourneyChanged?.Invoke();
        Refresh();
    }

    private void Enter()
    {
        if (Journey.PlaceHere is not { } place)
        {
            return;
        }

        _path.Clear();
        switch (place.Kind)
        {
            case PlaceKind.Dungeon when !Journey.IsOpen(place):
                string waitsOn = Journey.Map.Places.FirstOrDefault(other => other.ThemeId == place.UnlockedBy)?.Name ?? "another dungeon";
                Say($"{place.Name} is sealed. Beat {waitsOn} to open it.");
                break;
            case PlaceKind.Dungeon:
                BeginRaid.Ask($"{place.Name}, with {UiTheme.Count(Party.Count, "hero", "heroes")}. A raid takes the rest of the day." + (ReplacesSavedRun ? " This replaces your saved run." : ""));
                break;
            default:
                Say($"{place.Name} opens its doors with Raiding Party. Coming soon.");
                break;
        }
    }

    private void Refresh()
    {
        Character leader = Party[Leader];
        string time = Journey.IsDay ? $"Daylight, {Journey.StepsUntilChange} steps to nightfall" : $"Night, {Journey.StepsUntilChange} steps to dawn";
        _status.Text = $"{Journey.Map.Name.ToUpperInvariant()}   Day {Journey.Day}   {time}   {UiTheme.Count(Journey.TotalSteps, "step", "steps")} walked";
        LeaderButton.Text = $"Leader: {leader.Name}";
        LeaderButton.Disabled = Party.Count(hero => !hero.IsCompanion) < 2;

        WoarldPlace? here = Journey.PlaceHere;
        EnterButton.Visible = here is not null;
        if (here is not null)
        {
            bool raid = here.Kind == PlaceKind.Dungeon && Journey.IsOpen(here);
            EnterButton.Text = raid ? $"Raid {here.Name}" : $"Enter {here.Name}";
            EnterButton.TooltipText = here.Description;
        }

        _view.Party = [.. WalkingOrder.Select(hero => Looks.Resolve(hero.Appearance, hero.Portrait))];
        _view.Journey = Journey;
        _view.QueueRedraw();
    }

    private void Say(string text) => _message.Text = text;

    private Button AddButton(Vector2 position, float width, string text, string tooltip)
    {
        Button button = UiTheme.Button(position, new Vector2(width, 24), text);
        button.TooltipText = tooltip;
        AddChild(button);
        return button;
    }
}
