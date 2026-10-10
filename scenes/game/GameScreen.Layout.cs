using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Building the screen and keeping its panels, buttons and highlights in step with the rules.</summary>
public partial class GameScreen
{
    private const float SideWidth = 176;
    private const float StripHeight = 36;
    private const int LogLines = 3;

    private static readonly Vector2 BoardOrigin = new(SideWidth, StripHeight);

    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<int, string> _cardNames = [];
    private readonly List<string> _messages = [];

    private BoardView _board = null!;
    private HeroPanel _heroPanel = null!;
    private MinimapView _minimap = null!;
    private InventoryPanel _inventory = null!;
    private DiceTray _dice = null!;
    private TurnBanner _banner = null!;
    private RichTextLabel _header = null!;
    private Label _hoverInfo = null!;
    private RichTextLabel _log = null!;
    private Label _luckHint = null!;
    private Button _confirm = null!;
    private Button _roll = null!;
    private Button _endTurn = null!;
    private Button _menu = null!;
    private GridPosition? _hovered;

    private void RefreshPanels()
    {
        GameState state = _session.State;
        Hero? hero = ActiveHero;
        TurnState? turn = state.Turn;
        if (_selectedCardId is int id && hero?.FindCard(id) is null)
        {
            _selectedCardId = null;
            _targeting = false;
        }

        _heroPanel.ShowHero(hero, turn, _selectedCardId);
        _inventory.ShowHero(hero, _selectedCardId, AvailableCardActions(hero, turn));
        _header.Text = HeaderText();
        if (_hovered is null)
        {
            _hoverInfo.Text = Hint();
        }

        bool idle = !_busy && turn is not null;
        bool pending = turn?.PendingAttack is not null;
        _roll.Disabled = !(idle && !pending && turn!.MovementRoll is null && !turn.MovementLocked);
        _endTurn.Disabled = !(idle && !pending);
        _confirm.Visible = pending;
        _confirm.Disabled = _busy;

        bool canReroll = idle && hero!.LuckRemaining > 0
            && (pending || (_trayShowsMovement && turn!.MovementRoll is not null && !turn.HasMoved));
        _dice.Rerollable = canReroll;
        _luckHint.Visible = canReroll;

        _menu.Disabled = _busy || state.Phase == RunPhase.Ended;
        _menu.Text = _confirmingLeave ? "Leave?" : "Menu";

        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        if (_busy || ActiveHero is not { } hero || _session.State.Turn is not { } turn || hero.RoomId != _shownRoomId)
        {
            _board.ClearHighlights();
            return;
        }

        if (_targeting && _selectedCardId is int cardId)
        {
            _board.SetHighlights([], [], _session.GetCardTargets(cardId), []);
            return;
        }

        Room room = ShownRoom;
        var interactive = new List<GridPosition>();
        if (!turn.HasActed && turn.PendingAttack is null)
        {
            interactive.AddRange(hero.Position.OrthogonalNeighbors()
                .Where(tile => (room.Layout[tile] == TileKind.Chest && !room.IsChestOpened(tile)) || room.Layout[tile] == TileKind.Pot));
        }

        if (OwnTileIsUsable(hero, room, turn))
        {
            interactive.Add(hero.Position);
        }

        _board.SetHighlights(
            _session.GetReachableTiles().Keys,
            _session.GetAttackableMonsters().Select(monster => monster.Position),
            [],
            interactive);
    }

    /// <summary>Whether clicking the hero's own tile would do something: loot, stairs, exit or a door.</summary>
    private bool OwnTileIsUsable(Hero hero, Room room, TurnState turn)
    {
        if (turn.PendingAttack is not null)
        {
            return false;
        }

        bool canStep = !turn.MovementLocked && turn.MovementRoll is not null && turn.MovementPoints > 0;
        return room.Layout[hero.Position] switch
        {
            _ when room.LootAt(hero.Position).Count > 0 => !turn.HasActed,
            TileKind.StairsDown => canStep,
            TileKind.Exit => !room.Monsters.Any(monster => monster.Definition.Boss),
            _ => DoorAt(room, hero.Position) is not null && _session.GetUsableDoors().Count > 0,
        };
    }

    private CardActions AvailableCardActions(Hero? hero, TurnState? turn)
    {
        if (_busy || hero is null || turn is null || turn.PendingAttack is not null
            || _selectedCardId is not int cardId || hero.FindCard(cardId) is not var (card, location))
        {
            return CardActions.None;
        }

        CardDefinition definition = card.Definition;
        bool canAct = !turn.HasActed;
        return new CardActions(
            Use: definition.Effect is not null && _session.GetCardTargets(cardId).Count > 0,
            Equip: location == CardLocation.Backpack && definition.Slot is not null && canAct,
            Belt: location == CardLocation.Backpack && definition.Usage == CardUsage.SingleUse
                  && hero.Belt.Count < GameRules.BeltCapacity && canAct,
            Stow: location != CardLocation.Backpack && hero.Backpack.Count < GameRules.BackpackCapacity && canAct,
            Drop: true);
    }

    private string HeaderText()
    {
        GameState state = _session.State;
        string dim = Palette.Hex(Palette.TextDim);
        IEnumerable<string> heroes = state.Heroes.Select(hero =>
        {
            string name = Escape(hero.Name);
            string color = Palette.Hex(Palette.Seat(hero.Seat));
            return hero.Status switch
            {
                HeroStatus.Downed => $"[color={dim}][s]{name}[/s][/color]",
                HeroStatus.Escaped => $"[color={dim}]{name} (out)[/color]",
                _ when hero == ActiveHero => $"[color={color}][u]{name}[/u][/color]",
                _ => $"[color={color}]{name}[/color]",
            };
        });

        int floor = _shownRoomId.Length > 0 ? ShownRoom.Floor : 1;
        return $"[color={Palette.Hex(Palette.Trim)}]ROUND {state.Round}   FLOOR {floor}[/color]   {string.Join("  ", heroes)}";
    }

    private string NameOf(int entityId) => _names.GetValueOrDefault(entityId, "Someone");

    private string CardNameOf(int cardId)
    {
        RememberCardNames();
        return _cardNames.GetValueOrDefault(cardId, "card");
    }

    /// <summary>Keeps names of every card seen, so used-up or dropped cards can still be named in the log.</summary>
    private void RememberCardNames()
    {
        IEnumerable<CardInstance> cards = _session.State.Heroes.SelectMany(hero => hero.AllCards)
            .Concat(_session.State.Rooms.Values.SelectMany(room => room.LootPositions.SelectMany(room.LootAt)));
        foreach (CardInstance card in cards)
        {
            _cardNames[card.Id] = card.Definition.Name;
        }
    }

    private bool IsHero(int entityId) => _session.State.Heroes.Any(hero => hero.Id == entityId);

    private void Log(string message, Color? color = null)
    {
        string line = Escape(message);
        _messages.Add(color is { } tint ? $"[color={Palette.Hex(tint)}]{line}[/color]" : line);
        if (_messages.Count > LogLines)
        {
            _messages.RemoveAt(0);
        }

        string dim = Palette.Hex(Palette.TextDim);
        _log.Text = string.Join("\n", _messages.Select((text, index) =>
            index < _messages.Count - 1 ? $"[color={dim}]{text}[/color]" : text));
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    private void BuildLayout()
    {
        AddChild(new ColorRect { Color = Palette.Background, Size = new Vector2(640, 360), MouseFilter = MouseFilterEnum.Ignore });

        _heroPanel = new HeroPanel();
        _heroPanel.CardSelected += SelectCard;
        AddChild(_heroPanel);
        _minimap = new MinimapView { Position = new Vector2(8, 252), Size = new Vector2(160, 100) };
        AddChild(_minimap);

        AddChild(new Panel { Position = new Vector2(SideWidth, 0), Size = new Vector2(BoardView.Pixels, StripHeight) });
        _header = RichLabel(new Vector2(SideWidth + 6, 4), new Vector2(BoardView.Pixels - 56, 14));
        _menu = UiTheme.Button(new Vector2(SideWidth + BoardView.Pixels - 48, 4), new Vector2(42, 15), "Menu");
        _menu.Pressed += () =>
        {
            if (_confirmingLeave)
            {
                ExitRequested?.Invoke();
                return;
            }

            _confirmingLeave = true;
            Log("Click Leave? again to abandon this run. Nothing will be saved.", Palette.TrimBright);
            RefreshPanels();
        };
        AddChild(_menu);
        _hoverInfo = UiTheme.Label(new Vector2(SideWidth + 6, 19), new Vector2(BoardView.Pixels - 12, 14), color: Palette.TextDim);
        _hoverInfo.AutowrapMode = TextServer.AutowrapMode.Off;
        _hoverInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(_hoverInfo);

        _board = new BoardView { Position = BoardOrigin };
        _board.TileClicked += OnTileClicked;
        _board.TileHovered += OnTileHovered;
        _board.CancelRequested += CancelSelection;
        AddChild(_board);

        AddChild(new Panel { Position = new Vector2(SideWidth, StripHeight + BoardView.Pixels), Size = new Vector2(BoardView.Pixels, StripHeight) });
        _log = RichLabel(new Vector2(SideWidth + 6, StripHeight + BoardView.Pixels + 2), new Vector2(BoardView.Pixels - 12, 33));
        _log.AddThemeConstantOverride("line_separation", -2);

        var right = new Panel { Position = new Vector2(SideWidth + BoardView.Pixels, 0), Size = new Vector2(SideWidth, 360) };
        AddChild(right);

        _inventory = new InventoryPanel();
        _inventory.CardSelected += SelectCard;
        _inventory.ActionRequested += OnCardAction;
        right.AddChild(_inventory);

        right.AddChild(UiTheme.Label(new Vector2(8, 170), new Vector2(160, 12), "DICE", color: Palette.Trim));
        _dice = new DiceTray { Position = new Vector2(8, 182), Size = new Vector2(160, 80) };
        _dice.DieClicked += index => ExecuteForActiveHero(heroId => new RerollDie(heroId, index));
        right.AddChild(_dice);

        _luckHint = UiTheme.Label(new Vector2(8, 263), new Vector2(160, 14), "Luck: click a die to reroll it.", color: Palette.Heal);
        right.AddChild(_luckHint);

        _confirm = UiTheme.Button(new Vector2(8, 279), new Vector2(160, 18), "Confirm attack");
        _confirm.Pressed += () => ExecuteForActiveHero(heroId => new ConfirmAttack(heroId));
        right.AddChild(_confirm);

        _roll = UiTheme.Button(new Vector2(8, 303), new Vector2(160, 20), "Roll to move");
        _roll.Pressed += () => ExecuteForActiveHero(heroId => new RollMovement(heroId));
        right.AddChild(_roll);

        _endTurn = UiTheme.Button(new Vector2(8, 329), new Vector2(160, 22), "End turn");
        _endTurn.Pressed += () =>
        {
            CancelSelection();
            ExecuteForActiveHero(heroId => new EndTurn(heroId));
        };
        right.AddChild(_endTurn);

        _banner = new TurnBanner { Position = BoardOrigin, Size = new Vector2(BoardView.Pixels, BoardView.Pixels) };
        AddChild(_banner);
    }

    private RichTextLabel RichLabel(Vector2 position, Vector2 size)
    {
        var label = new RichTextLabel
        {
            Position = position,
            Size = size,
            BbcodeEnabled = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(label);
        return label;
    }
}
