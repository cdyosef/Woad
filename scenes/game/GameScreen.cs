using Godot;
using Woad.Core.Bots;
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
/// resulting events back as animations, one at a time, following the action from room to room.
/// Input is ignored while events play.
/// </summary>
public partial class GameScreen : Control
{
    private const double StepSeconds = 0.09;
    private const int AutoplayCommandLimit = 4000;

    private GameSession _session = null!;
    private EventNarrator _narrator = null!;

    private bool _busy;
    private bool _finished;
    private bool _autoplay;
    private int _autoplayCommands;
    private bool _confirmingLeave;
    private bool _trayShowsMovement;
    private bool _targeting;
    private int? _selectedCardId;

    /// <summary>The party, seed and dungeon to play. Set before adding the screen to the tree.</summary>
    public RunSetup Setup { get; set; } = null!;

    public ContentCatalog Content { get; init; } = ContentCatalog.LoadDefault();

    /// <summary>The player abandoned the run. Nothing is saved.</summary>
    public event Action? ExitRequested;

    /// <summary>The run ended; raised once, shortly after the final events have played.</summary>
    public event Action<IReadOnlyList<HeroRunResult>>? RunFinished;

    internal bool IsBusy => _busy;

    internal GameSession Session => _session;

    internal Button MenuButton => _menu;

    internal Button RollButton => _roll;

    /// <summary>The middle of a board tile, in screen (640x360) coordinates.</summary>
    internal Vector2 TileCenter(GridPosition tile) =>
        _board.GetGlobalRect().Position + BoardView.TileOrigin(tile) + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f);

    private Hero? ActiveHero => _session.State.ActiveHero;

    public override void _Ready()
    {
        Size = new Vector2(640, 360);
        string[] args = OS.GetCmdlineUserArgs();
        _autoplay = args.Contains("--autoplay");
        if (_autoplay && args.Contains("--fast"))
        {
            Engine.TimeScale = 8;
        }

        _narrator = new EventNarrator(NameOf, CardNameOf);
        BuildLayout();
        StartRun(Setup);

        if (args.Contains("--uitest"))
        {
            RunUiTest();
        }
        else if (args.Contains("--floortest"))
        {
            RunFloorTest();
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
        _names.Clear();
        _cardNames.Clear();
        _messages.Clear();
        _selectedCardId = null;
        _targeting = false;
        _confirmingLeave = false;
        _finished = false;
        _dice.Clear();

        _session = new GameSession(Content, setup);
        ReloadFigures();
        ShowRoom(_session.State.RoomOf(_session.State.Heroes[0]).Id);
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

    private void ExecuteForActiveHero(Func<int, GameCommand> command)
    {
        if (ActiveHero is { } hero)
        {
            Execute(command(hero.Id));
        }
    }

    // ---- Autoplay (debug): run with `-- --autoplay` -----------------------------------------

    private async void AutoplayNext()
    {
        await Pause(0.1);
        if (_busy)
        {
            return;
        }

        if (_session.State.Phase != RunPhase.InProgress || ++_autoplayCommands > AutoplayCommandLimit)
        {
            GD.Print($"Autoplay finished: {_session.State.Phase} after round {_session.State.Round}; " +
                     string.Join(", ", _session.State.Heroes.Select(hero =>
                         $"{hero.Name} {hero.Status} on floor {hero.DeepestFloor}, {hero.Hearts}/{hero.MaxHearts} hearts, {hero.Kills} kills")));
            GetTree().Quit();
            return;
        }

        GameCommand command = SimpleBot.NextCommand(_session);
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
            ReloadFigures();
            ShowRoom(ActiveHero is { } hero ? hero.RoomId : _shownRoomId);
            RefreshPanels();
        }

        if (_session.State.Phase == RunPhase.Ended)
        {
            FinishRun();
        }
        else if (_autoplay)
        {
            AutoplayNext();
        }
    }

    /// <summary>Hands the results over (to be saved and shown) once, after a moment to take in the ending.</summary>
    private async void FinishRun()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        if (_autoplay)
        {
            AutoplayNext();
            return;
        }

        await Pause(1.2);
        RunFinished?.Invoke(RunResults.For(_session.State));
    }

    private async Task Play(GameEvent gameEvent)
    {
        if (gameEvent is MonsterSpawned spawned)
        {
            _names[spawned.MonsterId] = Content.GetMonster(spawned.MonsterDefinitionId).Name;
        }

        if (_narrator.Describe(gameEvent) is { } message)
        {
            Log(message);
            if (_autoplay)
            {
                GD.Print($"  {message}");
            }
        }

        if (RoomOf(gameEvent) is { } roomId && roomId != _shownRoomId)
        {
            ShowRoom(roomId);
            await Pause(0.25);
        }

        switch (gameEvent)
        {
            case TurnStarted turn:
                _selectedCardId = null;
                _targeting = false;
                _trayShowsMovement = false;
                _dice.Clear();
                RefreshPanels();
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

            case HeroEnteredRoom entered:
                await ChangeRoom(entered.HeroId, entered.RoomId, entered.Position);
                if (_session.State.Rooms[entered.RoomId].IsSealed && entered.FirstVisit)
                {
                    Log("The doors slam shut behind you!", Palette.Danger);
                }

                break;

            case HeroDescended descended:
                await ChangeRoom(descended.HeroId, descended.RoomId, descended.Position);
                await _banner.Flash($"Floor {descended.Floor}", Palette.TrimBright, 0.5);
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

            case HeroHealed healed:
                await ShowHealing(healed);
                break;

            case MonsterDefeated defeated:
                await RemoveFigure(defeated.MonsterId);
                break;

            case HeroDowned downed:
                await RemoveFigure(downed.HeroId);
                _board.QueueRedraw();
                break;

            case HeroEscaped escaped:
                await RemoveFigure(escaped.HeroId);
                await _banner.Flash($"{NameOf(escaped.HeroId)} escapes!", Palette.TrimBright, 0.6);
                break;

            case MonsterCollapsed collapsed:
                await UpdateFigure(collapsed.MonsterId, figure => figure.Collapsed = true, 0.3);
                break;

            case MonsterReassembled risen:
                await UpdateFigure(risen.MonsterId, figure => { figure.Collapsed = false; figure.Hearts = risen.Hearts; }, 0.3);
                break;

            case MonsterSpawned:
                await SpawnFigure((MonsterSpawned)gameEvent);
                break;

            case MonsterStunned stunned:
                await UpdateFigure(stunned.MonsterId, figure => figure.Stunned = true, 0.2);
                break;

            case MonsterTurnSkipped skipped:
                await UpdateFigure(skipped.MonsterId, figure => figure.Stunned = false, 0.3);
                break;

            case RoomCleared:
                _board.QueueRedraw();
                await Pause(0.2);
                break;

            case ChestOpened or CardDropped or LootPickedUp:
                _board.QueueRedraw();
                break;

            case PotSmashed smashed:
                _board.QueueRedraw();
                if (smashed.HeartsRestored > 0 && _figures.TryGetValue(smashed.HeroId, out Figure? smasher))
                {
                    smasher.Hearts += smashed.HeartsRestored;
                    if (_tokens.TryGetValue(smashed.HeroId, out TokenView? token))
                    {
                        token.SetHearts(smasher.Hearts);
                    }

                    _board.FloatText(smashed.Pot, $"+{smashed.HeartsRestored} heart", Palette.Heal);
                }
                else
                {
                    _board.FloatText(smashed.Pot, smashed.CardId is null ? "empty" : "coins!", smashed.CardId is null ? Palette.TextDim : Palette.Loot);
                }

                await Pause(0.3);
                break;

            case LootDropped dropped:
                _board.QueueRedraw();
                _board.FloatText(dropped.Position, "loot!", Palette.Loot);
                await Pause(0.15);
                break;

            case XpGained gained when _figures.TryGetValue(gained.HeroId, out Figure? earner) && earner.RoomId == _shownRoomId:
                _board.FloatText(earner.Position, $"+{gained.Amount} XP", Palette.TrimBright);
                break;

            case RunEnded ended:
                string title = ended.WinnerHeroIds.Count == 0
                    ? "The dungeon claims you"
                    : $"{string.Join(" & ", ended.WinnerHeroIds.Select(NameOf))} {(ended.WinnerHeroIds.Count == 1 ? "wins" : "win")}!";
                await _banner.Flash(title, Palette.TrimBright, 1.2);
                break;
        }
    }

    /// <summary>The room an event happens in, so the view can cut there before playing it.</summary>
    private string? RoomOf(GameEvent gameEvent) => gameEvent switch
    {
        TurnStarted turn => FigureRoom(turn.HeroId),
        HeroMoved moved => FigureRoom(moved.HeroId),
        MonsterMoved moved => FigureRoom(moved.MonsterId),
        AttackRolled attack => FigureRoom(attack.AttackerId),
        DefenseRolled defense => FigureRoom(defense.DefenderId),
        Damaged damaged => FigureRoom(damaged.TargetId),
        HeroHealed healed => FigureRoom(healed.HeroId),
        HeroDowned downed => FigureRoom(downed.HeroId),
        MonsterDefeated defeated => FigureRoom(defeated.MonsterId),
        MonsterCollapsed collapsed => FigureRoom(collapsed.MonsterId),
        MonsterReassembled risen => FigureRoom(risen.MonsterId),
        MonsterStunned stunned => FigureRoom(stunned.MonsterId),
        MonsterTurnSkipped skipped => FigureRoom(skipped.MonsterId),
        MonsterSpawned spawned => spawned.RoomId,
        RoomCleared cleared => cleared.RoomId,
        PotSmashed smashed => FigureRoom(smashed.HeroId),
        LootDropped dropped => dropped.RoomId,
        _ => null,
    };

    private async Task Pause(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
