using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Tavern;
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
    private RichTextLabel _status = null!;
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

    /// <summary>What food and drink add to each hero's Rest roll (heroes in walking order); used up by the Rest. Null: nothing.</summary>
    public Func<IReadOnlyList<Character>, IReadOnlyList<RestBoost>>? RestBoosts { get; init; }

    /// <summary>What the Back button says it does.</summary>
    public string BackTooltip { get; init; } = "Back to the Dungeon Run list. Your travels are saved.";

    /// <summary>A hero's hearts now and at full, for the status bar; null when hearts don't carry over (a Dungeon Run).</summary>
    public Func<Character, (int Now, int Max)>? Hearts { get; init; }

    /// <summary>The Raiding Party's Woarld, whose wandering merchants are out on the map; null for none.</summary>
    public RaidWorld? World { get; init; }

    public ContentCatalog Content { get; init; } = ContentCatalog.LoadDefault();

    /// <summary>A hero bought or sold something from a wandering merchant: save them and the Woarld.</summary>
    public event Action<Character>? Traded;

    private WandererPanel _wandererPanel = null!;

    internal WandererPanel WandererPanel => _wandererPanel;

    private static readonly string[] WandererLooks = ["noble", "bard", "sultan", "halfling", "dwarf", "monk"];

    /// <summary>The wandering merchant the leader stands with, if any.</summary>
    private WanderingMerchant? WandererHere => World?.WandererAt(Journey.Leader, Content);

    public event Action? BackRequested;

    /// <summary>The party rested: the heroes (in walking order) and their Rest rolls, to heal them.</summary>
    public event Action<IReadOnlyList<Character>, IReadOnlyList<RestRoll>>? Rested;

    /// <summary>The party went into a place that isn't a dungeon (a tavern). Without a handler, such places aren't open yet.</summary>
    public event Action<WoarldPlace>? PlaceEntered;

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

    /// <summary>The party is on its way: steps still to take, or the last one still being walked.</summary>
    internal bool IsWalking => _path.Count > 0 || _view.Motion.IsMoving;

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
        _status = UiTheme.RichLabel(new Vector2(8, 2), new Vector2(Size.X - 16, 15));
        AddChild(_status);
        _message = UiTheme.Label(new Vector2(8, 16), new Vector2(Size.X - 16, 14), Greeting, color: Palette.Text);
        _message.AutowrapMode = TextServer.AutowrapMode.Off;
        _message.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(_message);

        AddChild(new Panel { Position = new Vector2(0, ButtonsTop - 4), Size = new Vector2(Size.X, Size.Y - ButtonsTop + 4), MouseFilter = MouseFilterEnum.Ignore });
        BackButton = AddButton(new Vector2(8, ButtonsTop), 80, "Back", BackTooltip);
        BackButton.Pressed += () => BackRequested?.Invoke();
        LeaderButton = AddButton(new Vector2(94, ButtonsTop), 150, "", "Who walks at the head of the line. Click for the next player.");
        LeaderButton.Pressed += NextLeader;
        RestButton = AddButton(new Vector2(250, ButtonsTop), 80, "Rest", $"Rest for {WoarldJourney.RestSteps} steps: everyone rolls a d6, and a 6 heals a heart and rolls again. Every 10 rolled in all heals a heart too.");
        RestButton.Pressed += Rest;
        EnterButton = AddButton(new Vector2(336, ButtonsTop), 296, "", "");
        EnterButton.Pressed += Enter;

        _wandererPanel = new WandererPanel { Position = new Vector2(Size.X / 2 - 150, BarHeight + 12) };
        _wandererPanel.Traded += result =>
        {
            Say(result.Message);
            if (result.Done)
            {
                Traded?.Invoke(Party[Leader]);
            }
        };
        AddChild(_wandererPanel);

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
            _view.Motion.StepSeconds = PathStepSeconds;
            return;
        }

        foreach ((Key key, Direction direction) in Keys)
        {
            if (Input.IsKeyPressed(key))
            {
                _view.Destination = null;
                TakeStep(direction);
                _stepCooldown = HeldStepSeconds;
                _view.Motion.StepSeconds = HeldStepSeconds;
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
        else if (WandererHere is { } wanderer)
        {
            _path.Clear();
            _view.Destination = null;
            Say(World!.GratitudeOf(wanderer, Party[Leader].Id) > 0
                ? $"{wanderer.Name}, the merchant you rescued, waves you over: \"Friends! Come and see what I've got.\""
                : $"You meet {wanderer.Name}, a wandering merchant of the Guild, with a pack of wares.");
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
        (IReadOnlyList<RestRoll> rolls, bool newDay, _) = Journey.Rest(_random, walkers.Count, RestBoosts?.Invoke(walkers));
        Sfx.Play("dice");
        Rested?.Invoke(walkers, rolls);
        IEnumerable<string> lines = walkers.Zip(rolls, (hero, roll) =>
            $"{hero.Name} {string.Join(" ", roll.Dice)}{(roll.Bonus > 0 ? $" +{roll.Bonus}" : "")}{(roll.Hearts > 0 ? $" (+{UiTheme.Count(roll.Hearts, "heart", "hearts")})" : "")}");
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
        if (WandererHere is { } wanderer && Journey.PlaceHere is null)
        {
            _path.Clear();
            _wandererPanel.Open(World!, wanderer, Party[Leader], Content);
            return;
        }

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
                if (PlaceEntered is null)
                {
                    Say($"{place.Name} opens its doors in Raiding Party.");
                }
                else
                {
                    PlaceEntered(place);
                }

                break;
        }
    }

    internal void Refresh()
    {
        Character leader = Party[Leader];
        string time = Journey.IsDay ? $"Daylight, {Journey.StepsUntilChange} steps to nightfall" : $"Night, {Journey.StepsUntilChange} steps to dawn";
        _status.Text = $"[color={Palette.Hex(Palette.TrimBright)}]{Journey.Map.Name.ToUpperInvariant()}[/color]  Day {Journey.Day}  {time}  {UiTheme.Count(Journey.TotalSteps, "step", "steps")} walked" +
                       (Hearts is null ? "" : "   " + string.Join("   ", WalkingOrder.Select(hero => $"{hero.Name} {UiTheme.Hearts(Hearts(hero).Now, Hearts(hero).Max)}")));
        LeaderButton.Text = $"Leader: {leader.Name}";
        LeaderButton.Disabled = Party.Count(hero => !hero.IsCompanion) < 2;

        WoarldPlace? here = Journey.PlaceHere;
        WanderingMerchant? wanderer = WandererHere;
        EnterButton.Visible = here is not null || wanderer is not null;
        if (here is not null)
        {
            bool raid = here.Kind == PlaceKind.Dungeon && Journey.IsOpen(here);
            EnterButton.Text = raid ? $"Raid {here.Name}" : $"Enter {here.Name}";
            EnterButton.TooltipText = here.Description;
        }
        else if (wanderer is not null)
        {
            EnterButton.Text = $"Trade with {wanderer.Name}";
            EnterButton.TooltipText = "Buy and sell with the leader's gold. Good standing with the Merchant Guild means better prices.";
        }

        if (wanderer is null)
        {
            _wandererPanel.Visible = false;
        }

        _view.Merchants = World is null ? [] : [.. World.Wanderers(Content).Select((merchant, index) =>
            (merchant.Position, Looks.Resolve(new Appearance(WandererLooks[(merchant.Name.Length + index) % WandererLooks.Length], Main: "gilt"), "")))];

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
