using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Tavern;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Tavern;

/// <summary>
/// Inside a tavern: the party walks in a line behind their leader (arrow keys or WASD, or click),
/// talks to the folk there (walk up to them or click them), and leaves by the door for the
/// Woarld Map. Talking opens a panel with what that person offers; anyone in the party can pay.
/// </summary>
public partial class TavernScreen : Control
{
    private const double HeldStepSeconds = 0.13;
    private const double PathStepSeconds = 0.08;
    private const float BarHeight = 32;
    private const float ButtonsTop = 330;
    private const float RowHeight = 16;

    private static readonly (Key Key, Direction Direction)[] Keys =
    [
        (Key.Up, Direction.North), (Key.W, Direction.North),
        (Key.Right, Direction.East), (Key.D, Direction.East),
        (Key.Down, Direction.South), (Key.S, Direction.South),
        (Key.Left, Direction.West), (Key.A, Direction.West),
    ];

    private readonly Queue<Direction> _path = new();
    private readonly IRandomSource _random = new Pcg32((ulong)DateTime.UtcNow.Ticks, sequence: 13);
    private TavernVisit _visit = null!;
    private TavernView _view = null!;
    private Label _status = null!;
    private Label _message = null!;
    private double _stepCooldown;

    /// <summary>Who to talk to once the walk there ends.</summary>
    private TavernNpc? _walkingTo;

    private Control _dialog = null!;
    private Label _dialogTitle = null!;
    private Label _dialogText = null!;
    private VBoxContainer _list = null!;
    private TavernNpc? _talkingTo;
    private Character _leader = null!;
    private Character _payer = null!;

    public TavernDefinition Tavern { get; init; } = null!;

    public RaidWorld World { get; init; } = null!;

    public ContentCatalog Content { get; init; } = null!;

    /// <summary>The party in seat order. The Innkeeper and Recruiter change it.</summary>
    public List<Character> Party { get; init; } = [];

    /// <summary>Every saved character, for the Innkeeper's rooms.</summary>
    public List<Character> Roster { get; init; } = [];

    /// <summary>Who leads (an index into <see cref="Party"/>).</summary>
    public int Leader
    {
        get => Math.Max(0, Party.IndexOf(_leader));
        init => _leader = Party.Count > 0 ? Party[Math.Clamp(value, 0, Party.Count - 1)] : null!;
    }

    public string Greeting { get; init; } = "";

    /// <summary>The party walked out of the door.</summary>
    public event Action? LeaveRequested;

    /// <summary>A character's gold or gear changed: save them.</summary>
    public event Action<Character>? CharacterChanged;

    /// <summary>The Woarld changed (bonds, reputation, stock, rumors, meals): save it.</summary>
    public event Action? WorldChanged;

    /// <summary>The party's members or leader changed.</summary>
    public event Action? PartyChanged;

    /// <summary>A new companion was hired: save them and add them to the roster.</summary>
    public event Action<Character>? RecruitHired;

    internal Button LeaderButton { get; private set; } = null!;

    internal Button TalkButton { get; private set; } = null!;

    internal Button DoorButton { get; private set; } = null!;

    internal Button PayerButton { get; private set; } = null!;

    internal Button ByeButton { get; private set; } = null!;

    internal Button RumorButton { get; private set; } = null!;

    internal TavernView View => _view;

    internal TavernVisit Visit => _visit;

    internal TavernNpc? TalkingTo => _talkingTo;

    internal string Message => _message.Text;

    internal string DialogText => _dialogText.Text;

    internal bool IsWalking => _path.Count > 0;

    internal Character Payer => _payer;

    /// <summary>A button in the talk panel's list, by its text (the first that starts with it).</summary>
    internal Button? ListButton(string text, int row = -1) =>
        _list.GetChildren().OfType<HBoxContainer>()
            .Where((_, index) => row < 0 || index == row)
            .SelectMany(line => line.GetChildren().OfType<Button>())
            .FirstOrDefault(button => button.Text.StartsWith(text, StringComparison.Ordinal));

    /// <summary>The rows of the talk panel's list, as text: each row's label.</summary>
    internal IEnumerable<string> ListRows => _list.GetChildren().OfType<HBoxContainer>().Select(line => line.GetChildren().OfType<Label>().FirstOrDefault()?.Text ?? "");

    private IEnumerable<Character> WalkingOrder => Party.Where(member => member == _leader).Concat(Party.Where(member => member != _leader));

    public override void _Ready()
    {
        Size = new Vector2(MenuScreen.Width, MenuScreen.Height);
        _payer = _leader;
        _visit = new TavernVisit(Tavern, Party.Count);
        AddChild(new ColorRect { Color = Palette.Background, Size = Size, MouseFilter = MouseFilterEnum.Ignore });

        _view = new TavernView { Position = new Vector2(0, BarHeight), Size = new Vector2(Size.X, ButtonsTop - 4 - BarHeight) };
        _view.TileClicked += Clicked;
        AddChild(_view);

        AddChild(new Panel { Size = new Vector2(Size.X, BarHeight), MouseFilter = MouseFilterEnum.Ignore });
        _status = UiTheme.Label(new Vector2(8, 2), new Vector2(Size.X - 16, 14), color: Palette.TrimBright);
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        AddChild(_status);
        _message = UiTheme.Label(new Vector2(8, 16), new Vector2(Size.X - 16, 14), Greeting.Length > 0 ? Greeting : $"Welcome to {Tavern.Name}. Walk up to anyone to talk, or click them.");
        _message.AutowrapMode = TextServer.AutowrapMode.Off;
        _message.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(_message);

        AddChild(new Panel { Position = new Vector2(0, ButtonsTop - 4), Size = new Vector2(Size.X, Size.Y - ButtonsTop + 4), MouseFilter = MouseFilterEnum.Ignore });
        DoorButton = AddButton(new Vector2(8, ButtonsTop), 140, "Out to the Woarld", "Leave by the door, out onto the Woarld Map.");
        DoorButton.Pressed += () =>
        {
            if (_visit.PathTo(Tavern.Start) is { } path)
            {
                _walkingTo = null;
                Enqueue(path);
                Enqueue([Direction.South]);
            }
        };
        LeaderButton = AddButton(new Vector2(154, ButtonsTop), 150, "", "Who walks at the head of the line. Click for the next player.");
        LeaderButton.Pressed += NextLeader;
        TalkButton = AddButton(new Vector2(310, ButtonsTop), 322, "", "");
        TalkButton.Pressed += () =>
        {
            if (_visit.NpcInReach is { } npc)
            {
                Talk(npc);
            }
        };

        BuildDialog();
        Refresh();
    }

    public override void _Process(double delta)
    {
        _stepCooldown -= delta;
        if (_stepCooldown > 0 || _dialog.Visible)
        {
            return;
        }

        if (_path.Count > 0)
        {
            TakeStep(_path.Dequeue());
            _stepCooldown = PathStepSeconds;
            if (_path.Count == 0 && _walkingTo is { } npc && _visit.NpcInReach == npc)
            {
                _walkingTo = null;
                Talk(npc);
            }

            return;
        }

        foreach ((Key key, Direction direction) in Keys)
        {
            if (Input.IsKeyPressed(key))
            {
                _walkingTo = null;
                TakeStep(direction);
                _stepCooldown = HeldStepSeconds;
                return;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.Escape && _dialog.Visible)
        {
            CloseDialog();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter or Key.Space && !_dialog.Visible && _visit.NpcInReach is { } npc)
        {
            Talk(npc);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>A click on someone walks over and talks to them; a click on the floor walks there.</summary>
    internal void Clicked(GridPosition tile)
    {
        if (_dialog.Visible)
        {
            return;
        }

        _path.Clear();
        _walkingTo = null;
        if (Tavern.NpcAt(tile) is { } npc)
        {
            if (_visit.NpcInReach == npc)
            {
                Talk(npc);
            }
            else if (Tavern.TalkingSpot(npc, _visit.Leader) is { } spot && _visit.PathTo(spot) is { } toThem)
            {
                _walkingTo = npc;
                Enqueue(toThem);
            }

            return;
        }

        if (_visit.PathTo(tile) is { } path)
        {
            Enqueue(path);
        }
    }

    private void Enqueue(IEnumerable<Direction> path)
    {
        foreach (Direction step in path)
        {
            _path.Enqueue(step);
        }
    }

    private void TakeStep(Direction direction)
    {
        if (_visit.Step(direction) is { } blocked)
        {
            _path.Clear();
            _walkingTo = null;
            Say(blocked);
            Refresh();
            return;
        }

        Sfx.Play("step");
        if (_visit.AtDoor)
        {
            _path.Clear();
            LeaveRequested?.Invoke();
            return;
        }

        Refresh();
    }

    private void NextLeader()
    {
        List<Character> players = [.. Party.Where(member => !member.IsCompanion)];
        if (players.Count < 2)
        {
            return;
        }

        _leader = players[(players.IndexOf(_leader) + 1) % players.Count];
        Say($"{_leader.Name} leads the way.");
        PartyChanged?.Invoke();
        Refresh();
    }

    private void Refresh()
    {
        if (!Party.Contains(_leader))
        {
            _leader = Party.First(member => !member.IsCompanion);
        }

        if (!Party.Contains(_payer))
        {
            _payer = _leader;
        }

        _visit.SetPartySize(Party.Count);
        _status.Text = $"{Tavern.Name.ToUpperInvariant()}   Reputation {World.Reputation(Tavern.Id)}   Gold: " +
                       string.Join(", ", Party.Select(member => $"{member.Name} {member.Gold}"));
        LeaderButton.Text = $"Leader: {_leader.Name}";
        LeaderButton.Disabled = Party.Count(member => !member.IsCompanion) < 2;
        TavernNpc? near = _visit.NpcInReach;
        TalkButton.Visible = near is not null;
        TalkButton.Text = near is null ? "" : $"Talk to {near.Name}";
        _view.Party = [.. WalkingOrder.Select(member => Looks.Resolve(member.Appearance, member.Portrait))];
        _view.Visit = _visit;
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

    // ---- Talking ------------------------------------------------------------------------------

    private void BuildDialog()
    {
        _dialog = new Control { Size = Size, Visible = false };
        _dialog.AddChild(new ColorRect { Color = new Color(Palette.Background, 0.6f), Size = Size });
        var panel = new Panel { Position = new Vector2(32, 36), Size = new Vector2(576, 288), MouseFilter = MouseFilterEnum.Ignore };
        _dialog.AddChild(panel);

        _dialogTitle = UiTheme.Heading(UiTheme.Label(new Vector2(14, 8), new Vector2(340, 20), "", 16, Palette.TrimBright), 2);
        panel.AddChild(_dialogTitle);
        PayerButton = UiTheme.Button(new Vector2(362, 10), new Vector2(200, 20), "");
        PayerButton.TooltipText = "Who's paying (and buying, selling or betting). Click for the next in the party.";
        PayerButton.Pressed += () =>
        {
            _payer = Party[(Party.IndexOf(_payer) + 1) % Party.Count];
            ShowTalk();
        };
        panel.AddChild(PayerButton);
        _dialogText = UiTheme.Label(new Vector2(14, 34), new Vector2(548, 30), color: Palette.Text);
        panel.AddChild(_dialogText);

        var scroll = new ScrollContainer { Position = new Vector2(14, 68), Size = new Vector2(548, 180), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list = new VBoxContainer { CustomMinimumSize = new Vector2(536, 0) };
        _list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_list);
        panel.AddChild(scroll);

        RumorButton = UiTheme.Button(new Vector2(14, 256), new Vector2(160, 22), "Any rumors?");
        RumorButton.Pressed += () =>
        {
            if (_talkingTo is { } npc && TavernServices.NextRumor(World, npc) is { } rumor)
            {
                Done(new ServiceResult(true, $"\"{rumor.Text}\"" + (rumor.Reveal is not null ? " (The Woarld Map shows where.)" : "")), changedCharacter: null);
            }
        };
        panel.AddChild(RumorButton);
        ByeButton = UiTheme.Button(new Vector2(462, 256), new Vector2(100, 22), "Bye");
        ByeButton.Pressed += CloseDialog;
        panel.AddChild(ByeButton);
        AddChild(_dialog);
    }

    internal void Talk(TavernNpc npc)
    {
        _path.Clear();
        _talkingTo = npc;
        _dialogText.Text = npc.Greeting;
        _dialog.Visible = true;
        ShowTalk();
    }

    private void CloseDialog()
    {
        _dialog.Visible = false;
        _talkingTo = null;
        Refresh();
    }

    /// <summary>Applies a service's outcome: says what happened and saves what changed.</summary>
    private void Done(ServiceResult result, Character? changedCharacter)
    {
        _dialogText.Text = result.Message;
        if (result.Done)
        {
            if (changedCharacter is not null)
            {
                Sfx.Play("coin");
                CharacterChanged?.Invoke(changedCharacter);
            }

            WorldChanged?.Invoke();
        }

        ShowTalk();
    }

    /// <summary>Rebuilds the talk panel for whoever's being talked to.</summary>
    private void ShowTalk()
    {
        if (_talkingTo is not { } npc)
        {
            return;
        }

        _dialogTitle.Text = $"{npc.Name.ToUpperInvariant()}";
        PayerButton.Text = $"Paying: {_payer.Name} ({_payer.Gold} gold)";
        RumorButton.Visible = npc.Rumors.Count > 0;
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        switch (npc.Role)
        {
            case NpcRole.Bartender:
                ShowBar();
                break;
            case NpcRole.Merchant:
                ShowMerchant();
                break;
            case NpcRole.Recruiter:
                ShowRecruits();
                break;
            case NpcRole.Innkeeper:
                ShowRooms();
                break;
            case NpcRole.Gambler:
                ShowBones();
                break;
        }

        Refresh();
    }

    private void ShowBar()
    {
        foreach (MenuItem item in Tavern.Menu)
        {
            HBoxContainer row = Row($"{item.Name}, {item.Price} gold: {item.Text}", 380);
            RowButton(row, item.Kind == "drink" ? "Drink" : "Eat", 60, () => Done(TavernServices.Eat(World, _payer, item), _payer));
            RowButton(row, "To-Go", 60, () => Done(TavernServices.BuyToGo(World, _payer, item), _payer));
        }

        int others = Party.Count - 1;
        int drink = Tavern.Menu.Where(item => item.Kind == "drink").Select(item => item.Price).DefaultIfEmpty(0).Min();
        Button party = RowButton(Row($"A round for the party ({drink * others} gold): everyone else drinks, and grows closer to {_payer.Name}.", 380), "Buy", 124,
            () => Done(TavernServices.RoundForTheParty(World, Tavern, _payer, Party), _payer));
        party.Disabled = others == 0;
        RowButton(Row($"A round for the whole bar ({Tavern.RoundForTheBar} gold): raises your name here (now {World.Reputation(Tavern.Id)}).", 380), "Buy", 124,
            () => Done(TavernServices.RoundForTheBar(World, Tavern, _payer), _payer));
        Row($"{_payer.Name} carries To-Go: {(World.Provisions(_payer.Id).Count == 0 ? "nothing" : string.Join(", ", World.Provisions(_payer.Id).Select(id => Tavern.Menu.FirstOrDefault(item => item.Id == id)?.Name ?? id)))}.", 536);
    }

    private void ShowMerchant()
    {
        Row("FOR SALE", 536).GetChild<Label>(0).AddThemeColorOverride("font_color", Palette.Trim);
        foreach (IGrouping<string, string> group in World.Stock(Tavern).GroupBy(id => id).OrderBy(group => Content.GetCard(group.Key).Name))
        {
            CardDefinition card = Content.GetCard(group.Key);
            HBoxContainer row = Row($"{card.Name}{(group.Count() > 1 ? $" x{group.Count()}" : "")}, {TavernServices.BuyPrice(card)} gold: {card.Text}", 456);
            RowButton(row, "Buy", 76, () => Done(TavernServices.Buy(World, Tavern, Content, _payer, card.Id), _payer));
        }

        Row($"{_payer.Name.ToUpperInvariant()}'S THINGS", 536).GetChild<Label>(0).AddThemeColorOverride("font_color", Palette.Trim);
        IEnumerable<(string Id, string Where)> owned = _payer.Backpack.Select(id => (id, "pack"))
            .Concat(_payer.Belt.Select(id => (id, "belt")))
            .Concat(_payer.Equipped.Select(id => (id, "worn")));
        foreach ((string id, string where) in owned.Distinct())
        {
            CardDefinition card = Content.GetCard(id);
            RowButton(Row($"{card.Name} ({where}), {TavernServices.SellPrice(card)} gold", 456), "Sell", 76,
                () => Done(TavernServices.Sell(World, Tavern, Content, _payer, id), _payer));
        }
    }

    private void ShowRecruits()
    {
        Row($"Your name here: {World.Reputation(Tavern.Id)}. At 3 and at 6, better folk will follow you.", 536);
        foreach (RecruitOffer offer in TavernServices.Recruits(World, Tavern))
        {
            bool hired = Roster.Any(character => string.Equals(character.Name, offer.Name, StringComparison.OrdinalIgnoreCase));
            string stats = string.Join(", ", PointBuy.AllStats.Where(stat => offer.Allocation[stat] > 0).Select(stat => $"+{offer.Allocation[stat]} {StatName(stat)}"));
            string gear = offer.Gear.Count > 0 ? $"; brings {string.Join(", ", offer.Gear.Select(id => Content.GetCard(id).Name))}" : "";
            HBoxContainer row = Row($"{offer.Name}, level {offer.Level}: {stats}{gear}", 436);
            Button hire = RowButton(row, hired ? "Hired" : $"Hire, {offer.Price} gold", 96, () => Hire(offer));
            hire.Disabled = hired;
        }
    }

    private void Hire(RecruitOffer offer)
    {
        (ServiceResult result, Character? recruit) = TavernServices.Hire(_payer, offer, Content, DateTimeOffset.UtcNow);
        if (recruit is not null)
        {
            Roster.Add(recruit);
            RecruitHired?.Invoke(recruit);
            if (Party.Count < Woad.Core.Rules.GameRules.MaxHeroes)
            {
                Seat(recruit);
            }
            else
            {
                result = result with { Message = result.Message + " The party's full, so they wait upstairs." };
            }
        }

        Done(result, _payer);
    }

    private void ShowRooms()
    {
        Row("Renting a room to heal comes with Raid saves. For now: who travels with you?", 536);
        Row("IN THE PARTY", 536).GetChild<Label>(0).AddThemeColorOverride("font_color", Palette.Trim);
        foreach (Character member in Party.ToList())
        {
            string owner = member.IsCompanion && OwnerOf(member) is { } player ? $", {player.Name}'s companion" : "";
            Button leave = RowButton(Row($"{member.Name}, level {member.Level}{owner}", 436), "Go upstairs", 96, () => Unseat(member));
            leave.Disabled = member == _leader;
            leave.TooltipText = member == _leader ? "The leader can't leave. Pass the lead first." : "";
        }

        Row("WAITING UPSTAIRS", 536).GetChild<Label>(0).AddThemeColorOverride("font_color", Palette.Trim);
        foreach (Character waiting in Roster.Where(character => !Party.Contains(character)))
        {
            Button join = RowButton(Row($"{waiting.Name}, level {waiting.Level}{(waiting.IsCompanion ? $", companion: joins {SeatOwner().Name}" : "")}", 436), "Join", 96, () => Seat(waiting));
            join.Disabled = Party.Count >= Woad.Core.Rules.GameRules.MaxHeroes;
        }
    }

    private void ShowBones()
    {
        HBoxContainer row = Row($"Bones: two dice each, high total takes the pot; a tie hands the wagers back. {_payer.Name} bets:", 300);
        foreach (int wager in new[] { 1, 5, 10, TavernServices.MaxWager })
        {
            RowButton(row, $"{wager} gold", 54, () =>
            {
                BonesResult game = TavernServices.Bones(_random, _payer, wager);
                Sfx.Play("dice");
                Done(new ServiceResult(game.Yours.Count > 0, game.Message), _payer);
            });
        }
    }

    /// <summary>The player a companion belongs to: the nearest player seated before it.</summary>
    private Character? OwnerOf(Character companion) => Party.Take(Party.IndexOf(companion)).LastOrDefault(member => !member.IsCompanion);

    /// <summary>The player a new companion joins: the one paying, or the one the paying companion belongs to.</summary>
    private Character SeatOwner() => !_payer.IsCompanion ? _payer : OwnerOf(_payer) ?? _leader;

    /// <summary>Seats a character: a player at the end, a companion after the paying player's other companions.</summary>
    private void Seat(Character character)
    {
        if (!character.IsCompanion)
        {
            Party.Add(character);
        }
        else
        {
            int end = Party.IndexOf(SeatOwner()) + 1;
            while (end < Party.Count && Party[end].IsCompanion)
            {
                end++;
            }

            Party.Insert(end, character);
        }

        _dialogText.Text = $"{character.Name} joins the party.";
        PartyChanged?.Invoke();
        ShowTalk();
    }

    /// <summary>Sends someone upstairs to wait; a player takes their companions along.</summary>
    private void Unseat(Character character)
    {
        int index = Party.IndexOf(character);
        int count = 1;
        while (!character.IsCompanion && index + count < Party.Count && Party[index + count].IsCompanion)
        {
            count++;
        }

        Party.RemoveRange(index, count);
        _dialogText.Text = count > 1 ? $"{character.Name} and their companions go up to wait." : $"{character.Name} goes up to wait.";
        PartyChanged?.Invoke();
        ShowTalk();
    }

    private static string StatName(StatKind stat) => stat switch
    {
        StatKind.AttackDice => "Rumble",
        StatKind.DefenseDice => "Defense",
        StatKind.OdHandDice => "Od Hand",
        _ => stat.ToString(),
    };

    private HBoxContainer Row(string text, float width)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        Label label = UiTheme.Label(Vector2.Zero, new Vector2(width, RowHeight), text);
        label.CustomMinimumSize = new Vector2(width, RowHeight);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.TooltipText = text;
        label.MouseFilter = MouseFilterEnum.Pass;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        _list.AddChild(row);
        return row;
    }

    private static Button RowButton(HBoxContainer row, string text, float width, Action action)
    {
        Button button = UiTheme.Button(Vector2.Zero, new Vector2(width, RowHeight), text);
        button.CustomMinimumSize = new Vector2(width, RowHeight);
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }
}
