using Godot;
using Woad.Core.Bots;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
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

    /// <summary>A saved run, already replayed to where it was left: played on instead of starting <see cref="Setup"/> afresh.</summary>
    public GameSession? Restored { get; init; }

    /// <summary>The run is saved as it goes (see <see cref="CommandPlayed"/>), so leaving keeps it.</summary>
    public bool SavesRun { get; init; }

    /// <summary>The player left the run.</summary>
    public event Action? ExitRequested;

    /// <summary>The rules accepted a command: the state is as it stands after it.</summary>
    public event Action<GameCommand, GameState>? CommandPlayed;

    /// <summary>The run ended; raised once, shortly after the final events have played.</summary>
    public event Action<IReadOnlyList<HeroRunResult>>? RunFinished;

    internal bool IsBusy => _busy;

    /// <summary>The player can't act: events are playing, or a companion is taking its turn.</summary>
    private bool Locked => _busy || ActiveHero is { IsCompanion: true };

    internal GameSession Session => _session;

    internal Button MenuButton => _menu;

    internal Button RollButton => _roll;

    internal Button EndTurnButton => _endTurn;

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

        _narrator = new EventNarrator(NameOf, CardNameOf, id => Content.FindSpell(id)?.Name ?? id);
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
        else if (args.Contains("--hazardtest"))
        {
            RunHazardTest();
        }
        else if (args.Contains("--keeltest"))
        {
            RunKeelTest();
        }
        else if (args.Contains("--firetest"))
        {
            RunFireTest();
        }
        else if (args.Contains("--cameratest"))
        {
            RunCameraTest();
        }
        else if (args.Contains("--bombtest"))
        {
            RunBombTest();
        }
        else if (args.Contains("--rumbletest"))
        {
            RunRumbleTest();
        }
        else if (args.Contains("--clicktest"))
        {
            RunClickTest();
        }
        else if (args.Contains("--loottest"))
        {
            RunLootTest();
        }
        else if (args.Contains("--tradetest"))
        {
            RunTradeTest();
        }
        else if (args.Contains("--monstertest"))
        {
            RunMonsterTest();
        }
        else if (args.Contains("--spelltest"))
        {
            RunSpellTest();
        }
        else if (args.Contains("--dietypetest"))
        {
            RunDieTypeTest();
        }
        else if (args.Contains("--magictest"))
        {
            RunMagicTest();
        }
        else if (args.Contains("--companiontest"))
        {
            RunCompanionTest();
        }
        else if (args.Contains("--partytest"))
        {
            RunPartyTest();
        }
        else if (args.Contains("--bosstest"))
        {
            RunBossTest();
        }
        else if (args.Contains("--dentest"))
        {
            RunDenTest();
        }
        else if (args.Contains("--merchanttest"))
        {
            RunMerchantTest();
        }
    }

    /// <summary>Hotkeys, before any focused button can take them as a press: R or Space is Rumble, W rolls Wend.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _session.State.Turn is null || ActiveHero is not { IsCompanion: false } || _autoplay)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Space or Key.R:
                ToggleRumble();
                break;
            case Key.W when !_roll.Disabled && _roll.Visible:
                RollWend();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
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

        _session = Restored ?? new GameSession(Content, setup);
        _board.ThemeId = setup.ThemeId;
        ReloadFigures();
        ShowRoom(_session.State.RoomOf(_session.State.Heroes[0]).Id);
        UpdateMusic();
        if (setup.FriendlyFire)
        {
            Log("Friendly fire is on (FF): blasts, attacks and shoves can hit your allies.", Palette.Danger);
        }

        if (Restored is not null)
        {
            Log($"Welcome back: round {_session.State.Round}, where you left off.", Palette.TrimBright);
            await PlayEvents([]);
            return;
        }

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

        CommandPlayed?.Invoke(command, _session.State);
        _confirmingLeave = false;
        ResetPool();
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
            Sfx.QuitGame(this);
            return;
        }

        GameCommand command = SimpleBot.NextCommand(_session);
        if (!await ExecuteAsync(command))
        {
            GD.PushError($"Autoplay command was refused: {command}");
            Sfx.QuitGame(this, 1);
        }
    }

    // ---- Companions: the game plays their turns ---------------------------------------------

    /// <summary>Plays a companion's next command, at a pace the players can follow.</summary>
    private async void CompanionNext()
    {
        await Pause(0.2);
        if (_busy || _session.State.Phase != RunPhase.InProgress || ActiveHero is not { IsCompanion: true } companion)
        {
            return;
        }

        GameCommand command = CompanionBot.NextCommand(_session);
        if (!await ExecuteAsync(command))
        {
            GD.PushError($"A companion's command was refused: {command}");
            await ExecuteAsync(new EndTurn(companion.Id));
        }
    }

    // ---- Event playback -------------------------------------------------------------------

    /// <summary>
    /// Whose turn the events being played back belong to (the state has often moved on by then): set
    /// while playing, a hero's id or null for the monsters; unset (the state's own) otherwise.
    /// </summary>
    private (bool Set, int? HeroId) _playingTurn;

    /// <summary>The hero whose turn is on show, or null while the monsters act.</summary>
    private Hero? TurnOnShow => _playingTurn.Set ? (_playingTurn.HeroId is int id ? _session.State.GetHero(id) : null) : ActiveHero;

    /// <summary>Marks whose turn is being played back, and moves the turn arrow to them (or off everyone).</summary>
    private void ShowTurnOf(int? heroId)
    {
        _playingTurn = (true, heroId);
        foreach ((int id, TokenView token) in _tokens)
        {
            token.Active = id == heroId;
            token.ZIndex = id == heroId ? 1 : 0;
        }
    }

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
            _playingTurn = default;
            ReloadFigures();
            ShowRoom(ActiveHero is { } hero ? hero.RoomId : _shownRoomId);
            RefreshPanels();
            UpdateMusic();
        }

        if (_session.State.Phase == RunPhase.Ended)
        {
            FinishRun();
        }
        else if (_autoplay)
        {
            AutoplayNext();
        }
        else if (ActiveHero is { IsCompanion: true })
        {
            CompanionNext();
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

        if (FocusOf(gameEvent) is int focus)
        {
            FocusOn(focus);
        }

        PlaySound(gameEvent);
        switch (gameEvent)
        {
            case TurnStarted turn:
                _selectedCardId = null;
                _showingOrders = false;
                _targeting = false;
                _trayShowsMovement = false;
                _dice.Clear();
                _goal = null;
                CloseRummage();
                CloseTrade();
                ResetCombo();
                ResetPool();
                ShowTurnOf(turn.HeroId);
                RefreshPanels();
                Hero hero = _session.State.GetHero(turn.HeroId);
                await _banner.Flash($"{hero.Name}'s turn", Palette.Seat(hero.Seat));
                break;

            case EnemyPhaseStarted:
                ShowTurnOf(null);
                RefreshPanels();
                await _banner.Flash("The monsters stir...", Palette.Danger, 0.3);
                break;

            case MovementRolled rolled:
                _trayShowsMovement = true;
                await _dice.ShowRoll($"{NameOf(rolled.HeroId)} rolls to move", rolled.Roll, rolled.Bonus, newGroup: true, rolled.PowerDice, DiceKind.Wend);
                break;

            case DieRerolled rerolled:
                await _dice.Reroll(rerolled.DieIndex, rerolled.NewFace);
                break;

            case HeroMoved moved:
                await Walk(moved.HeroId, moved.Path);
                SyncDiving(moved.HeroId);
                break;

            case MonsterMoved moved:
                await Walk(moved.MonsterId, moved.Path);
                break;

            case MonsterSurged surged:
                _trayShowsMovement = false;
                CalloutOver(surged.MonsterId, $"+{surged.Faces.Sum()} WEND!", Palette.Power);
                await _dice.ShowRoll($"{NameOf(surged.MonsterId)} surges", new DiceRoll([.. surged.Faces]), 0, newGroup: true, surged.Faces.Count, DiceKind.Wend);
                break;

            case HeroEnteredRoom entered:
                UpdateMusic();
                await ChangeRoom(entered.HeroId, entered.RoomId, entered.Position);
                if (_session.State.Rooms[entered.RoomId].IsSealed && entered.FirstVisit)
                {
                    Log("The doors slam shut behind you!", Palette.Danger);
                }

                if (entered.FirstVisit)
                {
                    await Taunt(_session.State.Rooms[entered.RoomId]);
                }

                break;

            case HeroDescended descended:
                await ChangeRoom(descended.HeroId, descended.RoomId, descended.Position);
                await _banner.Flash($"Floor {descended.Floor}", Palette.TrimBright, 0.5);
                break;

            case HeroAscended ascended:
                await ChangeRoom(ascended.HeroId, ascended.RoomId, ascended.Position);
                await _banner.Flash($"Back to floor {ascended.Floor}", Palette.TrimBright, 0.5);
                break;

            case HeroFell fell:
                await ChangeRoom(fell.HeroId, fell.RoomId, fell.Position);
                await _banner.Flash($"Fell to floor {fell.Floor}", Palette.Danger, 0.5);
                break;

            case FellIntoPit fell when fell.RoomId == _shownRoomId:
                _board.FloatText(fell.Pit, "falls!", Palette.Danger);
                await Pause(0.3);
                break;

            case ClimbedOut climbed:
                await Walk(climbed.EntityId, [climbed.Position]);
                break;

            case Shoved shoved:
                _dice.ShowOutcome(true);
                await Walk(shoved.TargetId, [shoved.To]);
                break;

            case Flipped flipped:
                _dice.ShowOutcome(true);
                CalloutOver(flipped.MonsterId, "TOPPLED!", Palette.TrimBright);
                await UpdateFigure(flipped.MonsterId, figure => figure.Prone = true, 0.25);
                break;

            case Pulled pulled:
                CalloutOver(pulled.HeroId, "PULLED!", Palette.Danger);
                await Walk(pulled.HeroId, [pulled.To]);
                break;

            case KnockedBack knocked:
                CalloutOver(knocked.HeroId, "KNOCKED BACK!", Palette.Danger);
                await Walk(knocked.HeroId, [knocked.To]);
                break;

            case Grabbed grabbed:
                CalloutOver(grabbed.HeroId, "GRABBED!", Palette.Danger);
                await Pause(0.3);
                break;

            case Hexed hexed:
                CalloutOver(hexed.HeroId, "HEXED!", Palette.Power);
                await Pause(0.3);
                break;

            case MimicWoke woke:
                // The chest is gone from the board; the Mimic stands where it was.
                _board.QueueRedraw();
                CalloutOver(woke.MonsterId, "MIMIC!", Palette.Danger);
                await Pause(0.4);
                break;

            case Engulfed engulfed:
                _board.QueueRedraw();
                FloatOver(engulfed.MonsterId, "gulp!", Palette.TextDim);
                await Pause(0.2);
                break;

            case MonsterRaised raised:
                CalloutOver(raised.MonsterId, "RISE!", Palette.Power);
                await Pause(0.3);
                break;

            case Crumbled crumbled:
                await RemoveFigure(crumbled.MonsterId);
                break;

            case MonsterRegenerated regenerated:
                FloatOver(regenerated.MonsterId, "+1", Palette.Heal);
                await UpdateFigure(regenerated.MonsterId, figure => figure.Hearts = regenerated.Hearts, 0.2);
                break;

            case MerchantFreed or MerchantMoved or MerchantFollowed or MerchantSlain or MerchantRescued or MerchantTraded or MerchantRobbed:
                await PlayMerchantEvent(gameEvent);
                break;

            case DaggerThrown or FloorBurnedOut:
                _board.QueueRedraw();
                break;

            case FloorIgnited ignited when ignited.RoomId == _shownRoomId:
                _board.QueueRedraw();
                _board.Shake(2);
                await Pause(0.25);
                break;

            case SpellCast cast when Content.FindSpell(cast.SpellId) is { } spell:
                _spellCaption = $"{NameOf(cast.HeroId)} casts {spell.Name}";
                CalloutOver(cast.HeroId, spell.Name.ToUpperInvariant() + "!", DieTypes.Tint(spell.DieType));
                RefreshPanels();
                await Pause(0.3);
                break;

            case ExtraDiceRolled extra:
                foreach (int targetId in extra.TargetIds)
                {
                    FloatOver(targetId, $"+{extra.Roll.Total}", DieTypes.Tint(extra.DieTypes.FirstOrDefault()));
                }

                break;

            case SpellLearned learned when Content.FindSpell(learned.SpellId) is { } spell:
                FloatOver(learned.HeroId, $"learns {spell.Name}", DieTypes.Tint(spell.DieType));
                break;

            case PointSpent spent:
                CalloutOver(spent.HeroId, "LEVEL UP!", Palette.Heal);
                break;

            case TorchBurnedOut burnedOut:
                CalloutOver(burnedOut.HeroId, "TORCH OUT!", Palette.Fire);
                await UpdateFigure(burnedOut.HeroId, figure => figure.Torch = _session.State.GetHero(burnedOut.HeroId).TorchesHeld > 0, 0.2);
                break;

            case HeroShackled shackled:
                CalloutOver(shackled.HeroId, "SHACKLED!", Palette.Danger);
                await UpdateFigure(shackled.HeroId, figure => figure.Chained = true, 0.3);
                break;

            case HeroUnshackled freed:
                CalloutOver(freed.HeroId, "FREE!", Palette.Heal);
                await UpdateFigure(freed.HeroId, figure => figure.Chained = false, 0.3);
                break;

            case Cleaved cleaved:
                CalloutOver(cleaved.MonsterId, "CLEAVE!", Palette.DieRumble);
                await Pause(0.2);
                break;

            case Guarded guarded:
                CalloutOver(guarded.HeroId, "GUARDED!", Palette.DieDefense);
                await Pause(0.3);
                break;

            case LastChanceUsed lucky:
                CalloutOver(lucky.HeroId, "LUCKY!", Palette.TrimBright);
                await Pause(0.3);
                break;

            case ShoveResisted resisted:
                _dice.ShowOutcome(false);
                if (_figures.TryGetValue(resisted.TargetId, out Figure? firm) && firm.RoomId == _shownRoomId)
                {
                    _board.FloatText(firm.Position, "holds firm", Palette.TextDim);
                }

                await Pause(0.25);
                break;

            case MonsterTrapped trapped:
                _board.QueueRedraw();
                _board.FloatText(trapped.Position, "trap!", Palette.Danger);
                await Pause(0.3);
                break;

            case MonsterFell fell:
                // It's gone from this room; the room below shows it once someone gets there.
                await RemoveFigure(fell.MonsterId);
                break;

            case Leaped leap:
                await _dice.ShowRoll($"{NameOf(leap.HeroId)} leaps ({leap.From.ChebyshevDistanceTo(leap.Target) * 2}+)", new DiceRoll([leap.Roll]), 0, newGroup: true);
                _dice.ShowOutcome(leap.Landing == leap.Target);
                await Walk(leap.HeroId, [leap.Landing]);
                if (leap.Landing != leap.Target && _figures.TryGetValue(leap.HeroId, out Figure? leaper) && leaper.RoomId == _shownRoomId)
                {
                    _board.FloatText(leap.Landing, "short!", Palette.Danger);
                }

                break;

            case StayedAfloat afloat when _figures.TryGetValue(afloat.HeroId, out Figure? swimmer) && swimmer.RoomId == _shownRoomId:
                _board.FloatText(swimmer.Position, "-1 Rumble: afloat", Palette.Heal);
                await Pause(0.3);
                break;

            case Drowned drowned:
                if (_figures.TryGetValue(drowned.HeroId, out Figure? drowning) && drowning.RoomId == _shownRoomId)
                {
                    _board.FloatText(drowned.From, "drowning!", Palette.Danger);
                }

                await Walk(drowned.HeroId, [drowned.To]);
                break;

            case OdHandRolled rolled:
                _trayShowsMovement = false;
                string goal = rolled.Difficulty > 0 ? $" ({rolled.Difficulty}+)" : "";
                await _dice.ShowRoll($"{NameOf(rolled.HeroId)} {OdHandCaption(rolled.Kind)}{goal}", rolled.Roll, rolled.Bonus, newGroup: true, rolled.PowerDice, DiceKind.OdHand, rolled.ToolDice);
                break;

            case Stole stole:
                // What was taken, on the dice bar.
                _dice.ShowOutcome(true, "STOLEN");
                _dice.Retitle($"{NameOf(stole.ThiefId)} steals {(stole.Key ? "a key" : stole.CardId is int taken ? $"the {CardNameOf(taken)}" : "something")}");
                _board.QueueRedraw();
                break;

            case DoorUnlocked or ChestUnlocked or TrapDisarmed or GateOpened:
                _dice.ShowOutcome(true);
                _board.QueueRedraw();
                break;

            case CheckFailed failed when _figures.TryGetValue(failed.HeroId, out Figure? fumbler) && fumbler.RoomId == _shownRoomId:
                _board.FloatText(fumbler.Position, "failed", Palette.TextDim);
                break;

            case TrapSprung sprung when _figures.TryGetValue(sprung.HeroId, out Figure? victim) && victim.RoomId == _shownRoomId:
                _board.QueueRedraw();
                _board.FloatText(sprung.Position, "trap!", Palette.Danger);
                await Pause(0.3);
                break;

            case SecretsFound found:
                _board.QueueRedraw();
                foreach (GridPosition spot in found.Traps.Concat(found.Stashes).Concat(found.Gates ?? []).Concat(found.Doors.Select(ShownRoom.DoorTile)))
                {
                    _board.FloatText(spot, "found!", Palette.TrimBright);
                }

                await Pause(0.3);
                break;

            case GoldFound found:
                FloatOver(found.HeroId, $"+{found.Amount} gold", Palette.Loot);
                _board.QueueRedraw();
                await Pause(0.2);
                break;

            case GoldAppeared appeared when appeared.RoomId == _shownRoomId:
                _board.QueueRedraw();
                _board.FloatText(appeared.Position, $"{appeared.Amount} gold", Palette.Loot);
                await Pause(0.15);
                break;

            case WallBroken or KeysAppeared or BossKeyDropped:
                _board.QueueRedraw();
                await Pause(0.15);
                break;

            case BossKeyFound found:
                await _banner.Flash($"{NameOf(found.HeroId)} has the Boss Key!", Palette.TrimBright, 0.6);
                break;

            case AttackRolled attack:
                _trayShowsMovement = false;
                BeginStrike(attack.AttackerId, attack.Total, attack.Roll.Faces.TakeLast(attack.PowerDice).Sum());
                await Lunge(attack.AttackerId, attack.TargetIds.FirstOrDefault());
                await _dice.ShowRoll(_spellCaption ?? $"{NameOf(attack.AttackerId)} {(attack.Shove ? "shoves" : attack.Chain ? "flings its chain" : "attacks")}", attack.Roll, attack.Bonus, newGroup: true, attack.PowerDice, DiceKind.Rumble, types: attack.DieTypes);
                _spellCaption = null;
                // A hero's sixes get their own events; a monster's roll straight in, so call them out here.
                if (attack.PowerDice > 0 && !IsHero(attack.AttackerId))
                {
                    CalloutOver(attack.AttackerId, "6!", Palette.Power);
                }

                break;

            case DefenseRolled defense:
                _defense = defense;
                await _dice.ShowRoll($"{NameOf(defense.DefenderId)} defends", defense.Roll, defense.Armor, newGroup: false, defense.PowerDice, DiceKind.Defense);
                if (defense.PowerDice > 0)
                {
                    CalloutOver(defense.DefenderId, "6!", Palette.Power);
                }

                break;

            case PowerDiceBanked banked when !IsHero(banked.OwnerId) && _figures.TryGetValue(banked.OwnerId, out Figure? banker) && banker.RoomId == _shownRoomId:
                _board.FloatText(banker.Position, banked.Count > 1 ? $"6! {banked.Count} Power Dice" : "6! Power Die", Palette.Power);
                break;

            case PowerDiceEarned earned when _figures.TryGetValue(earned.HeroId, out Figure? lucky) && lucky.RoomId == _shownRoomId:
                _board.FloatText(lucky.Position, "6! Power Die", Palette.Power);
                break;

            case CritRolled crit:
                CalloutOver(crit.HeroId, "6!", Palette.Power);
                await Pause(0.2);
                break;

            case LockpickSnapped snapped:
                CalloutOver(snapped.HeroId, "PICK SNAPS!", Palette.DieTool);
                RefreshPanels();
                await Pause(0.3);
                break;

            case PowerDieAdded added:
                _attackTotal += added.Face;
                _attackPower += added.Face;
                await _dice.AddPowerDie(added.Face);
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

            case Ignited ignited:
                CalloutOver(ignited.EntityId, ignited.FireDice == 1 ? "BURN!" : $"BURN {ignited.FireDice}!", Palette.Fire);
                await UpdateFigure(ignited.EntityId, figure => figure.FireDice = ignited.FireDice, 0.3);
                break;

            case FireSpread spread:
                FloatOver(spread.ToId, "catches fire!", Palette.Fire);
                await UpdateFigure(spread.ToId, figure => figure.FireDice = spread.FireDice, 0.3);
                break;

            case FireBurned burned:
                _trayShowsMovement = false;
                BeginStrike(0, burned.Roll.Total, 0);
                await _dice.ShowRoll($"{NameOf(burned.EntityId)} burns", burned.Roll, 0, newGroup: true);
                await UpdateFigure(burned.EntityId, figure => figure.FireDice = burned.FireDiceLeft, 0.1);
                break;

            case FireOut { Cause: var cause } fireOut:
                FloatOver(fireOut.EntityId, cause switch { FireOutCause.Water => "doused", FireOutCause.Rolled => "rolls it out", _ => "fire out" }, Palette.Text);
                await UpdateFigure(fireOut.EntityId, figure => figure.FireDice = 0, 0.3);
                break;

            case ConditionChanged changed:
                await ShowCondition(changed);
                break;

            case GearCorroded corroded:
                CalloutOver(corroded.HeroId, "ARMOR MELTS!", DieTypes.Tint(DieType.Acid));
                RefreshPanels();
                await Pause(0.3);
                break;

            case GearMended mended:
                FloatOver(mended.HeroId, "good as new", Palette.Heal);
                RefreshPanels();
                break;

            case Slammed slammed:
                _trayShowsMovement = false;
                BeginStrike(0, slammed.Roll.Total, 0);
                CalloutOver(slammed.EntityId, "SLAM!", DieTypes.Tint(DieType.Squall));
                await _dice.ShowRoll($"{NameOf(slammed.EntityId)} slams into {(slammed.IntoId is int into ? NameOf(into) : "the wall")}", slammed.Roll, 0, newGroup: true, types: [.. slammed.Roll.Faces.Select(_ => DieType.Squall)]);
                break;

            case DugOut dug:
                _trayShowsMovement = false;
                await _dice.ShowRoll($"{NameOf(dug.EntityId)} digs", dug.Roll, 0, newGroup: true, types: [.. dug.Roll.Faces.Select(_ => DieType.Earth)]);
                FloatOver(dug.EntityId, dug.BuriedLeft == 0 ? "digs free!" : $"{dug.BuriedLeft} to dig", DieTypes.Tint(DieType.Earth));
                await UpdateFigure(dug.EntityId, figure => SetCondition(figure, Condition.Buried, dug.BuriedLeft), 0.2);
                break;

            case PoisonRolled poison:
                FloatOver(poison.EntityId, poison.LostHeart ? $"poison bites ({poison.Face})" : poison.Face == GameRules.PowerDieFace ? "shakes off poison" : $"resists poison ({poison.Face})", DieTypes.Tint(DieType.Poison));
                await UpdateFigure(poison.EntityId, figure => SetCondition(figure, Condition.Poison, poison.PoisonLeft), 0.3);
                break;

            case AcidBurned acid:
                _trayShowsMovement = false;
                BeginStrike(0, acid.Roll.Total, 0);
                await _dice.ShowRoll($"Acid eats at {NameOf(acid.EntityId)}", acid.Roll, 0, newGroup: true, types: [.. acid.Roll.Faces.Select(_ => DieType.Acid)]);
                break;

            case FrozenSolid frozen:
                CalloutOver(frozen.EntityId, "FROZEN SOLID!", DieTypes.Tint(DieType.Ice));
                await Pause(0.4);
                break;

            case Staggered staggered:
                CalloutOver(staggered.EntityId, "CONFUSED!", DieTypes.Tint(DieType.Psycho));
                await Pause(0.3);
                break;

            case PlantsGrew grew:
                _board.QueueRedraw();
                if (grew.RoomId == _shownRoomId)
                {
                    _board.FloatText(grew.Tile, "plants grow", DieTypes.Tint(DieType.Life));
                    await Pause(0.2);
                }

                break;

            case HeroSummoned summoned:
                await SummonFigure(summoned);
                RefreshPanels();
                break;

            case WentProne prone:
                FloatOver(prone.EntityId, prone.Diving ? "dives" : "drops prone", Palette.Text);
                await UpdateFigure(prone.EntityId, figure => { figure.Prone = true; figure.Diving = prone.Diving; }, 0.2);
                break;

            case MonsterStole stole:
                FloatOver(stole.HeroId, stole.Hits > 0 ? "robbed!" : "nothing taken", stole.Hits > 0 ? Palette.Danger : Palette.TextDim);
                await UpdateFigure(stole.MonsterId, figure => figure.CarriesLoot |= stole.Gold > 0 || stole.CardId is not null, 0.3);
                break;

            case Mugged mugged:
                FloatOver(mugged.HeroId, $"-{mugged.Gold} gold", Palette.Loot);
                await UpdateFigure(mugged.MonsterId, figure => figure.CarriesLoot = true, 0.15);
                break;

            case BombPlaced placed when placed.RoomId == _shownRoomId:
                _board.QueueRedraw();
                _board.FloatText(placed.Position, placed.Lit ? "lit!" : "bomb", placed.Lit ? Palette.Fire : Palette.Text);
                await Pause(0.25);
                break;

            case FuseBurned fuse when fuse.RoomId == _shownRoomId && ShownRoom.FindBomb(fuse.BombId) is { } burning:
                _board.FloatText(burning.Position, fuse.FuseTurns == 1 ? "1 turn!" : $"{fuse.FuseTurns} turns", Palette.Fire);
                break;

            case BombExploded boom:
                if (boom.RoomId == _shownRoomId)
                {
                    _board.LookAt(boom.Position);
                    _board.Explosion(boom.Position);
                    _board.QueueRedraw();
                }

                await Pause(0.45);
                break;

            case BlastRolled blast:
                _trayShowsMovement = false;
                BeginStrike(0, blast.Roll.Total, 0);
                await _dice.ShowRoll("The blast", blast.Roll, 0, newGroup: true, blast.PowerDice);
                break;

            case BombStruck { Exploded: false } struck:
                FloatOver(struck.HeroId, $"{struck.Chance}%: nothing", Palette.TextDim);
                await Pause(0.2);
                break;

            case BombMoved or BombDefused or BombFell:
                _board.QueueRedraw();
                if (gameEvent is BombDefused { Doused: var doused } defused && defused.RoomId == _shownRoomId && ShownRoom.FindBomb(defused.BombId) is { } quiet)
                {
                    _board.FloatText(quiet.Position, doused ? "fizzles out" : "defused", Palette.Text);
                }

                await Pause(0.2);
                break;

            case Stunned stunned:
                CalloutOver(stunned.EntityId, "STUN!", Palette.TrimBright);
                await UpdateFigure(stunned.EntityId, figure => figure.StunMarks = stunned.Stacks + 3 * stunned.PowerfulStacks, 0.25);
                break;

            case StunWoreOff wore:
                await UpdateFigure(wore.EntityId, figure => figure.StunMarks = 0, 0.1);
                break;

            case GotUp up:
                await UpdateFigure(up.EntityId, figure => { figure.Prone = false; figure.Diving = false; }, 0.2);
                break;

            case HeroKeeledOver keeled:
                await Walk(keeled.HeroId, [keeled.Position]);
                CalloutOver(keeled.HeroId, "DOWN!", Palette.Danger, size: 3);
                _board.Shake(4);
                await UpdateFigure(keeled.HeroId, figure => { figure.Keeling = true; figure.Hearts = 0; figure.LifeMarks = figure.DeathMarks = 0; }, 0.4);
                break;

            case KeelRolled keel:
                await ShowKeelRoll(keel);
                break;

            case HeroRoused roused:
                _dice.ShowOutcome(roused.LifeGained > 0);
                FloatOver(roused.HeroId, roused.LifeGained > 0 ? $"+{roused.LifeGained} life" : "stirs...", roused.LifeGained > 0 ? Palette.Heal : Palette.TextDim);
                await UpdateFigure(roused.HeroId, figure => figure.LifeMarks = roused.LifeMarks, 0.3);
                break;

            case KeelerStruck struck:
                FloatOver(struck.HeroId, "mark of death", Palette.Danger);
                await UpdateFigure(struck.HeroId, figure => figure.DeathMarks = struck.DeathMarks, 0.3);
                break;

            case HeroStoodUp stood:
                FloatOver(stood.HeroId, "stands up!", Palette.Heal);
                await UpdateFigure(stood.HeroId, figure => { figure.Keeling = false; figure.Hearts = stood.Hearts; }, 0.4);
                break;

            case HeroDied died:
                FloatOver(died.HeroId, "R.I.P.", Palette.Text);
                await RemoveFigure(died.HeroId);
                _board.QueueRedraw();
                await _banner.Flash($"{died.Tombstone.Inscription}", Palette.TextDim, 0.8);
                break;

            case HeroEscaped escaped:
                await RemoveFigure(escaped.HeroId);
                await _banner.Flash($"{NameOf(escaped.HeroId)} escapes!", Palette.TrimBright, 0.6);
                break;

            case HeroRetreated retreated:
                _dice.ShowOutcome(true, "AWAY");
                await RemoveFigure(retreated.HeroId);
                await _banner.Flash($"{NameOf(retreated.HeroId)} gets away!", Palette.TrimBright, 0.6);
                break;

            case Routed routed:
                CalloutOver(routed.HeroId, "ROUTED!", Palette.Danger);
                await _dice.ShowRoll($"{NameOf(routed.HeroId)} bolts", routed.Roll, routed.Steps - routed.Roll.Total, newGroup: true, kind: DiceKind.Wend);
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

            case MonsterFrozen stunned:
                await UpdateFigure(stunned.MonsterId, figure => figure.Frozen = true, 0.2);
                break;

            case MonsterTurnSkipped skipped:
                await UpdateFigure(skipped.MonsterId, figure => figure.Frozen = false, 0.3);
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
                Music.Stop();
                string title = ended.WinnerHeroIds.Count == 0
                    ? "The dungeon claims you"
                    : $"{string.Join(" & ", ended.WinnerHeroIds.Select(NameOf))} {(ended.WinnerHeroIds.Count == 1 ? "wins" : "win")}!";
                await _banner.Flash(title, Palette.TrimBright, 1.2);
                break;
        }
    }

    private static void PlaySound(GameEvent gameEvent)
    {
        switch (gameEvent)
        {
            case TurnStarted:
                Sfx.Play("turn");
                break;
            case EnemyPhaseStarted:
                Sfx.Play("monsters");
                break;
            case MovementRolled or AttackRolled or DefenseRolled or DieRerolled or PowerDieAdded:
                Sfx.PlayVaried("dice");
                break;
            case PowerDiceEarned:
                Sfx.Play("coin", 1.3f);
                break;
            case Damaged { Amount: > 0 }:
                Sfx.Play("hit");
                break;
            case MonsterFrozen:
                Sfx.Play("block", 0.6f);
                break;
            case MonsterDefeated:
                Sfx.Play("defeat");
                break;
            case HeroHealed or RoomCleared:
                Sfx.Play("heal");
                break;
            case ChestOpened:
                Sfx.Play("treasure");
                break;
            case LootDropped or LootPickedUp:
                Sfx.Play("coin");
                break;
            case PotSmashed smashed:
                Sfx.Play("pot");
                Sfx.Play(smashed.CardId is not null ? "coin" : smashed.HeartsRestored > 0 ? "heal" : "click");
                break;
            case HeroEnteredRoom or DoorUnlocked or GateOpened:
                Sfx.Play("door");
                break;
            case OdHandRolled:
                Sfx.PlayVaried("dice");
                break;
            case ChestUnlocked or TrapDisarmed:
                Sfx.Play("click");
                break;
            case CheckFailed:
                Sfx.Play("block", 0.8f);
                break;
            case SecretsFound { Doors.Count: > 0 } or SecretsFound { Traps.Count: > 0 } or SecretsFound { Stashes.Count: > 0 } or SecretsFound { Gates.Count: > 0 }:
                Sfx.Play("heal");
                break;
            case TrapSprung:
                Sfx.Play("hit");
                break;
            case HeroFell:
                Sfx.Play("stairs", 0.7f);
                break;
            case FellIntoPit:
                Sfx.Play("stairs", 0.6f);
                break;
            case Shoved or Leaped:
                Sfx.Play("block", 1.3f);
                break;
            case Pulled:
                Sfx.Play("block", 0.9f);
                break;
            case KnockedBack:
                Sfx.Play("block", 0.7f);
                break;
            case Grabbed or MimicWoke:
                Sfx.Play("hit", 0.7f);
                break;
            case Hexed or MonsterRaised:
                Sfx.Play("block", 1.6f);
                break;
            case Crumbled:
                Sfx.Play("pot", 0.8f);
                break;
            case WeaponReloaded:
                Sfx.Play("click");
                break;
            case ShoveResisted:
                Sfx.Play("block", 0.8f);
                break;
            case MonsterTrapped:
                Sfx.Play("hit");
                break;
            case WallBroken:
                Sfx.Play("pot", 0.6f);
                break;
            case KeysFound or Stole or GoldFound or GoldAppeared:
                Sfx.Play("coin");
                break;
            case BossKeyFound:
                Sfx.Play("treasure");
                break;
            case HeroDescended or HeroAscended:
                Sfx.Play("stairs");
                break;
            case HeroKeeledOver:
                Sfx.Play("downed");
                break;
            case Ignited or FireSpread:
                Sfx.Play("hit", 1.4f);
                break;
            case FireBurned:
                Sfx.PlayVaried("dice");
                break;
            case FireOut:
                Sfx.Play("block", 0.7f);
                break;
            case Stunned:
                Sfx.Play("block", 1.5f);
                break;
            case BombExploded:
                Sfx.Play("boom");
                break;
            case MonsterStole { Hits: > 0 } or Mugged:
                Sfx.Play("coin", 0.7f);
                break;
            case BombPlaced { Lit: true } or FuseBurned:
                Sfx.Play("fuse");
                break;
            case BlastRolled:
                Sfx.PlayVaried("dice");
                break;
            case BombDefused or BombMoved:
                Sfx.Play("click");
                break;
            case WentProne or GotUp:
                Sfx.PlayVaried("step");
                break;
            case KeelRolled:
                Sfx.PlayVaried("dice");
                break;
            case HeroRoused { LifeGained: > 0 } or HeroStoodUp:
                Sfx.Play("heal");
                break;
            case KeelerStruck:
                Sfx.Play("hit");
                break;
            case HeroDied:
                Sfx.Play("downed", 0.7f);
                break;
            case HeroEscaped or RunEnded { WinnerHeroIds.Count: > 0 }:
                Sfx.Play("victory");
                break;
            case CardEquipped or CardBelted or CardStowed or CardDropped:
                Sfx.Play("click");
                break;
        }
    }

    /// <summary>
    /// Boss music while a hero still in the dungeon shares a room with a living boss, the dungeon
    /// theme otherwise, and nothing once the run is over (the results screen plays its own tune).
    /// Checked after each batch of events, so the boss music ends once its defeat has played out.
    /// </summary>
    private void UpdateMusic()
    {
        GameState state = _session.State;
        if (state.Phase == RunPhase.Ended)
        {
            Music.Stop();
            return;
        }

        List<Monster> facing = state.Heroes
            .Where(hero => hero.Status == HeroStatus.Active)
            .SelectMany(hero => state.RoomOf(hero).Monsters)
            .ToList();
        bool bossFight = facing.Any(monster => monster.Definition.Boss);
        string? ownSong = facing.Select(monster => monster.Definition.Music).FirstOrDefault(song => song is not null);
        Music.Play(bossFight ? "boss" : ownSong ?? DungeonSong);
    }

    /// <summary>Monsters with something to say bellow it when heroes first walk in (the Ogre Champ laughs).</summary>
    private async Task Taunt(Room room)
    {
        foreach (Monster monster in room.Monsters.Where(monster => monster.Definition.Taunt is not null))
        {
            Log($"The {monster.Definition.Name} roars with laughter: \"{monster.Definition.Taunt}\"", Palette.Danger);
            CalloutOver(monster.Id, monster.Definition.Taunt!, Palette.Danger);
            Sfx.Play("laugh");
            await Pause(0.9);
        }
    }

    /// <summary>Each dungeon's own exploring song.</summary>
    private string DungeonSong => _session.State.ThemeId == "sunken_temple" ? "temple" : "dungeon";

    /// <summary>Who an event is about, so the camera can turn to them in a room bigger than the view.</summary>
    private static int? FocusOf(GameEvent gameEvent) => gameEvent switch
    {
        TurnStarted turn => turn.HeroId,
        HeroMoved moved => moved.HeroId,
        MonsterMoved moved => moved.MonsterId,
        AttackRolled attack => attack.AttackerId,
        Damaged damaged => damaged.TargetId,
        OdHandRolled rolled => rolled.HeroId,
        Shoved shoved => shoved.TargetId,
        Pulled pulled => pulled.HeroId,
        KnockedBack knocked => knocked.HeroId,
        Flipped flipped => flipped.MonsterId,
        Leaped leap => leap.HeroId,
        HeroKeeledOver keeled => keeled.HeroId,
        KeelRolled keel => keel.HeroId,
        FireBurned burned => burned.EntityId,
        MonsterSpawned spawned => spawned.MonsterId,
        _ => null,
    };

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
        HeroKeeledOver keeled => FigureRoom(keeled.HeroId),
        Ignited ignited => FigureRoom(ignited.EntityId),
        FireSpread spread => FigureRoom(spread.ToId),
        FireBurned burned => FigureRoom(burned.EntityId),
        FireOut fireOut => FigureRoom(fireOut.EntityId),
        WentProne prone => FigureRoom(prone.EntityId),
        GotUp up => FigureRoom(up.EntityId),
        Stunned stunned => FigureRoom(stunned.EntityId),
        BombPlaced placed => placed.RoomId,
        MonsterStole stole => FigureRoom(stole.MonsterId),
        Mugged mugged => FigureRoom(mugged.MonsterId),
        FuseBurned fuse => fuse.RoomId,
        BombExploded boom => boom.RoomId,
        BlastRolled blast => blast.RoomId,
        BlastHit hit => FigureRoom(hit.EntityId),
        BombStruck struck => FigureRoom(struck.HeroId),
        BombMoved moved => moved.RoomId,
        BombDefused defused => defused.RoomId,
        BombFell fell => fell.RoomId,
        StunWoreOff wore => FigureRoom(wore.EntityId),
        KeelRolled keel => FigureRoom(keel.HeroId),
        HeroRoused roused => FigureRoom(roused.HeroId),
        KeelerStruck struck => FigureRoom(struck.HeroId),
        HeroStoodUp stood => FigureRoom(stood.HeroId),
        HeroDied died => FigureRoom(died.HeroId),
        MonsterDefeated defeated => FigureRoom(defeated.MonsterId),
        MonsterCollapsed collapsed => FigureRoom(collapsed.MonsterId),
        MonsterReassembled risen => FigureRoom(risen.MonsterId),
        MonsterFrozen stunned => FigureRoom(stunned.MonsterId),
        MonsterTurnSkipped skipped => FigureRoom(skipped.MonsterId),
        MonsterSpawned spawned => spawned.RoomId,
        RoomCleared cleared => cleared.RoomId,
        PotSmashed smashed => FigureRoom(smashed.HeroId),
        OdHandRolled rolled => FigureRoom(rolled.HeroId),
        TrapSprung sprung => FigureRoom(sprung.HeroId),
        FellIntoPit fell => fell.RoomId,
        ClimbedOut climbed => FigureRoom(climbed.EntityId),
        Shoved shoved => FigureRoom(shoved.TargetId),
        Pulled pulled => FigureRoom(pulled.HeroId),
        KnockedBack knocked => FigureRoom(knocked.HeroId),
        Grabbed grabbed => FigureRoom(grabbed.HeroId),
        Hexed hexed => FigureRoom(hexed.HeroId),
        MimicWoke woke => woke.RoomId,
        Engulfed engulfed => FigureRoom(engulfed.MonsterId),
        MonsterRaised raised => FigureRoom(raised.MonsterId),
        Crumbled crumbled => FigureRoom(crumbled.MonsterId),
        MonsterRegenerated regenerated => FigureRoom(regenerated.MonsterId),
        LastChanceUsed lucky => FigureRoom(lucky.HeroId),
        Guarded guarded => FigureRoom(guarded.HeroId),
        DaggerThrown thrown => thrown.RoomId,
        MerchantFreed freed => FigureRoom(freed.MerchantId),
        MerchantMoved moved => FigureRoom(moved.MerchantId),
        MerchantSlain slain => FigureRoom(slain.MerchantId),
        MerchantTraded traded => FigureRoom(traded.HeroId),
        Cleaved cleaved => FigureRoom(cleaved.MonsterId),
        Flipped flipped => FigureRoom(flipped.MonsterId),
        ShoveResisted resisted => FigureRoom(resisted.TargetId),
        MonsterTrapped trapped => FigureRoom(trapped.MonsterId),
        MonsterFell fell => fell.FromRoomId,
        Leaped leap => FigureRoom(leap.HeroId),
        StayedAfloat afloat => FigureRoom(afloat.HeroId),
        Drowned drowned => FigureRoom(drowned.HeroId),
        SecretsFound found => found.RoomId,
        WallBroken broken => broken.RoomId,
        KeysAppeared keys => keys.RoomId,
        LootDropped dropped => dropped.RoomId,
        GoldFound found => FigureRoom(found.HeroId),
        GoldAppeared appeared => appeared.RoomId,
        _ => null,
    };

    private static string OdHandCaption(OdHandCheckKind kind) => kind switch
    {
        OdHandCheckKind.PickDoor or OdHandCheckKind.PickChest or OdHandCheckKind.PickGate or OdHandCheckKind.PickChains or OdHandCheckKind.PickShackles => "picks the lock",
        OdHandCheckKind.Search => "searches",
        OdHandCheckKind.Disarm => "disarms",
        OdHandCheckKind.Rouse => "rouses an ally",
        OdHandCheckKind.DefuseBomb => "defuses",
        OdHandCheckKind.Retreat => "looks for a way out",
        _ => "steals",
    };

    private async Task Pause(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
