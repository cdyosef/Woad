using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// One run on screen. Turns clicks into commands for the rules engine (Woad.Core) and plays the
/// resulting events back as animations, one at a time. Input is ignored while events play.
/// </summary>
public partial class GameScreen : Control
{
    private const float SideWidth = 176;
    private const float StripHeight = 36;
    private const double StepSeconds = 0.09;
    private const int LogLines = 3;

    private static readonly Vector2 BoardOrigin = new(SideWidth, StripHeight);

    private readonly Dictionary<int, TokenView> _tokens = [];
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<int, string> _cardNames = [];
    private readonly List<string> _messages = [];

    private GameSession _session = null!;
    private EventNarrator _narrator = null!;
    private BoardView _board = null!;
    private HeroPanel _heroPanel = null!;
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
    private Button _playAgain = null!;
    private Button _leave = null!;

    private bool _busy;
    private bool _confirmingLeave;
    private bool _autoplay;
    private int _autoplayCommandsLeft = 400;
    private bool _trayShowsMovement;
    private bool _targeting;
    private int? _selectedCardId;

    private Hero? ActiveHero => _session.State.ActiveHero;

    private Room ShownRoom => _session.State.RoomOf(ActiveHero ?? _session.State.Heroes[0]);

    /// <summary>The party, seed and dungeon to play. Set before adding the screen to the tree.</summary>
    public RunSetup Setup { get; set; } = null!;

    public ContentCatalog Content { get; init; } = ContentCatalog.LoadDefault();

    /// <summary>The player chose to leave the run (abandoning it, or after it ended).</summary>
    public event Action? ExitRequested;

    internal bool IsBusy => _busy;

    internal GameSession Session => _session;

    internal Button MenuButton => _menu;

    public override void _Ready()
    {
        Size = new Vector2(640, 360);
        _autoplay = OS.GetCmdlineUserArgs().Contains("--autoplay");
        if (_autoplay && OS.GetCmdlineUserArgs().Contains("--fast"))
        {
            Engine.TimeScale = 8;
        }

        _narrator = new EventNarrator(NameOf, CardNameOf);
        BuildLayout();
        StartRun(Setup);

        if (OS.GetCmdlineUserArgs().Contains("--uitest"))
        {
            RunUiTest();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            CancelSelection();
            GetViewport().SetInputAsHandled();
        }
    }

    private async void StartRun(RunSetup setup)
    {
        Setup = setup;
        foreach (TokenView token in _tokens.Values)
        {
            token.QueueFree();
        }

        _tokens.Clear();
        _names.Clear();
        _cardNames.Clear();
        _messages.Clear();
        _selectedCardId = null;
        _targeting = false;
        _dice.Clear();

        _confirmingLeave = false;
        _session = new GameSession(Content, setup);
        SyncBoard();
        await PlayEvents(_session.Begin());
    }

    private async void Execute(GameCommand command) => await ExecuteAsync(command);

    private async Task<bool> ExecuteAsync(GameCommand command)
    {
        if (_busy)
        {
            return false;
        }

        CommandResult result = _session.Execute(command);
        if (!result.IsAccepted)
        {
            Log(result.Error!, Palette.Danger);
            return false;
        }

        _confirmingLeave = false;
        if (command is UseCard)
        {
            _targeting = false;
            _board.SetBlast([]);
        }

        await PlayEvents(result.Events);
        return true;
    }

    // ---- Autoplay (debug): run with `-- --autoplay` -----------------------------------------

    private async void AutoplayNext()
    {
        await Pause(0.15);
        if (_busy)
        {
            return;
        }

        bool roomCleared = ShownRoom.Monsters.Count == 0;
        if (_session.State.Phase != RunPhase.InProgress || roomCleared || _autoplayCommandsLeft-- <= 0)
        {
            GD.Print(roomCleared ? "Autoplay: room cleared." : "Autoplay: stopping.");
            Hero[] heroes = [.. _session.State.Heroes];
            GD.Print($"Autoplay finished: {_session.State.Phase} after round {_session.State.Round}; " +
                     string.Join(", ", heroes.Select(hero => $"{hero.Name} {hero.Status} {hero.Hearts}/{hero.MaxHearts} hearts, {hero.Kills} kills")));
            GetTree().Quit();
            return;
        }

        GameCommand command = AutoplayBot.NextCommand(_session);
        if (!await ExecuteAsync(command))
        {
            GD.PushError($"Autoplay command was refused: {command}");
            GetTree().Quit(1);
        }
    }

    // ---- Event playback -------------------------------------------------------------------

    private async Task PlayEvents(IReadOnlyList<GameEvent> events)
    {
        _busy = true;
        RefreshPanels();
        try
        {
            foreach (GameEvent gameEvent in events)
            {
                await Play(gameEvent);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Event playback failed: {exception}");
        }
        finally
        {
            _busy = false;
            Refresh();
        }

        if (_autoplay)
        {
            AutoplayNext();
        }
    }

    private async Task Play(GameEvent gameEvent)
    {
        if (gameEvent is MonsterSpawned spawned)
        {
            _names[spawned.MonsterId] = _session.Content.GetMonster(spawned.MonsterDefinitionId).Name;
        }

        if (_narrator.Describe(gameEvent) is { } message)
        {
            Log(message);
            if (_autoplay)
            {
                GD.Print($"  {message}");
            }
        }

        switch (gameEvent)
        {
            case TurnStarted turn:
                _selectedCardId = null;
                _targeting = false;
                _trayShowsMovement = false;
                _dice.Clear();
                Refresh();
                Hero hero = _session.State.GetHero(turn.HeroId);
                await _banner.Flash($"{hero.Name}'s turn", Palette.Seat(hero.Seat));
                break;

            case EnemyPhaseStarted:
                await _banner.Flash("The monsters stir...", Palette.Danger, 0.3);
                break;

            case MovementRolled rolled:
                _trayShowsMovement = true;
                await _dice.ShowRoll($"{NameOf(rolled.HeroId)} rolls to move", rolled.Roll, 0, newGroup: true);
                break;

            case DieRerolled rerolled:
                await _dice.Reroll(rerolled.DieIndex, rerolled.NewFace);
                break;

            case HeroMoved moved:
                await Walk(moved.HeroId, moved.Path);
                break;

            case MonsterMoved moved:
                await Walk(moved.MonsterId, moved.Path);
                break;

            case AttackRolled attack:
                _trayShowsMovement = false;
                await Lunge(attack.AttackerId, attack.TargetIds.FirstOrDefault());
                await _dice.ShowRoll($"{NameOf(attack.AttackerId)} attacks", attack.Roll, GameRules.HitFace, newGroup: true);
                break;

            case DefenseRolled defense:
                int blockFace = IsHero(defense.DefenderId) ? GameRules.HeroBlockFace : GameRules.MonsterBlockFace;
                await _dice.ShowRoll($"{NameOf(defense.DefenderId)} defends", defense.Roll, blockFace, newGroup: false);
                break;

            case Damaged damaged:
                await ShowDamage(damaged);
                break;

            case HeroHealed healed when _tokens.TryGetValue(healed.HeroId, out TokenView? token):
                token.SetHearts(healed.Hearts);
                _board.FloatText(token.Tile, $"+{healed.Amount}", Palette.Heal);
                await Pause(0.3);
                break;

            case MonsterDefeated defeated:
                await FadeOut(defeated.MonsterId);
                break;

            case HeroDowned downed:
                await FadeOut(downed.HeroId);
                _board.QueueRedraw();
                break;

            case MonsterCollapsed collapsed when _tokens.TryGetValue(collapsed.MonsterId, out TokenView? token):
                token.Collapsed = true;
                await Pause(0.3);
                break;

            case MonsterReassembled risen when _tokens.TryGetValue(risen.MonsterId, out TokenView? token):
                token.Collapsed = false;
                token.SetHearts(risen.Hearts);
                await Tint(token, Palette.TrimBright);
                break;

            case MonsterSpawned:
                SpawnToken((MonsterSpawned)gameEvent);
                await Pause(0.15);
                break;

            case MonsterStunned stunned when _tokens.TryGetValue(stunned.MonsterId, out TokenView? token):
                token.Stunned = true;
                await Tint(token, Palette.TrimBright);
                break;

            case MonsterTurnSkipped skipped when _tokens.TryGetValue(skipped.MonsterId, out TokenView? token):
                token.Stunned = false;
                await Pause(0.3);
                break;

            case ChestOpened or CardDropped or LootPickedUp:
                _board.QueueRedraw();
                break;

            case RunEnded ended:
                string title = ended.WinnerHeroIds.Count == 0
                    ? "The dungeon claims you"
                    : $"{string.Join(" & ", ended.WinnerHeroIds.Select(NameOf))} wins!";
                await _banner.Flash(title, Palette.TrimBright, 1.2);
                break;
        }
    }

    private async Task Walk(int entityId, IReadOnlyList<GridPosition> path)
    {
        if (path.Count == 0 || !_tokens.TryGetValue(entityId, out TokenView? token))
        {
            return;
        }

        Tween tween = CreateTween();
        foreach (GridPosition step in path)
        {
            tween.TweenProperty(token, "position", BoardView.TileOrigin(step), StepSeconds);
        }

        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task Lunge(int attackerId, int targetId)
    {
        if (!_tokens.TryGetValue(attackerId, out TokenView? attacker) || !_tokens.TryGetValue(targetId, out TokenView? target))
        {
            return;
        }

        Vector2 home = attacker.Position;
        Vector2 toward = home + (target.Position - home).Normalized() * 7;
        Tween tween = CreateTween();
        tween.TweenProperty(attacker, "position", toward, 0.07);
        tween.TweenProperty(attacker, "position", home, 0.09);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task ShowDamage(Damaged damaged)
    {
        if (!_tokens.TryGetValue(damaged.TargetId, out TokenView? token))
        {
            return;
        }

        token.SetHearts(damaged.HeartsLeft);
        if (damaged.Amount > 0)
        {
            _board.FloatText(token.Tile, $"-{damaged.Amount}", Palette.Danger);
            await Tint(token, new Color(1, 0.3f, 0.3f));
        }
        else
        {
            _board.FloatText(token.Tile, "Blocked", Palette.Text);
            await Pause(0.25);
        }
    }

    private async Task FadeOut(int entityId)
    {
        if (!_tokens.Remove(entityId, out TokenView? token))
        {
            return;
        }

        Tween tween = CreateTween();
        tween.TweenProperty(token, "modulate:a", 0.0, 0.3);
        await ToSignal(tween, Tween.SignalName.Finished);
        token.QueueFree();
    }

    private async Task Tint(CanvasItem item, Color color)
    {
        Tween tween = CreateTween();
        tween.TweenProperty(item, "modulate", color, 0.06);
        tween.TweenProperty(item, "modulate", Colors.White, 0.2);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task Pause(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private void SpawnToken(MonsterSpawned spawned)
    {
        if (spawned.RoomId != ShownRoom.Id || ShownRoom.FindMonster(spawned.MonsterId) is not { } monster)
        {
            return;
        }

        TokenView token = MonsterToken(monster);
        token.Modulate = Colors.Transparent;
        CreateTween().TweenProperty(token, "modulate", Colors.White, 0.2);
    }

    // ---- Syncing the view with the rules state -------------------------------------------

    private void Refresh()
    {
        SyncBoard();
        RefreshPanels();
    }

    /// <summary>Snaps every token to the true state; corrects anything an animation missed.</summary>
    private void SyncBoard()
    {
        Room room = ShownRoom;
        _board.ShowRoom(room);
        var present = new HashSet<int>();

        foreach (Hero hero in _session.State.ActiveHeroesIn(room))
        {
            _names[hero.Id] = hero.Name;
            TokenView token = TokenFor(hero.Id, () => new TokenView
            {
                EntityId = hero.Id,
                IsHero = true,
                Glyph = hero.Name[..1].ToUpperInvariant(),
                Fill = Palette.Seat(hero.Seat),
            });
            token.Position = BoardView.TileOrigin(hero.Position);
            token.SetHearts(hero.Hearts, hero.MaxHearts);
            token.Active = hero == ActiveHero;
            present.Add(hero.Id);
        }

        foreach (Monster monster in room.Monsters)
        {
            TokenView token = MonsterToken(monster);
            token.Position = BoardView.TileOrigin(monster.Position);
            token.SetHearts(monster.Hearts, monster.Definition.Hearts);
            token.Stunned = monster.StunnedTurns > 0;
            token.Collapsed = monster.Collapsed;
            present.Add(monster.Id);
        }

        foreach (int stale in _tokens.Keys.Except(present).ToList())
        {
            _tokens[stale].QueueFree();
            _tokens.Remove(stale);
        }

        RememberCardNames();
    }

    private TokenView MonsterToken(Monster monster)
    {
        _names[monster.Id] = monster.Definition.Name;
        TokenView token = TokenFor(monster.Id, () => new TokenView
        {
            EntityId = monster.Id,
            IsHero = false,
            Glyph = char.ToUpperInvariant(monster.Definition.Glyph).ToString(),
            Fill = Palette.Monster(monster.Definition.Id),
            Position = BoardView.TileOrigin(monster.Position),
        });
        token.SetHearts(monster.Hearts, monster.Definition.Hearts);
        return token;
    }

    private TokenView TokenFor(int entityId, Func<TokenView> create)
    {
        if (!_tokens.TryGetValue(entityId, out TokenView? token))
        {
            token = create();
            _tokens[entityId] = token;
            _board.AddChild(token);
        }

        return token;
    }

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

        bool idle = !_busy && turn is not null;
        bool pending = turn?.PendingAttack is not null;
        _roll.Disabled = !(idle && !pending && turn!.MovementRoll is null);
        _endTurn.Disabled = !(idle && !pending);
        _confirm.Visible = pending;
        _confirm.Disabled = _busy;

        bool canReroll = idle && hero!.LuckRemaining > 0
            && (pending || (_trayShowsMovement && turn!.MovementRoll is not null && !turn.HasMoved));
        _dice.Rerollable = canReroll;
        _luckHint.Visible = canReroll;

        bool ended = state.Phase == RunPhase.Ended && !_busy;
        _playAgain.Visible = _leave.Visible = ended;
        _menu.Disabled = _busy;
        _menu.Text = _confirmingLeave ? "Leave?" : "Menu";

        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        if (_busy || ActiveHero is not { } hero || _session.State.Turn is not { } turn)
        {
            _board.ClearHighlights();
            return;
        }

        if (_targeting && _selectedCardId is int cardId)
        {
            _board.SetHighlights([], [], _session.GetCardTargets(cardId), []);
            return;
        }

        Room room = _session.State.RoomOf(hero);
        var interactive = new List<GridPosition>();
        if (!turn.HasActed && turn.PendingAttack is null)
        {
            interactive.AddRange(hero.Position.OrthogonalNeighbors()
                .Where(tile => room.Layout[tile] == TileKind.Chest && !room.IsChestOpened(tile)));
            if (room.LootAt(hero.Position).Count > 0)
            {
                interactive.Add(hero.Position);
            }
        }

        _board.SetHighlights(
            _session.GetReachableTiles().Keys,
            _session.GetAttackableMonsters().Select(monster => monster.Position),
            [],
            interactive);
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
        IEnumerable<string> heroes = state.Heroes.Select(hero =>
        {
            string name = Escape(hero.Name);
            string color = Palette.Hex(Palette.Seat(hero.Seat));
            string dim = Palette.Hex(Palette.TextDim);
            return hero.Status switch
            {
                HeroStatus.Downed => $"[color={dim}][s]{name}[/s][/color]",
                HeroStatus.Escaped => $"[color={dim}]{name} (escaped)[/color]",
                _ when hero == ActiveHero => $"[color={color}][u]{name}[/u][/color]",
                _ => $"[color={color}]{name}[/color]",
            };
        });

        string theme = Escape(_session.Content.GetTheme(state.ThemeId).Name);
        return $"[color={Palette.Hex(Palette.Trim)}]ROUND {state.Round}[/color]   {string.Join("  ", heroes)}" +
               $"   [color={Palette.Hex(Palette.TextDim)}]{theme}[/color]";
    }

    // ---- Input --------------------------------------------------------------------------

    private void OnTileClicked(GridPosition tile)
    {
        if (_busy || ActiveHero is not { } hero || _session.State.Turn is not { } turn)
        {
            return;
        }

        if (_targeting && _selectedCardId is int cardId)
        {
            if (_session.GetCardTargets(cardId).Contains(tile))
            {
                Execute(new UseCard(hero.Id, cardId, tile));
            }
            else
            {
                CancelSelection();
            }

            return;
        }

        Room room = _session.State.RoomOf(hero);
        if (room.MonsterAt(tile) is { } monster)
        {
            Execute(new AttackMonster(hero.Id, monster.Id));
        }
        else if (room.Layout[tile] == TileKind.Chest)
        {
            Execute(new OpenChest(hero.Id, tile));
        }
        else if (tile == hero.Position)
        {
            if (room.LootAt(tile).Count > 0)
            {
                Execute(new PickUpLoot(hero.Id));
            }
        }
        else if (turn.MovementRoll is null && !turn.MovementLocked)
        {
            Log("Roll to move first.", Palette.TextDim);
        }
        else
        {
            Execute(new MoveHero(hero.Id, tile));
        }
    }

    private void OnTileHovered(GridPosition? tile)
    {
        _hoverInfo.Text = tile is { } hovered ? DescribeTile(hovered) : "";

        if (_targeting && tile is { } target && _selectedCardId is int cardId
            && ActiveHero?.FindCard(cardId)?.Card.Definition.Effect is { AreaRadius: > 0 } effect
            && _session.GetCardTargets(cardId).Contains(target))
        {
            _board.SetBlast(RoomGrid.AllPositions().Where(position => position.ChebyshevDistanceTo(target) <= effect.AreaRadius));
        }
        else
        {
            _board.SetBlast([]);
        }
    }

    private string DescribeTile(GridPosition tile)
    {
        Room room = ShownRoom;
        if (room.MonsterAt(tile) is { } monster)
        {
            MonsterDefinition definition = monster.Definition;
            return $"{definition.Name}: {monster.Hearts}/{definition.Hearts} hearts, move {definition.Move}, " +
                   $"attack {definition.AttackDice}, defense {definition.DefenseDice}{Traits(monster)}";
        }

        if (_session.State.ActiveHeroesIn(room).FirstOrDefault(hero => hero.Position == tile) is { } standing)
        {
            return $"{standing.Name}: {standing.Hearts}/{standing.MaxHearts} hearts";
        }

        if (room.LootAt(tile) is { Count: > 0 } loot)
        {
            return "Loot: " + string.Join(", ", loot.Select(card => card.Definition.Name));
        }

        string terrain = room.Layout[tile] switch
        {
            TileKind.Chest when room.IsChestOpened(tile) => "An empty chest.",
            TileKind.Chest => "A closed chest. Stand next to it and click to open it.",
            TileKind.Wall => "A stone pillar.",
            TileKind.Pit => "A bottomless pit. Only flyers cross it.",
            _ => "",
        };

        if (_session.GetReachableTiles().TryGetValue(tile, out int steps))
        {
            terrain = $"{steps} step{(steps == 1 ? "" : "s")} away. {terrain}";
        }

        return terrain;
    }

    private static string Traits(Monster monster)
    {
        MonsterDefinition definition = monster.Definition;
        var traits = new List<string>();
        if (definition.AttackPattern == AttackPattern.Reach)
        {
            traits.Add($"reach {definition.AttackRange}");
        }

        if (definition.AttackPattern == AttackPattern.Ranged)
        {
            traits.Add($"ranged {definition.AttackRange}");
        }

        if (definition.Flying)
        {
            traits.Add("flies");
        }

        if (definition.SplitsInto is not null)
        {
            traits.Add("splits");
        }

        if (definition.Reassembles && !monster.HasReassembled)
        {
            traits.Add("reassembles");
        }

        if (monster.StunnedTurns > 0)
        {
            traits.Add("stunned");
        }

        return traits.Count == 0 ? "" : $" ({string.Join(", ", traits)})";
    }

    private void SelectCard(int cardId)
    {
        if (_busy)
        {
            return;
        }

        _selectedCardId = _selectedCardId == cardId ? null : cardId;
        _targeting = false;
        _board.SetBlast([]);
        RefreshPanels();
    }

    private void CancelSelection()
    {
        _selectedCardId = null;
        _targeting = false;
        _board.SetBlast([]);
        RefreshPanels();
    }

    private void OnCardAction(CardAction action)
    {
        if (_busy || _selectedCardId is not int cardId || ActiveHero is not { } hero || hero.FindCard(cardId) is not var (card, _))
        {
            return;
        }

        switch (action)
        {
            case CardAction.Use when card.Definition.Effect?.Kind == CardEffectKind.Heal:
                Execute(new UseCard(hero.Id, cardId));
                break;
            case CardAction.Use:
                _targeting = true;
                Log($"Choose a target for the {card.Definition.Name}. Right-click to cancel.", Palette.Target with { A = 1 });
                RefreshPanels();
                break;
            case CardAction.Equip:
                Execute(new EquipCard(hero.Id, cardId));
                break;
            case CardAction.Belt:
                Execute(new BeltCard(hero.Id, cardId));
                break;
            case CardAction.Stow:
                Execute(new StowCard(hero.Id, cardId));
                break;
            case CardAction.Drop:
                Execute(new DropCard(hero.Id, cardId));
                break;
        }
    }

    // ---- Names and messages ---------------------------------------------------------------

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

    // ---- Layout -----------------------------------------------------------------------------

    private void BuildLayout()
    {
        AddChild(new ColorRect { Color = Palette.Background, Size = new Vector2(640, 360), MouseFilter = MouseFilterEnum.Ignore });

        _heroPanel = new HeroPanel();
        _heroPanel.CardSelected += SelectCard;
        AddChild(_heroPanel);

        AddChild(new Panel { Position = new Vector2(SideWidth, 0), Size = new Vector2(BoardView.Pixels, StripHeight) });
        _header = RichLabel(new Vector2(SideWidth + 6, 4), new Vector2(BoardView.Pixels - 56, 14));
        _menu = UiTheme.Button(new Vector2(SideWidth + BoardView.Pixels - 48, 4), new Vector2(42, 15), "Menu");
        _menu.Pressed += () =>
        {
            if (_confirmingLeave || _session.State.Phase == RunPhase.Ended)
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

        _playAgain = UiTheme.Button(BoardOrigin + new Vector2(40, 196), new Vector2(100, 22), "Play again");
        _playAgain.Visible = false;
        _playAgain.Pressed += () => StartRun(Setup with { Seed = (ulong)DateTime.UtcNow.Ticks });
        AddChild(_playAgain);

        _leave = UiTheme.Button(BoardOrigin + new Vector2(148, 196), new Vector2(100, 22), "Leave");
        _leave.Visible = false;
        _leave.Pressed += () => ExitRequested?.Invoke();
        AddChild(_leave);
    }

    private void ExecuteForActiveHero(Func<int, GameCommand> command)
    {
        if (ActiveHero is { } hero)
        {
            Execute(command(hero.Id));
        }
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
