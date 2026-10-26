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
    /// <summary>How many lines the log keeps to scroll back through.</summary>
    private const int LogLines = 100;

    private static readonly Vector2 BoardOrigin = new(SideWidth, StripHeight);

    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<int, string> _cardNames = [];
    private readonly List<string> _messages = [];

    private BoardView _board = null!;
    private HeroPanel _heroPanel = null!;
    private MinimapView _minimap = null!;
    private TurnFrame _turnFrame = null!;
    private PartyPanel _party = null!;
    private Button _partyButton = null!;
    private InventoryPanel _inventory = null!;
    private DiceBar _dice = null!;
    private TurnBanner _banner = null!;
    private RichTextLabel _header = null!;
    private Label _hoverInfo = null!;
    private RichTextLabel _log = null!;
    private Label _luckHint = null!;
    private Button _confirm = null!;
    private Button _addPower = null!;
    private Button _bankPower = null!;
    private Button _rumble = null!;
    private Button _prone = null!;
    private Button _dropRoll = null!;
    private Button _roll = null!;
    private Button _search = null!;
    private Button _steal = null!;
    private Button _shove = null!;
    private Button _leap = null!;
    private Button _endTurn = null!;
    private Button _menu = null!;
    private GridPosition? _hovered;

    // Wend dice and bonuses the player has deselected in the pool; everything else is used by the next move or roll.
    private int _moveDiceHeld;
    private bool _weaponHeld;
    private bool _speedHeld;
    private int _powerDiceSelected;
    private bool _toolsHeld;

    // How many Rumble and Od Hand dice each hero (by id) rolls at a time: their last choice, 1 to begin with.
    private readonly Dictionary<int, int> _rumbleChoice = [];
    private readonly Dictionary<int, int> _odHandChoice = [];

    /// <summary>Steal was pressed: the next click on an adjacent monster or hero tries to rob them.</summary>
    private bool _stealing;

    /// <summary>Shove was pressed: the next click on an adjacent monster tries to push it.</summary>
    private bool _shoving;

    /// <summary>Leap was pressed: the next click on a purple tile leaps toward it.</summary>
    private bool _leaping;

    /// <summary>Rumble was pressed (or the space bar): the next click on a target in reach attacks it.</summary>
    private bool _rumbling;

    private void RefreshPanels()
    {
        RefreshMerchantPanel();
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
        if (_party.Visible)
        {
            _party.ShowParty(_session, Locked ? null : hero);
        }

        // Someone with points to spend: the Party button says so.
        bool levelUp = !Locked && hero is not null && _session.State.Heroes.Any(other => (other == hero || other.OwnerId == hero.Id) && other.IsInPlay && other.PointsToSpend > 0);
        _partyButton.Text = levelUp ? "Party!" : "Party";
        if (_playingTurn is { Set: true, HeroId: null })
        {
            _turnFrame.Show("THE MONSTERS' TURN", Palette.Danger);
        }
        else
        {
            Hero? onShow = TurnOnShow;
            _turnFrame.Show(onShow is null || turn is null ? null : TurnTag(onShow), onShow is null ? Palette.TrimBright : Palette.Seat(onShow.Seat));
        }
        if (_hovered is null)
        {
            _hoverInfo.Text = Hint();
        }

        // Keeling Over, the only thing to do is roll the Keel Die (the End turn button), with any Power Dice selected.
        bool keeling = !Locked && turn is { KeelingOver: true };
        bool idle = !Locked && turn is not null && !keeling;
        bool choosingPower = turn?.UnsettledPowerDice > 0;
        bool pending = turn?.PendingRoll is not null || choosingPower;
        UpdatePool((idle && !pending) || keeling);
        bool held = hero is not null && _session.HeldBy(hero) is not null;
        _roll.Disabled = !(idle && !pending && _session.MoveDiceLeft > 0) || held;
        // Out on the Woarld Map there's nothing to search: the button retreats instead.
        bool skirmish = _session.State.Skirmish;
        _search.Text = skirmish ? "Retreat" : "Search";
        string? searchBlocked = skirmish ? _session.CheckRetreat() : _session.CheckSearch();
        _search.Disabled = !(idle && !pending) || searchBlocked is not null;
        _search.TooltipText = _session.State.Turn is null ? "" : searchBlocked ?? (skirmish
            ? $"Retreat: your selected Od Hand dice look for a way out, against the monsters' chase (a die more than there are of them, +{GameSession.RetreatChaseBonus}). Get away and you're off the field, the monsters left where they are; fail and you bolt in a panic and fall flat."
            : "Search the room with your selected Od Hand dice: finds secret doors, traps and stashes your total reaches.");
        _steal.Disabled = !(idle && !pending) || _session.OdHandDiceLeft < 1 || turn!.HasStolen;
        _stealing &= !_steal.Disabled;
        _steal.Text = _stealing ? "Cancel" : "Steal";
        _rumble.Disabled = !(idle && !pending) || _session.AttackDiceLeft < 1;
        _rumbling &= !_rumble.Disabled;
        _rumble.Text = _rumbling ? "Cancel" : "Rumble";
        _shove.Disabled = !(idle && !pending) || _session.AttackDiceLeft < 1;
        _leap.Disabled = !(idle && !pending) || _session.MoveDiceLeft < 1 || held;
        _leaping &= !_leap.Disabled;
        _leap.Text = _leaping ? "Cancel" : "Leap";
        _shoving &= !_shove.Disabled;
        _shove.Text = _shoving ? "Cancel" : "Shove";
        _endTurn.Disabled = !((idle && !pending) || keeling);
        _endTurn.Text = turn is { KeelingOver: true } ? "Roll Keel Die" : "End turn";
        _endTurn.TooltipText = turn is { KeelingOver: true }
            ? "Keeling Over: roll the Keel Die. A 6 is a mark of life and rolls a Power Die (another on a 6); a 1 is a mark of death and rolls a Doom Die (another on a 1). Every roll after the first adds Doom Dice: 1 on the second, 2 on the third... Select banked Power Dice to add them: their 6s are marks of life."
            : "";
        _confirm.Visible = turn?.PendingRoll is not null && !choosingPower;
        _confirm.Disabled = Locked;
        _addPower.Visible = _bankPower.Visible = choosingPower;
        _addPower.Disabled = _bankPower.Disabled = Locked;
        _bankPower.Text = hero is null ? "Bank it" : $"Bank it ({hero.Power.Banked}/{GameRules.MaxBankedPowerDice})";

        // Where Confirm and the Power Die buttons go, while there's no roll to settle: lie down or get up, and stop, drop and roll.
        bool lying = hero?.Prone == true;
        // The row above the dice buttons: Rumble, Shove and Prone (Drop & roll while burning), where Confirm
        // and the Power Die buttons go while a roll waits.
        bool waiting = turn?.PendingRoll is not null || choosingPower;
        _rumble.Visible = _shove.Visible = _prone.Visible = _dropRoll.Visible = !waiting;
        _prone.Text = lying ? "Get up" : "Prone";
        string? proneError = hero is null || turn is null ? "" : lying ? _session.CheckGetUp() : _session.CheckGoProne();
        _prone.Disabled = proneError is not null;
        _prone.TooltipText = proneError ?? (lying
            ? $"Get back on your feet: {GameRules.GetUpCost} steps."
            : $"Lie down: {GameRules.ProneCost} step. Every step costs double while prone; melee on you rolls an extra die, ranged attacks face an extra defense die. In deep water you dive: out of reach of ranged attacks.");
        string? dropError = hero is null || turn is null ? "" : _session.CheckStopDropRoll();
        _dropRoll.Visible &= hero?.FireDice > 0 && dropError is null;
        _prone.Visible &= !_dropRoll.Visible;
        _dropRoll.Disabled = dropError is not null;
        _dropRoll.TooltipText = dropError ?? "Stop, drop and roll: put the fire out. It takes your whole turn and leaves you prone.";

        bool canReroll = idle && hero!.LuckRemaining > 0
            && (pending || (_trayShowsMovement && turn!.MovementRoll is not null && !turn.HasMoved));
        _dice.Rerollable = canReroll;
        _luckHint.Visible = canReroll;

        _menu.Disabled = _busy || state.Phase == RunPhase.Ended;
        _menu.Text = _confirmingLeave ? "Leave?" : "Menu";

        RefreshOrders();
        UpdateHighlights();
        RefreshRummage();
        RefreshTrade();
    }

    /// <summary>Rumble (the button or the space bar): show the attack range and wait for a target, or put it away.</summary>
    private void RollWend()
    {
        if (SelectedMoveDice < 1)
        {
            Log("Select at least one Move die.", Palette.TextDim);
            return;
        }

        ExecuteForActiveHero(heroId => new RollMovement(heroId, SelectedMoveDice, !_speedHeld, SelectedPowerDice));
    }

    private void ToggleRumble()
    {
        if (_rumble.Disabled && !_rumbling)
        {
            return;
        }

        _rumbling = !_rumbling;
        _stealing = _shoving = _leaping = false;
        if (_rumbling)
        {
            Log("Click a target in red to attack it. Right-click to cancel.", Palette.Danger);
        }

        RefreshPanels();
    }

    /// <summary>What Rumble can strike from where the active hero stands: monsters, bombs and (with friendly fire on) allies.</summary>
    private List<GridPosition> RumbleTargets(Room room) =>
    [
        .. _session.GetAttackableMonsters().SelectMany(monster => monster.Tiles),
        .. _session.GetAttackableMerchants().Select(merchant => merchant.Position),
        .. _session.GetAttackableAllies().Select(ally => ally.Position),
        .. room.Bombs.Where(bomb => _session.CheckAttackBomb(bomb.Id) is null).Select(bomb => bomb.Position),
    ];

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
            _board.SetHighlights([], [], _session.GetShoveTargets(), []);
            return;
        }

        if (_leaping)
        {
            _board.SetHighlights([], [], [], [], jumps: _session.GetLeapTargets());
            return;
        }

        if (_rumbling)
        {
            List<GridPosition> reach = room.Positions
                .Where(tile => tile != hero.Position && GameSession.CanHeroReach(hero, room.Layout, hero.Position, tile))
                .ToList();
            _board.SetHighlights([], RumbleTargets(room), [], [], range: reach);
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
                || (room.Layout[tile] == TileKind.Gate && (hero.Keys > 0 || _session.CheckPickLock(tile) is null))
                || _session.CheckDisarm(tile) is null));
        }

        interactive.AddRange(_session.GetRousableAllies().Select(fallen => fallen.Position));
        if (OwnTileIsUsable(hero, room, turn))
        {
            interactive.Add(hero.Position);
        }

        // Pits and found traps can be walked into, at a cost: they show red over the blue.
        IReadOnlyCollection<GridPosition> reachable = _session.GetReachableTiles().Keys.ToList();
        _board.SetHighlights(
            reachable,
            _session.GetAttackableMonsters().Select(monster => monster.Position).Concat(_session.GetAttackableAllies().Select(ally => ally.Position)),
            [],
            interactive,
            hazards: reachable.Where(tile => room.Layout[tile] == TileKind.Pit || room.TrapAt(tile) is { Revealed: true }));
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
            _ when room.Breaches.ContainsKey(hero.Position) => canStep && !room.IsSealed,
            TileKind.StairsDown or TileKind.StairsUp => canStep,
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

        // Keeling Over, only banked Power Dice can be added (to the Keel roll).
        if (turn is { KeelingOver: true })
        {
            pool.ShowPool(DicePool.Rumble, 0, 0, 0, false);
            pool.ShowPool(DicePool.Wend, 0, 0, 0, false);
            pool.ShowPool(DicePool.OdHand, 0, 0, 0, false);
            pool.ShowPool(DicePool.Power, ActiveHero?.Power.Banked ?? 0, SelectedPowerDice, 0, false);
            ShowDefense(pool);
            return;
        }

        pool.ShowPool(DicePool.Rumble, _session.AttackDiceLeft, SelectedAttackDice, weapon, weapon > 0 && !_weaponHeld, bonusDice: _session.WeaponDiceLeft);
        pool.ShowPool(DicePool.Wend, _session.MoveDiceLeft, SelectedMoveDice, speed, speed < 0 || (speed > 0 && !_speedHeld), steps);
        pool.ShowPool(DicePool.OdHand, _session.OdHandDiceLeft, SelectedOdHandDice, tools, tools > 0 && !_toolsHeld, bonusDice: _session.ToolDiceLeft);
        pool.ShowPool(DicePool.Power, ActiveHero?.Power.Banked ?? 0, SelectedPowerDice, 0, false);
        ShowDefense(pool);
    }

    private void ShowDefense(DicePoolView pool)
    {
        if (ActiveHero is not { } hero)
        {
            return;
        }

        int baseDice = GameSession.BaseDefenseDice(hero);
        Stats stats = hero.EffectiveStats;
        pool.ShowDefense(baseDice, GameSession.ExtraDefenseDice(hero), GameSession.ExtraDefenseDiceReady(hero), Math.Max(0, stats.Armor), Math.Max(0, stats.Block), GameSession.BlockReady(hero) > 0,
            Math.Max(0, stats.GuardDice), GameSession.GuardDiceReady(hero));
    }

    /// <summary>Banked Power Dice start unselected: they're only spent when the player picks them.</summary>
    private int SelectedPowerDice => Math.Clamp(_powerDiceSelected, 0, ActiveHero?.Power.Banked ?? 0);

    /// <summary>
    /// Rumble and Od Hand roll the number of dice each hero last chose (1 to begin with), or as many as are
    /// left; the choice carries over from roll to roll and turn to turn. Wend rolls everything unless held back.
    /// </summary>
    private int SelectedAttackDice => Math.Min(ChosenDice(_rumbleChoice), _session.AttackDiceLeft);

    private int SelectedMoveDice => Math.Clamp(_session.MoveDiceLeft - _moveDiceHeld, 0, _session.MoveDiceLeft);

    private int SelectedOdHandDice => Math.Min(ChosenDice(_odHandChoice), _session.OdHandDiceLeft);

    private int ChosenDice(Dictionary<int, int> choices) => ActiveHero is { } hero && choices.TryGetValue(hero.Id, out int dice) ? dice : 1;

    /// <summary>Clicking a lit die drops one from the choice, a dark one adds one; never fewer than 1.</summary>
    private void Choose(Dictionary<int, int> choices, int index, int selected)
    {
        if (ActiveHero is { } hero)
        {
            choices[hero.Id] = Math.Max(1, index < selected ? selected - 1 : selected + 1);
        }
    }

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
                Choose(_rumbleChoice, index, SelectedAttackDice);
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
                Choose(_odHandChoice, index, SelectedOdHandDice);
                break;
            case DicePool.Power:
                _powerDiceSelected += index < SelectedPowerDice ? -1 : 1;
                break;
        }

        RefreshPanels();
    }

    /// <summary>Selects all the Wend dice and bonuses again (Rumble and Od Hand keep each hero's choice), after each command and at the start of a turn.</summary>
    private void ResetPool()
    {
        _moveDiceHeld = _powerDiceSelected = 0;
        _weaponHeld = _speedHeld = _toolsHeld = _stealing = _shoving = _leaping = _rumbling = false;
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
            (CardAction.Use, card.Definition.Reload is not null ? _session.CheckReload() : _session.CheckUse(cardId)),
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
            [CardAction.Use] = card.Definition.Reload is { } reload
                ? $"Reload: takes {(reload.OdHand == 1 ? "an Od Hand die" : $"{reload.OdHand} Od Hand dice")}{(reload.Wend > 0 ? " and a Wend die" : "")}."
                : location == CardLocation.Belt ? "Free: one belt item a turn." : "Takes your action.",
            [CardAction.Equip] = card.Definition.AltSlot is null ? gearCost
                : hero.SlotOf(card) is { } held ? $"Moves it to your {(held == EquipSlot.MainHand ? "off" : "main")} hand. {gearCost}"
                : $"Goes in either hand: you'll choose which. {gearCost}",
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
                HeroStatus.Dead => $"[color={dim}][s]{name}[/s][/color]",
                HeroStatus.KeelingOver when hero == ActiveHero => $"[color={Palette.Hex(Palette.Danger)}][u]{name}[/u] (down)[/color]",
                HeroStatus.KeelingOver => $"[color={Palette.Hex(Palette.Danger)}]{name}{(compact ? "" : " (down)")}[/color]",
                HeroStatus.Escaped => $"[color={dim}]{name} (out)[/color]",
                _ when hero == ActiveHero => $"[bgcolor={color}][color={Palette.Hex(Palette.Ink)}] {name} [/color][/bgcolor]",
                _ => $"[color={color}]{name}[/color]",
            };
        });

        int floor = _shownRoomId.Length > 0 ? ShownRoom.Floor : 1;
        string friendlyFire = state.FriendlyFire ? $"  [color={Palette.Hex(Palette.Danger)}]FF[/color]" : "";
        return $"[color={Palette.Hex(Palette.Trim)}]ROUND {state.Round}   FLOOR {floor}[/color]{friendlyFire}   {string.Join(compact ? " " : "  ", heroes)}";
    }

    /// <summary>"ARIA'S TURN", or for a companion "PIP'S TURN (ARIA'S COMPANION)".</summary>
    private string TurnTag(Hero hero)
    {
        string owner = hero.OwnerId is int ownerId ? $" ({NameOf(ownerId).ToUpperInvariant()}'S COMPANION)" : "";
        return $"{hero.Name.ToUpperInvariant()}'S TURN{owner}";
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
        _minimap = new MinimapView { Position = new Vector2(8, 266), Size = new Vector2(160, 90) };
        AddChild(_minimap);

        AddChild(new Panel { Position = new Vector2(SideWidth, 0), Size = new Vector2(BoardView.Pixels, StripHeight) });
        _header = RichLabel(new Vector2(SideWidth + 6, 4), new Vector2(BoardView.Pixels - 102, 14));
        _partyButton = UiTheme.Button(new Vector2(SideWidth + BoardView.Pixels - 94, 4), new Vector2(42, 15), "Party");
        _partyButton.TooltipText = "The Party: everyone's level, hearts and gear; level up, and set how your companions manage their things.";
        _partyButton.Pressed += () =>
        {
            _party.Visible = !_party.Visible;
            RefreshPanels();
        };
        AddChild(_partyButton);
        _menu = UiTheme.Button(new Vector2(SideWidth + BoardView.Pixels - 48, 4), new Vector2(42, 15), "Menu");
        _menu.Pressed += () =>
        {
            if (_confirmingLeave)
            {
                ExitRequested?.Invoke();
                return;
            }

            _confirmingLeave = true;
            Log(SavesRun ? "Click Leave? again to leave. The run is saved: continue it from Dungeon Run." : "Click Leave? again to abandon this run. Nothing will be saved.", Palette.TrimBright);
            RefreshPanels();
        };
        AddChild(_menu);
        _hoverInfo = UiTheme.Label(new Vector2(SideWidth + 6, 19), new Vector2(BoardView.Pixels - 12, 14), color: Palette.TextDim);
        _hoverInfo.AutowrapMode = TextServer.AutowrapMode.Off;
        _hoverInfo.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(_hoverInfo);

        // The board scrolls inside a frame the size of a standard room; around a smaller room, solid rock.
        var frame = new Control { Position = BoardOrigin, Size = new Vector2(BoardView.Pixels, BoardView.Pixels), ClipContents = true, MouseFilter = MouseFilterEnum.Pass };
        frame.AddChild(new ColorRect { Size = frame.Size, Color = Palette.Rock, MouseFilter = MouseFilterEnum.Ignore });
        _board = new BoardView();
        _board.TileClicked += OnTileClicked;
        _board.TileDoubleClicked += OnTileDoubleClicked;
        _board.TileHovered += OnTileHovered;
        _board.CancelRequested += CancelSelection;
        frame.AddChild(_board);
        frame.AddChild(new ScrollHints { Board = _board, Size = frame.Size });
        _turnFrame = new TurnFrame { Size = frame.Size };
        frame.AddChild(_turnFrame);
        AddChild(frame);

        // The Party menu, over the board while it's open.
        _party = new PartyPanel { Position = BoardOrigin, Size = new Vector2(BoardView.Pixels, BoardView.Pixels), Visible = false, ZIndex = 2 };
        _party.SpendRequested += (heroId, stat) => ExecuteForActiveHero(ownerId => new SpendPoint(ownerId, heroId, stat));
        _party.PolicyRequested += (companionId, policy) => ExecuteForActiveHero(ownerId => new SetCompanionPolicy(ownerId, companionId, policy));
        _party.CloseRequested += () =>
        {
            _party.Visible = false;
            RefreshPanels();
        };
        AddChild(_party);

        // Under the board, the dice bar: who rolls on the left, VS, who answers on the right.
        AddChild(new Panel { Position = new Vector2(SideWidth, StripHeight + BoardView.Pixels), Size = new Vector2(BoardView.Pixels, StripHeight) });
        _dice = new DiceBar { Position = new Vector2(SideWidth + 6, StripHeight + BoardView.Pixels + 2), Size = new Vector2(BoardView.Pixels - 12, 32) };
        _dice.DieClicked += index => ExecuteForActiveHero(heroId => new RerollDie(heroId, index));
        AddChild(_dice);

        var right = new Panel { Position = new Vector2(SideWidth + BoardView.Pixels, 0), Size = new Vector2(SideWidth, 360) };
        AddChild(right);

        _inventory = new InventoryPanel();
        _inventory.CardSelected += SelectCard;
        _inventory.HandChosen += (cardId, hand) => ExecuteForActiveHero(heroId => new EquipCard(heroId, cardId, hand));
        _inventory.ActionRequested += OnCardAction;
        _inventory.BlockedActionClicked += action =>
            Log($"{action}: {AvailableCardActions(ActiveHero, _session.State.Turn).Explain(action)}", Palette.TextDim);
        right.AddChild(_inventory);

        // The message log, under the cards: the newest line bright, older ones dimmed; scroll back with the wheel.
        _log = RichLabel(new Vector2(SideWidth + BoardView.Pixels + 8, 189), new Vector2(160, 74));
        _log.AddThemeConstantOverride("line_separation", -2);
        _log.ScrollActive = true;
        _log.ScrollFollowing = true;
        _log.MouseFilter = MouseFilterEnum.Stop;

        _luckHint = UiTheme.Label(new Vector2(8, 263), new Vector2(160, 14), "Luck: click a die to reroll it.", color: Palette.Heal);
        right.AddChild(_luckHint);

        _confirm = UiTheme.Button(new Vector2(8, 279), new Vector2(160, 18), "Confirm roll");
        _confirm.Pressed += () => ExecuteForActiveHero(heroId => new ConfirmAttack(heroId));
        right.AddChild(_confirm);

        // After a 6: roll the Power Die into this roll, or keep it for later.
        _addPower = UiTheme.Accent(UiTheme.Button(new Vector2(8, 279), new Vector2(78, 18), "Add Power Die"), Palette.Power);
        _addPower.Pressed += () => ExecuteForActiveHero(heroId => new AddPowerDie(heroId));
        right.AddChild(_addPower);
        _bankPower = UiTheme.Accent(UiTheme.Button(new Vector2(90, 279), new Vector2(78, 18), "Bank it"), Palette.Power);
        _bankPower.Pressed += () => ExecuteForActiveHero(heroId => new BankPowerDie(heroId));
        right.AddChild(_bankPower);

        _rumble = UiTheme.Accent(UiTheme.CompactButton(new Vector2(8, 279), new Vector2(52, 20), "Rumble"), Palette.DieRumble);
        _rumble.TooltipText = "Attack (R or Space): your attack range shows in red; click a target in it. Uses your selected Rumble dice and your weapon.";
        _rumble.Pressed += ToggleRumble;
        right.AddChild(_rumble);

        _prone = UiTheme.CompactButton(new Vector2(116, 279), new Vector2(52, 20), "Prone");
        _prone.Pressed += () => ExecuteForActiveHero(heroId => ActiveHero?.Prone == true ? new GetUp(heroId) : new GoProne(heroId));
        right.AddChild(_prone);
        _dropRoll = UiTheme.CompactButton(new Vector2(116, 279), new Vector2(52, 20), "Drop&roll");
        _dropRoll.Pressed += () => ExecuteForActiveHero(heroId => new StopDropRoll(heroId));
        right.AddChild(_dropRoll);

        // Five buttons in a row: Wend, Leap, Search, Steal and Shove.
        _roll = UiTheme.Accent(UiTheme.CompactButton(new Vector2(8, 303), new Vector2(38, 20), "Wend"), Palette.DieWend);
        _roll.TooltipText = "Roll your selected Wend dice (plus your Wend bonus) to move. (W)";
        _roll.Pressed += RollWend;
        right.AddChild(_roll);

        _leap = UiTheme.Accent(UiTheme.CompactButton(new Vector2(48, 303), new Vector2(38, 20), "Leap"), Palette.DieWend);
        _leap.TooltipText = $"Leap with one Wend die: pick a tile up to {GameRules.MaxLeap} away in a straight line, then roll; you carry half the roll.";
        _leap.Pressed += () =>
        {
            _leaping = !_leaping;
            _stealing = _shoving = _rumbling = false;
            if (_leaping)
            {
                Log("Click a purple tile to leap toward it. Right-click to cancel.", Palette.Power);
            }

            RefreshPanels();
        };
        right.AddChild(_leap);

        _search = UiTheme.Accent(UiTheme.CompactButton(new Vector2(88, 303), new Vector2(40, 20), "Search"), Palette.DieOdHand);
        _search.Pressed += () => ExecuteOdHand((heroId, dice, tools, power) =>
            _session.State.Skirmish ? new Retreat(heroId, dice, false, power) : new Search(heroId, dice, tools, power));
        right.AddChild(_search);

        _steal = UiTheme.Accent(UiTheme.CompactButton(new Vector2(130, 303), new Vector2(38, 20), "Steal"), Palette.DieOdHand);
        _steal.TooltipText = "Steal with your selected Od Hand dice: click it, then an adjacent monster or hero. Once a turn.";
        _steal.Pressed += () =>
        {
            _stealing = !_stealing;
            _shoving = _leaping = _rumbling = false;
            if (_stealing)
            {
                Log("Click a monster or hero next to you to steal from them. Right-click to cancel.", Palette.Target with { A = 1 });
            }

            RefreshPanels();
        };
        right.AddChild(_steal);

        _shove = UiTheme.Accent(UiTheme.CompactButton(new Vector2(62, 279), new Vector2(52, 20), "Shove"), Palette.DieRumble);
        _shove.TooltipText = "Shove with your selected Rumble dice: click it, then a monster next to you. Beat its defense to push it a tile, onto a trap or into a pit.";
        _shove.Pressed += () =>
        {
            _shoving = !_shoving;
            _stealing = _leaping = _rumbling = false;
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
            ExecuteForActiveHero(heroId => _session.State.Turn is { KeelingOver: true } ? new RollKeel(heroId, SelectedPowerDice) : new EndTurn(heroId));
        };
        right.AddChild(_endTurn);
        BuildOrders(right);

        BuildLootWindows();
        BuildMerchantWindow();
        BuildTradeWindow();
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
