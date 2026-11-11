using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Saves;
using Woad.Core.Tavern;
using Woad.Core.Woarld;
using Woad.Scenes.Game;
using Woad.Scenes.Ui;
using Woad.Scenes.Woarld;

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
    private RichTextLabel _status = null!;
    private Label _message = null!;
    private double _stepCooldown;

    /// <summary>Who to talk to once the walk there ends.</summary>
    private TavernNpc? _walkingTo;

    private Control _dialog = null!;
    private Label _dialogTitle = null!;
    private Label _dialogText = null!;
    private VBoxContainer _list = null!;
    private TavernNpc? _talkingTo;

    /// <summary>A game of Bones's dice, shown as in a fight: yours, VS, the gambler's.</summary>
    private DiceBar _bones = null!;

    /// <summary>The dice of a game of Bones are still tumbling.</summary>
    private bool _rolling;
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

    /// <summary>The raid being played: everyone's hearts, and who rests in a room.</summary>
    public RaidSave Raid { get; init; } = null!;

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

    /// <summary>A character with points to spend, from the Party menu: open their points.</summary>
    public event Action<Character>? LevelUpRequested;

    internal Button PartyButton { get; private set; } = null!;

    internal PartyDialog PartyMenu { get; private set; } = null!;

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

    /// <summary>The party is on its way: steps still to take, or the last one still being walked.</summary>
    internal bool IsWalking => _path.Count > 0 || _view.Motion.IsMoving;

    /// <summary>A game of Bones's dice are still tumbling: its outcome isn't in yet.</summary>
    internal bool IsRolling => _rolling;

    internal DiceBar BonesDice => _bones;

    internal Character Payer => _payer;

    /// <summary>The Party Chest pays (and the leader eats, drinks, bets and hires), instead of someone's own purse.</summary>
    private bool _chestPays;

    /// <summary>Whose coins pay: the payer's, or the Party Chest's.</summary>
    private IPurse Purse => _chestPays ? Raid.Chest : _payer;

    private string PurseName => _chestPays ? "The Party Chest" : _payer.Name;

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
        Music.Play("inn");
        _payer = _leader;
        _visit = new TavernVisit(Tavern, Party.Count);
        AddChild(new ColorRect { Color = Palette.Background, Size = Size, MouseFilter = MouseFilterEnum.Ignore });

        _view = new TavernView { Position = new Vector2(0, BarHeight), Size = new Vector2(Size.X, ButtonsTop - 4 - BarHeight) };
        _view.TileClicked += Clicked;
        AddChild(_view);

        AddChild(new Panel { Size = new Vector2(Size.X, BarHeight), MouseFilter = MouseFilterEnum.Ignore });
        _status = UiTheme.RichLabel(new Vector2(8, 2), new Vector2(Size.X - 16, 15));
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
        TalkButton = AddButton(new Vector2(310, ButtonsTop), 260, "", "");
        PartyButton = AddButton(new Vector2(576, ButtonsTop), 56, "Party", "The Party: everyone's gear, belts and packs, the Party Chest, and levelling up.");
        PartyButton.Pressed += () => PartyMenu.Open();
        TalkButton.Pressed += () =>
        {
            if (_visit.NpcInReach is { } npc)
            {
                Talk(npc);
            }
        };

        BuildDialog();

        int Max(Character character) => character.MaxHearts(Content);
        PartyMenu = new PartyDialog
        {
            Party = Party,
            Content = Content,
            Chest = Raid.Chest,
            Hearts = character => (Raid.HeartsOf(character.Id, Max(character)), Max(character)),
            Wound = character => Raid.SetHearts(character.Id, Math.Max(1, Raid.HeartsOf(character.Id, Max(character)) - 1), Max(character)),
            Random = _random,
        };
        PartyMenu.Changed += heroes =>
        {
            foreach (Character hero in heroes)
            {
                CharacterChanged?.Invoke(hero);
            }

            PartyChanged?.Invoke();
            WorldChanged?.Invoke();
            Refresh();
        };
        PartyMenu.LevelUpRequested += character => LevelUpRequested?.Invoke(character);
        AddChild(PartyMenu);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _stepCooldown -= delta;
        if (PartyMenu.Visible)
        {
            return;
        }

        if (_stepCooldown > 0 || _dialog.Visible)
        {
            return;
        }

        if (_path.Count > 0)
        {
            TakeStep(_path.Dequeue());
            _stepCooldown = PathStepSeconds;
            _view.Motion.StepSeconds = PathStepSeconds;
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
                _view.Motion.StepSeconds = HeldStepSeconds;
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
        _status.Text = $"[color={Palette.Hex(Palette.TrimBright)}]{Tavern.Name.ToUpperInvariant()}[/color]  Day {World.Journey.Day}  Reputation {World.Reputation(Tavern.Id)}   " +
                       string.Join("    ", Party.Select(member => $"{member.Name} {UiTheme.Hearts(HeartsOf(member), member.MaxHearts(Content))} {UiTheme.Coins(member.Coins)}"));
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
        // Dims the room but not the bar above it, so everyone's gold and hearts stay in view while talking.
        _dialog.AddChild(new ColorRect { Color = new Color(Palette.Background, 0.6f), Position = new Vector2(0, BarHeight), Size = Size - new Vector2(0, BarHeight) });
        var panel = new Panel { Position = new Vector2(32, 36), Size = new Vector2(576, 288), MouseFilter = MouseFilterEnum.Ignore };
        _dialog.AddChild(panel);

        _dialogTitle = UiTheme.Heading(UiTheme.Label(new Vector2(14, 8), new Vector2(340, 20), "", 16, Palette.TrimBright), 2);
        panel.AddChild(_dialogTitle);
        PayerButton = UiTheme.Button(new Vector2(362, 10), new Vector2(200, 20), "");
        PayerButton.TooltipText = "Who's paying (and eating, betting or hiring): everyone in turn, then the Party Chest. Click for the next.";
        PayerButton.Pressed += () =>
        {
            // Everyone in turn, then the Party Chest (with the leader acting for it), then round again.
            if (_chestPays)
            {
                _chestPays = false;
                _payer = Party[0];
            }
            else if (Party.IndexOf(_payer) == Party.Count - 1)
            {
                _chestPays = true;
                _payer = _leader;
            }
            else
            {
                _payer = Party[Party.IndexOf(_payer) + 1];
            }
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

        _bones = new DiceBar { Position = new Vector2(46, 150), Size = new Vector2(484, 32), Visible = false };
        panel.AddChild(_bones);

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
        _bones.Clear();
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
        PayerButton.Text = _chestPays ? $"Paying: Party Chest ({Money.Short(Raid.Chest.Coins)})" : $"Paying: {_payer.Name} ({Money.Short(_payer.Coins)})";
        _bones.Visible = npc.Role == NpcRole.Gambler;
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
            case NpcRole.Cowherder:
                ShowCraven();
                break;
            case NpcRole.Priest:
                ShowPriest();
                break;
        }

        Refresh();
    }

    private void ShowBar()
    {
        foreach (MenuItem item in Tavern.Menu)
        {
            HBoxContainer row = Row($"{item.Name}, {Money.Short(item.Price)}: {item.Text}", 380);
            RowButton(row, item.Kind == "drink" ? "Drink" : "Eat", 60, () => Done(TavernServices.Eat(World, _payer, item, Purse), _payer));
            RowButton(row, "To-Go", 60, () => Done(TavernServices.BuyToGo(World, _payer, item, Purse), _payer));
        }

        int others = Party.Count - 1;
        Button party = RowButton(Row($"A round for the party ({Money.Short(TavernServices.RoundForThePartyPrice)}): everyone else drinks, and grows closer to {_payer.Name}.", 380), "Buy", 124,
            () => Done(TavernServices.RoundForTheParty(World, Tavern, _payer, Party, Purse), _payer));
        party.Disabled = others == 0;
        RowButton(Row($"A round for the whole bar ({Money.Short(Tavern.RoundForTheBar)}): raises your name here (now {World.Reputation(Tavern.Id)}).", 380), "Buy", 124,
            () => Done(TavernServices.RoundForTheBar(World, Tavern, Purse), _payer));
        Row($"{_payer.Name} carries To-Go: {(World.Provisions(_payer.Id).Count == 0 ? "nothing" : string.Join(", ", World.Provisions(_payer.Id).Select(id => Tavern.Menu.FirstOrDefault(item => item.Id == id)?.Name ?? id)))}.", 536);
    }

    /// <summary>The merchant's wares, on the Party menu's shop page: anyone in the party (or the chest) buys and sells.</summary>
    private void ShowMerchant()
    {
        Row($"Merchant Guild level {World.GuildLevel}. Anyone in the party can buy or sell, or the Party Chest can.", 436);
        Button trade = RowButton(Row("Oswin's wares, beside everyone's things.", 436), "Trade", 96, () => PartyMenu.OpenShop(InnShop()));
        trade.Name = "Trade";
        PartyMenu.OpenShop(InnShop());
    }

    /// <summary>The inn's merchant as a shop: prices by the shopper's Guild standing; buys anything but quest things.</summary>
    internal ShopFront InnShop() => new()
    {
        Name = $"{_talkingTo?.Name ?? "The merchant"}'s wares",
        Note = $"Merchant Guild level {World.GuildLevel}. Better standing, better prices.",
        Wares = () => World.Stock(Tavern),
        Price = (id, shopper) => TavernServices.BuyPrice(Content.GetCard(id), shopper.Standing),
        Buy = (id, shopper) => TavernServices.Buy(World, Tavern, Content, shopper, id),
        Pays = key => Content.GetCard(key).Quest ? null : TavernServices.SellPrice(Content.GetCard(key)),
        Sell = (key, seller, take) => TavernServices.Sell(World, Tavern, Content, seller, key, take),
    };

    private void ShowCraven()
    {
        Row($"{_payer.Name} has fled {_payer.CowardStanding} times (Cowards {_payer.CowardStanding}). At {TavernServices.CowardsForTheCloak}, Craven gives a sign of respect.", 536);
        Button ask = RowButton(Row("The Coward's Cloak: a hood to hide in. Once, it slows the monsters beside you and turns your Rumble and Od Hand dice to Wend dice.", 436),
            _payer.HasCowardsCloak ? "Given" : "Ask for it", 96, () => Done(TavernServices.CowardsCloak(_payer, Content), _payer));
        ask.Disabled = _payer.HasCowardsCloak || _payer.CowardStanding < TavernServices.CowardsForTheCloak;
    }

    private void ShowPriest()
    {
        QuestStage stage = World.Quest(Quests.Relic);
        bool carrying = Party.Any(member => member.Equipped.Concat(member.Belt).Concat(member.Backpack).Contains("saints_relic"));
        (string text, string action) = (stage, carrying) switch
        {
            (QuestStage.Done, _) => ("The Relic of Saint Wyn is home. \"The Graves are worse than ever. Pray.\"", ""),
            (_, true) => ("You carry the Relic of Saint Wyn. Father Ambrose's eyes fill with tears.", "Give it"),
            (QuestStage.Taken, _) => ("THE RELIC: bring the Relic of Saint Wyn from the reliquary in the ruined temple, at the heart of the Unhallowed Graves. The Holy Key opens it.", "Lost the key?"),
            _ => ("\"Saint Wyn's relic lies in the temple at the heart of the Unhallowed Graves, and the dead will not let it go. Will you bring it home?\"", "Take the quest"),
        };
        HBoxContainer row = Row(text, action.Length > 0 ? 436 : 536);
        if (action.Length > 0)
        {
            RowButton(row, action, 96, () =>
            {
                // The relic may come from anyone in the party: they're all saved.
                ServiceResult result = TavernServices.Relic(World, Tavern, Content, _payer, Party);
                foreach (Character member in result.Done ? Party.Where(member => member != _payer) : [])
                {
                    CharacterChanged?.Invoke(member);
                }

                Done(result, _payer);
            });
        }

        if (stage != QuestStage.Done)
        {
            return;
        }

        (string ritual, string ask) = World.Quest(Quests.Ritual) switch
        {
            QuestStage.NotTaken => ("THE RITUAL: \"The cult means to open a door to the realm of death, in the temple at the heart of the Graves. Will you stop them?\"", "Take the quest"),
            QuestStage.Taken => ($"THE RITUAL: kill the cabal of five round the circle in the ruined temple, and the cult leader at the pulpit, within {Quests.RitualRounds} rounds of the gate.", ""),
            QuestStage.Achieved => ("You broke the ritual. Father Ambrose wants to hear all of it.", "Tell him"),
            QuestStage.Failed => ("The portal stands open in the Graves. Father Ambrose can only pray.", ""),
            _ => ("The ritual was broken. The Graves are quieter for it. A little.", ""),
        };
        HBoxContainer ritualRow = Row(ritual, ask.Length > 0 ? 436 : 536);
        if (ask.Length > 0)
        {
            RowButton(ritualRow, ask, 96, () => Done(TavernServices.Ritual(World, Tavern, Content, _payer), _payer));
        }
    }

    private void ShowRecruits()
    {
        Row($"Your name here: {World.Reputation(Tavern.Id)}. At 3 and at 6, better folk will follow you.", 536);
        // Clod Hopper, the frog hunter, is always at the recruiter's elbow, keen to come along.
        foreach (RecruitOffer offer in TavernServices.Recruits(World, Tavern).Append(TavernServices.Clod))
        {
            bool hired = Roster.Any(character => string.Equals(character.Name, offer.Name, StringComparison.OrdinalIgnoreCase));
            string stats = string.Join(", ", PointBuy.AllStats.Where(stat => offer.Allocation[stat] > 0).Select(stat => $"+{offer.Allocation[stat]} {StatName(stat)}")
                .Concat(Skills.In(offer.Allocation.Skills).Select(skill => skill.Name())));
            string gear = offer.Gear.Count > 0 ? $"; brings {string.Join(", ", offer.Gear.Select(id => Content.GetCard(id).Name))}" : "";
            HBoxContainer row = Row($"{offer.Name}, level {offer.Level}: {stats}{gear}", 436);
            Button hire = RowButton(row, hired ? "Hired" : $"Hire, {Money.Short(offer.Price)}", 96, () => Hire(offer));
            hire.Disabled = hired;
        }
    }

    private void Hire(RecruitOffer offer)
    {
        (ServiceResult result, Character? recruit) = TavernServices.Hire(_payer, offer, Content, DateTimeOffset.UtcNow, Purse);
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
        RowButton(Row($"Rooms for the whole party tonight ({Money.Short(TavernServices.NightPrice)}): a day passes, and everyone heals a heart and makes a Rest roll.", 436),
            "Sleep the night", 96, SleepTheNight);
        Row("IN THE PARTY", 536).GetChild<Label>(0).AddThemeColorOverride("font_color", Palette.Trim);
        foreach (Character member in Party.ToList())
        {
            string owner = member.IsCompanion && OwnerOf(member) is { } player ? $", {player.Name}'s companion" : "";
            int leaving = Leaving(member).Count;
            HBoxContainer row = Row($"{member.Name}, level {member.Level}, {HeartsOf(member)}/{member.MaxHearts(Content)} hearts{owner}", 376);
            string who = leaving > 1 ? $"{member.Name} and their companions" : member.Name;
            Button leave = RowButton(row, $"Room, {Money.Short(TavernServices.RoomPrice * leaving)}", 96, () => GoUpstairs(member, sty: false));
            Button sty = RowButton(row, "Pig sty", 56, () => GoUpstairs(member, sty: true));
            leave.Disabled = sty.Disabled = member == _leader;
            leave.TooltipText = member == _leader
                ? "The leader can't leave. Pass the lead first."
                : $"A room upstairs for {who}: each night a heart healed and a Rest roll, until they rejoin.";
            sty.TooltipText = member == _leader
                ? "The leader can't leave. Pass the lead first."
                : $"The pig sty out back for {who}, free: a Rest roll each night, and nothing more.";
        }

        Row("WAITING UPSTAIRS", 536).GetChild<Label>(0).AddThemeColorOverride("font_color", Palette.Trim);
        foreach (Character waiting in Roster.Where(character => !Party.Contains(character)))
        {
            string room = Raid.Rooms.ContainsKey(waiting.Id) ? ", resting in a room" : Raid.Sty.ContainsKey(waiting.Id) ? ", in the pig sty" : "";
            Button join = RowButton(Row($"{waiting.Name}, level {waiting.Level}, {HeartsOf(waiting)}/{waiting.MaxHearts(Content)} hearts{room}{(waiting.IsCompanion ? $", companion: joins {SeatOwner().Name}" : "")}", 436),
                "Join", 96, () => Rejoin(waiting));
            join.Disabled = Party.Count >= Woad.Core.Rules.GameRules.MaxHeroes;
        }
    }

    private int HeartsOf(Character character) => Raid.HeartsOf(character.Id, character.MaxHearts(Content));

    private int MaxHeartsOf(Guid id) => Roster.FirstOrDefault(character => character.Id == id)?.MaxHearts(Content) ?? 1;

    /// <summary>Who goes upstairs with someone: a player takes their companions along.</summary>
    private List<Character> Leaving(Character character)
    {
        int index = Party.IndexOf(character);
        int count = 1;
        while (!character.IsCompanion && index + count < Party.Count && Party[index + count].IsCompanion)
        {
            count++;
        }

        return Party.GetRange(index, count);
    }

    /// <summary>
    /// Rooms for the whole party tonight: paid first, then everyone's Rest roll eats what they've had
    /// (or carry To-Go).
    /// </summary>
    private void SleepTheNight()
    {
        if (Purse.Coins < TavernServices.NightPrice)
        {
            Done(ServiceResult.Refused($"{PurseName} has {Money.Long(Purse.Coins)}; rooms cost {Money.Long(TavernServices.NightPrice)}."), null);
            return;
        }

        IReadOnlyList<RestBoost> boosts = World.TakeRestBoosts([.. Party.Select(member => member.Id)], Tavern);
        Done(TavernServices.SleepTheNight(Raid, World.Journey, _random, Purse, Party, MaxHeartsOf, boosts), _payer);
    }

    /// <summary>Sends someone (and their companions) off to wait: rooms upstairs, paid, or the pig sty out back, free.</summary>
    private void GoUpstairs(Character member, bool sty)
    {
        List<Character> leaving = Leaving(member);
        int price = sty ? 0 : TavernServices.RoomPrice * leaving.Count;
        if (Purse.Coins < price)
        {
            Done(ServiceResult.Refused($"{PurseName} has {Money.Long(Purse.Coins)}; rooms cost {Money.Long(price)}."), null);
            return;
        }

        foreach (Character guest in leaving)
        {
            if (sty)
            {
                TavernServices.SleepInSty(Raid, World.Journey.Day, guest);
            }
            else
            {
                TavernServices.RentRoom(Raid, World.Journey.Day, Purse, guest);
            }
        }

        Unseat(member);
        Done(new ServiceResult(true, sty
            ? $"{string.Join(" and ", leaving.Select(guest => guest.Name))} bed{(leaving.Count == 1 ? "s" : "")} down in the pig sty out back. {DialogText}"
            : $"{PurseName} pays {Money.Long(price)} for rooms. {DialogText}"), _payer);
    }

    /// <summary>Brings someone back down (or in from the sty), healed for the nights they rested.</summary>
    private void Rejoin(Character waiting)
    {
        int before = HeartsOf(waiting);
        Raid.LeaveRoom(waiting.Id, World.Journey.Day, waiting.MaxHearts(Content));
        int healed = HeartsOf(waiting) - before;
        Seat(waiting);
        if (healed > 0)
        {
            _dialogText.Text += $" Rested, {waiting.Name} healed {UiTheme.Count(healed, "heart", "hearts")}.";
        }

        WorldChanged?.Invoke();
    }

    private void ShowBones()
    {
        Row("Bones: two dice each, high total takes the pot; a tie hands the wagers back.", 536);
        HBoxContainer row = Row(_chestPays ? $"{_payer.Name} bets the chest's coins:" : $"{_payer.Name} bets:", 300);
        foreach (int wager in new[] { 1, 5, 10, TavernServices.MaxWager })
        {
            Button bet = RowButton(row, $"{wager} gold", 54, () => PlayBones(wager));
            bet.Disabled = Purse.Coins < Money.Gold(wager) || _rolling;
            bet.TooltipText = bet.Disabled ? $"{PurseName} has {Money.Long(Purse.Coins)}." : $"Bet {wager} gold on a game of Bones.";
        }
    }

    /// <summary>
    /// Plays a game of Bones: the dice tumble and land as in a fight (the payer's, VS, the gambler's),
    /// the winner glows, and only then does the gold change hands on screen.
    /// </summary>
    private async void PlayBones(int wager)
    {
        if (_rolling || _talkingTo is not { Role: NpcRole.Gambler } gambler)
        {
            return;
        }

        Character player = _payer;
        BonesResult game = TavernServices.Bones(_random, player, wager, Purse);
        if (game.Yours.Count == 0)
        {
            Done(new ServiceResult(false, game.Message), null);
            return;
        }

        _rolling = true;
        foreach (Button bet in _list.GetChildren().OfType<HBoxContainer>().SelectMany(row => row.GetChildren().OfType<Button>()))
        {
            bet.Disabled = true;
        }

        _dialogText.Text = $"{player.Name} bets {Money.Long(Money.Gold(wager))}. The dice fly...";
        Sfx.Play("dice");
        await _bones.ShowRoll($"{player.Name} rolls", new DiceRoll(game.Yours), 0, newGroup: true);
        await _bones.ShowRoll($"{gambler.Name} rolls", new DiceRoll(game.Theirs), 0, newGroup: false);
        if (game.Winnings != 0)
        {
            _bones.ShowOutcome(game.Winnings > 0, game.Winnings > 0 ? "YOU WIN" : "YOU LOSE");
        }
        else
        {
            _bones.ShowTie();
        }

        _rolling = false;
        if (IsInsideTree())
        {
            Done(new ServiceResult(true, game.Message), player);
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
        StatKind.MoveDice => "Wend",
        StatKind.Speed => "Wend bonus",
        _ => stat.ToString(),
    };

    private HBoxContainer Row(string text, float width)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        // Long lines (a card's whole text) wrap onto more lines rather than being cut short.
        Label label = UiTheme.Label(Vector2.Zero, new Vector2(width, RowHeight), text);
        label.CustomMinimumSize = new Vector2(width, RowHeight);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(label);
        _list.AddChild(row);
        return row;
    }

    private static Button RowButton(HBoxContainer row, string text, float width, Action action)
    {
        Button button = UiTheme.Button(Vector2.Zero, new Vector2(width, RowHeight), text);
        button.CustomMinimumSize = new Vector2(width, RowHeight);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }
}
