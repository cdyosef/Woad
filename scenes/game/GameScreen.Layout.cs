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
    private Button _addPower = null!;
    private Button _bankPower = null!;
    private Button _roll = null!;
    private Button _search = null!;
    private Button _steal = null!;
    private Button _shove = null!;
    private Button _endTurn = null!;
    private Button _menu = null!;
    private GridPosition? _hovered;

    // Dice the player has deselected in the pool; everything else is used by the next attack or move.
    private int _attackDiceHeld;
    private int _moveDiceHeld;
    private bool _weaponHeld;
    private bool _speedHeld;
    private int _powerDiceSelected;
    private int _odHandDiceHeld;
    private bool _toolsHeld;

    /// <summary>Steal was pressed: the next click on an adjacent monster or hero tries to rob them.</summary>
    private bool _stealing;

    /// <summary>Shove was pressed: the next click on an adjacent monster tries to push it.</summary>
    private bool _shoving;

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

        bool idle = !Locked && turn is not null;
        bool choosingPower = turn?.UnsettledPowerDice > 0;
        bool pending = turn?.PendingRoll is not null || choosingPower;
        UpdatePool(idle && !pending);
        _roll.Disabled = !(idle && !pending && _session.MoveDiceLeft > 0);
        _search.Disabled = !(idle && !pending) || _session.CheckSearch() is not null;
        _search.TooltipText = _session.State.Turn is null ? "" : _session.CheckSearch() ?? "Search the room with your selected Od Hand dice: finds secret doors, traps and stashes your total reaches.";
        _steal.Disabled = !(idle && !pending) || _session.OdHandDiceLeft < 1 || turn!.HasStolen;
        _stealing &= !_steal.Disabled;
        _steal.Text = _stealing ? "Cancel" : "Steal";
        _shove.Disabled = !(idle && !pending) || _session.AttackDiceLeft < 1;
        _shoving &= !_shove.Disabled;
        _shove.Text = _shoving ? "Cancel" : "Shove";
        _endTurn.Disabled = !(idle && !pending);
        _confirm.Visible = turn?.PendingRoll is not null && !choosingPower;
        _confirm.Disabled = Locked;
        _addPower.Visible = _bankPower.Visible = choosingPower;
        _addPower.Disabled = _bankPower.Disabled = Locked;
        _bankPower.Text = hero is null ? "Bank it" : $"Bank it ({hero.Power.Banked}/{GameRules.MaxBankedPowerDice})";

        bool canReroll = idle && hero!.LuckRemaining > 0
            && (pending || (_trayShowsMovement && turn!.MovementRoll is not null && !turn.HasMoved));
        _dice.Rerollable = canReroll;
        _luckHint.Visible = canReroll;

        _menu.Disabled = _busy || state.Phase == RunPhase.Ended;
        _menu.Text = _confirmingLeave ? "Leave?" : "Menu";

        RefreshOrders();
        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        if (Locked || ActiveHero is not { } hero || _session.State.Turn is not { } turn || hero.RoomId != _shownRoomId)
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
        if (_shoving)
        {
            _board.SetHighlights([], [], _session.GetShoveableMonsters().Select(monster => monster.Position), []);
            return;
        }

        if (_stealing)
        {
            IEnumerable<GridPosition> victims = room.Monsters.Where(monster => _session.CheckSteal(monster.Id) is null).Select(monster => monster.Position)
                .Concat(_session.State.ActiveHeroesIn(room).Where(other => _session.CheckSteal(other.Id) is null).Select(other => other.Position));
            _board.SetHighlights([], [], victims, []);
            return;
        }

        var interactive = new List<GridPosition>();
        if (turn.PendingRoll is null && turn.UnsettledPowerDice == 0)
        {
            interactive.AddRange(hero.Position.OrthogonalNeighbors().Where(tile =>
                (room.Layout[tile] == TileKind.Chest && (_session.CheckOpenChest(tile) is null || _session.CheckPickLock(tile) is null || _session.CheckDisarm(tile) is null))
                || (room.Layout[tile] == TileKind.Pot && !turn.HasActed)
                || _session.CheckDisarm(tile) is null));
        }

        if (OwnTileIsUsable(hero, room, turn))
        {
            interactive.Add(hero.Position);
        }

        // Pits and found traps can be walked into, at a cost: they show red over the blue.
        IReadOnlyCollection<GridPosition> reachable = _session.GetReachableTiles().Keys.ToList();
        _board.SetHighlights(
            reachable,
            _session.GetAttackableMonsters().Select(monster => monster.Position),
            [],
            interactive,
            hazards: reachable.Where(tile => room.Layout[tile] == TileKind.Pit || room.TrapAt(tile) is { Revealed: true }),
            jumps: _session.GetJumpLandings().Where(tile => !reachable.Contains(tile)));
    }

    /// <summary>Whether clicking the hero's own tile would do something: loot, stairs, exit or a door.</summary>
    private bool OwnTileIsUsable(Hero hero, Room room, TurnState turn)
    {
        if (turn.PendingRoll is not null)
        {
            return false;
        }

        bool canStep = turn.MovementPoints > 0;
        if (DoorAt(room, hero.Position) is { } side && room.PassageOn(side) is { IsLocked: true } locked)
        {
            return locked.NeedsBossKey ? hero.HasBossKey : hero.Keys > 0 || _session.CheckPickLock(hero.Position) is null;
        }

        return room.Layout[hero.Position] switch
        {
            _ when room.HasLootAt(hero.Position) => _session.CheckPickUp() is null,
            TileKind.StairsDown => canStep,
            TileKind.Exit => !room.Monsters.Any(monster => monster.Definition.Boss),
            _ => _session.CheckDisarm(hero.Position) is null || (DoorAt(room, hero.Position) is not null && _session.GetUsableDoors().Count > 0),
        };
    }

    /// <summary>
    /// Shows the dice left this turn. Every die and bonus starts selected, so a click on a monster
    /// (or Roll) uses them all; the player can deselect some to split them across several attacks or moves.
    /// </summary>
    private void UpdatePool(bool enabled)
    {
        DicePoolView pool = _heroPanel.Pool;
        pool.Enabled = enabled;
        TurnState? turn = _session.State.Turn;
        int weapon = _session.WeaponBonusLeft;
        int speed = _session.SpeedBonusLeft;
        string steps = turn?.MovementRoll is null ? "" : UiTheme.Count(turn.MovementPoints, "step", "steps") + " left";
        int tools = _session.ToolsBonusLeft;
        pool.ShowPool(DicePool.Rumble, _session.AttackDiceLeft, SelectedAttackDice, weapon, weapon > 0 && !_weaponHeld);
        pool.ShowPool(DicePool.Wend, _session.MoveDiceLeft, SelectedMoveDice, speed, speed < 0 || (speed > 0 && !_speedHeld), steps);
        pool.ShowPool(DicePool.OdHand, _session.OdHandDiceLeft, SelectedOdHandDice, tools, tools > 0 && !_toolsHeld);
        pool.ShowPool(DicePool.Power, ActiveHero?.Power.Banked ?? 0, SelectedPowerDice, 0, false);
    }

    /// <summary>Banked Power Dice start unselected: they're only spent when the player picks them.</summary>
    private int SelectedPowerDice => Math.Clamp(_powerDiceSelected, 0, ActiveHero?.Power.Banked ?? 0);

    private int SelectedAttackDice => Math.Clamp(_session.AttackDiceLeft - _attackDiceHeld, 0, _session.AttackDiceLeft);

    private int SelectedMoveDice => Math.Clamp(_session.MoveDiceLeft - _moveDiceHeld, 0, _session.MoveDiceLeft);

    private int SelectedOdHandDice => Math.Clamp(_session.OdHandDiceLeft - _odHandDiceHeld, 0, _session.OdHandDiceLeft);

    /// <summary>Clicking a selected die deselects one; clicking an unselected one selects it again. Bonus tokens toggle.</summary>
    private void OnPoolTokenClicked(DicePool pool, int index)
    {
        if (Locked)
        {
            return;
        }

        switch (pool)
        {
            case DicePool.Rumble when index < 0:
                _weaponHeld = !_weaponHeld;
                break;
            case DicePool.Rumble:
                _attackDiceHeld += index < SelectedAttackDice ? 1 : -1;
                break;
            case DicePool.Wend when index < 0:
                _speedHeld = _session.SpeedBonusLeft > 0 && !_speedHeld;
                break;
            case DicePool.Wend:
                _moveDiceHeld += index < SelectedMoveDice ? 1 : -1;
                break;
            case DicePool.OdHand when index < 0:
                _toolsHeld = !_toolsHeld;
                break;
            case DicePool.OdHand:
                _odHandDiceHeld += index < SelectedOdHandDice ? 1 : -1;
                break;
            case DicePool.Power:
                _powerDiceSelected += index < SelectedPowerDice ? -1 : 1;
                break;
        }

        RefreshPanels();
    }

    /// <summary>Selects everything again, after each command and at the start of a turn.</summary>
    private void ResetPool()
    {
        _attackDiceHeld = _moveDiceHeld = _odHandDiceHeld = _powerDiceSelected = 0;
        _weaponHeld = _speedHeld = _toolsHeld = _stealing = _shoving = false;
    }

    /// <summary>The card buttons' states, with the rules engine's reason for each one that's unavailable.</summary>
    private CardActions AvailableCardActions(Hero? hero, TurnState? turn)
    {
        if (hero is null || turn is null || _selectedCardId is not int cardId || hero.FindCard(cardId) is not var (card, location))
        {
            return CardActions.None;
        }

        if (Locked)
        {
            return CardActions.Unavailable("Wait for the action to finish.");
        }

        string? Drop() => turn.PendingRoll is not null || turn.UnsettledPowerDice > 0 ? "Finish the roll first." : null;
        var blocked = new Dictionary<CardAction, string>();
        foreach ((CardAction action, string? reason) in new[]
        {
            (CardAction.Use, _session.CheckUse(cardId)),
            (CardAction.Equip, _session.CheckEquip(cardId)),
            (CardAction.Belt, _session.CheckBelt(cardId)),
            (CardAction.Stow, _session.CheckStow(cardId)),
            (CardAction.Drop, Drop()),
        })
        {
            if (reason is not null)
            {
                blocked[action] = reason;
            }
        }

        string gearCost = _session.GearIsFree ? "Free while no monsters are in the room." : "In a fight, this takes your action.";
        var hints = new Dictionary<CardAction, string>
        {
            [CardAction.Use] = location == CardLocation.Belt ? "Free: one belt item a turn." : "Takes your action.",
            [CardAction.Equip] = gearCost,
            [CardAction.Belt] = gearCost,
            [CardAction.Stow] = gearCost,
            [CardAction.Drop] = "Leave it on the floor here. Free.",
        };
        return new CardActions(blocked, hints);
    }

    private string HeaderText()
    {
        GameState state = _session.State;
        string dim = Palette.Hex(Palette.TextDim);
        // A big party doesn't fit by name: everyone but the active hero is shown by initial.
        bool compact = state.Heroes.Count > GameRules.FullParty;
        IEnumerable<string> heroes = state.Heroes.Select(hero =>
        {
            string name = Escape(compact && hero != ActiveHero ? hero.Name[..1].ToUpperInvariant() : hero.Name);
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
        return $"[color={Palette.Hex(Palette.Trim)}]ROUND {state.Round}   FLOOR {floor}[/color]   {string.Join(compact ? " " : "  ", heroes)}";
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
        _heroPanel.Pool.TokenClicked += OnPoolTokenClicked;
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
        _inventory.BlockedActionClicked += action =>
            Log($"{action}: {AvailableCardActions(ActiveHero, _session.State.Turn).Explain(action)}", Palette.TextDim);
        right.AddChild(_inventory);

        right.AddChild(UiTheme.Heading(UiTheme.Label(new Vector2(8, 169), new Vector2(160, 12), "DICE", color: Palette.Trim), 1));
        _dice = new DiceTray { Position = new Vector2(8, 182), Size = new Vector2(160, 80) };
        _dice.DieClicked += index => ExecuteForActiveHero(heroId => new RerollDie(heroId, index));
        right.AddChild(_dice);

        _luckHint = UiTheme.Label(new Vector2(8, 263), new Vector2(160, 14), "Luck: click a die to reroll it.", color: Palette.Heal);
        right.AddChild(_luckHint);

        _confirm = UiTheme.Button(new Vector2(8, 279), new Vector2(160, 18), "Confirm attack");
        _confirm.Pressed += () => ExecuteForActiveHero(heroId => new ConfirmAttack(heroId));
        right.AddChild(_confirm);

        // After a 6: roll the Power Die into this roll, or keep it for later.
        _addPower = UiTheme.Button(new Vector2(8, 279), new Vector2(78, 18), "Add Power Die");
        _addPower.Pressed += () => ExecuteForActiveHero(heroId => new AddPowerDie(heroId));
        right.AddChild(_addPower);
        _bankPower = UiTheme.Button(new Vector2(90, 279), new Vector2(78, 18), "Bank it");
        _bankPower.Pressed += () => ExecuteForActiveHero(heroId => new BankPowerDie(heroId));
        right.AddChild(_bankPower);

        // Four buttons in a row: Wend, Search, Steal and Shove.
        _roll = UiTheme.Button(new Vector2(8, 303), new Vector2(38, 20), "Wend");
        _roll.TooltipText = "Roll your selected Wend dice (plus your Wend bonus) to move.";
        _roll.Pressed += () =>
        {
            if (SelectedMoveDice < 1)
            {
                Log("Select at least one Move die.", Palette.TextDim);
                return;
            }

            ExecuteForActiveHero(heroId => new RollMovement(heroId, SelectedMoveDice, !_speedHeld, SelectedPowerDice));
        };
        right.AddChild(_roll);

        _search = UiTheme.Button(new Vector2(48, 303), new Vector2(38, 20), "Search");
        _search.Pressed += () => ExecuteOdHand((heroId, dice, tools, power) => new Search(heroId, dice, tools, power));
        right.AddChild(_search);

        _steal = UiTheme.Button(new Vector2(88, 303), new Vector2(38, 20), "Steal");
        _steal.TooltipText = "Steal with your selected Od Hand dice: click it, then an adjacent monster or hero. Once a turn.";
        _steal.Pressed += () =>
        {
            _stealing = !_stealing;
            _shoving = false;
            if (_stealing)
            {
                Log("Click a monster or hero next to you to steal from them. Right-click to cancel.", Palette.Target with { A = 1 });
            }

            RefreshPanels();
        };
        right.AddChild(_steal);

        _shove = UiTheme.Button(new Vector2(128, 303), new Vector2(40, 20), "Shove");
        _shove.TooltipText = "Shove with your selected Rumble dice: click it, then a monster next to you. Beat its defense to push it a tile, onto a trap or into a pit.";
        _shove.Pressed += () =>
        {
            _shoving = !_shoving;
            _stealing = false;
            if (_shoving)
            {
                Log("Click a monster next to you to shove it. Right-click to cancel.", Palette.Target with { A = 1 });
            }

            RefreshPanels();
        };
        right.AddChild(_shove);

        _endTurn = UiTheme.Button(new Vector2(8, 329), new Vector2(160, 22), "End turn");
        _endTurn.Pressed += () =>
        {
            CancelSelection();
            ExecuteForActiveHero(heroId => new EndTurn(heroId));
        };
        right.AddChild(_endTurn);
        BuildOrders(right);

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
