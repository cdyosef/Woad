using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Saves;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A recorded session to watch: the record, the move to start from, and how (playing at once, the screen clean of
/// panels, quitting at the end, for recording a video).
/// </summary>
public sealed record ReplayRun(SessionRecord Record, IReadOnlyList<GameCommand> Commands, int StartMove, bool AutoPlay = false, bool Clean = false, bool QuitAtEnd = false, float Speed = 1);

/// <summary>
/// Watching a recorded session: the run plays again move by move through the rules, with every animation, and what
/// was said shows when it was said. A bar over the board: back to the start, play or pause, one move on, the speed,
/// a round back or on, Clean (the panels away, for clean shots: H), Shot (a screenshot: F12), and Exit. The camera
/// moves as ever (right-drag to pan, the wheel to zoom), and clicking a hero in the turn order follows their sheet.
/// </summary>
public partial class GameScreen
{
    public ReplayRun? Replay { get; init; }

    /// <summary>Jump to another move of the replay (the screen is built afresh from there).</summary>
    public event Action<int, float>? ReplayJumpRequested;

    private int _replayIndex;
    private int _replayNotes;
    private bool _replayPlaying;
    private bool _replayStepping;
    private bool _replayPlayingMove;
    private float _replaySpeed = 1;
    private bool _clean;
    private Panel? _replayBar;
    private Label _replayInfo = null!;
    private Button _replayPlay = null!;
    private Button _replaySpeedButton = null!;
    private IReadOnlyDictionary<int, int> _roundStarts = new Dictionary<int, int>();

    private static readonly float[] Speeds = [0.5f, 1, 2, 4];

    /// <summary>How far the replay has got: the moves played.</summary>
    internal int ReplayMove => _replayIndex;

    internal bool ReplayPlaying => _replayPlaying;

    internal bool IsClean => _clean;

    private void ReplayReady()
    {
        if (Replay is not { } replay)
        {
            return;
        }

        _replayIndex = replay.StartMove;
        _replayNotes = replay.Record.Notes.Count(note => note.AfterMove < replay.StartMove);
        _replaySpeed = replay.Speed;
        Engine.TimeScale = _replaySpeed;
        _replayPlaying = replay.AutoPlay;
        _roundStarts = replay.Record.RoundStarts(Content);

        // At the top of the right-hand panel (Party and Menu have nothing to do in a replay).
        const float barWidth = 172;
        _replayBar = new Panel { Position = new Vector2(SideWidth + BoardView.Pixels + 2, 2), Size = new Vector2(barWidth, 56), ZIndex = 5 };
        _replayBar.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Ink with { A = 0.85f }, Palette.Trim));
        AddChild(_replayBar);
        float x = 3, y = 2;
        Button Bar(string text, float width, string tip, Action pressed)
        {
            if (x + width > barWidth - 2)
            {
                x = 3;
                y += 18;
            }

            Button button = UiTheme.CompactButton(new Vector2(x, y), new Vector2(width, 16), text);
            button.Name = $"Replay{text.Replace(" ", "")}";
            button.AddThemeFontSizeOverride("font_size", UiTheme.FontSize - 2);
            button.TooltipText = tip;
            button.FocusMode = FocusModeEnum.None;
            button.Pressed += pressed;
            _replayBar.AddChild(button);
            x += width + 2;
            return button;
        }

        Bar("|<", 18, "Back to the start.", () => ReplayJumpRequested?.Invoke(0, _replaySpeed));
        Bar("<R", 20, "Back to the start of the round before.", () => JumpRound(-1));
        _replayPlay = Bar("Play", 34, "Play or pause (Space).", ToggleReplay);
        Bar("Step", 30, "One move on.", () => _replayStepping = true);
        Bar("R>", 20, "On to the start of the next round.", () => JumpRound(1));
        _replaySpeedButton = Bar("1x", 24, "Faster or slower.", CycleSpeed);
        Bar("Clean", 34, "Hide the panels, for clean shots (H).", ToggleClean);
        Bar("Shot", 30, "Save a screenshot (F12).", () => TakeScreenshot());
        Bar("Exit", 28, "Stop watching.", () => ExitRequested?.Invoke());
        _replayInfo = UiTheme.Label(new Vector2(4, y + 19), new Vector2(barWidth - 8, 14), "", color: Palette.TrimBright);
        _replayInfo.ClipText = true;
        _replayInfo.MouseFilter = MouseFilterEnum.Ignore;
        _replayBar.AddChild(_replayInfo);
        if (replay.Clean)
        {
            ToggleClean();
        }

        ShowReplayInfo();
    }

    private void ShowReplayInfo()
    {
        if (Replay is not { } replay || _replayBar is null)
        {
            return;
        }

        _replayPlay.Text = _replayPlaying ? "Pause" : "Play";
        _replaySpeedButton.Text = _replaySpeed < 1 ? "1/2x" : $"{_replaySpeed:0}x";
        string who = _replayIndex < replay.Record.Moves.Count ? $"  {replay.Record.Moves[_replayIndex].Player} to move" : "  the end";
        _replayInfo.Text = $"Move {_replayIndex}/{replay.Commands.Count}  round {_session.State.Round}{who}";
    }

    private void ToggleReplay()
    {
        _replayPlaying = !_replayPlaying;
        ShowReplayInfo();
    }

    private void CycleSpeed()
    {
        _replaySpeed = Speeds[(Array.IndexOf(Speeds, _replaySpeed) + 1) % Speeds.Length];
        Engine.TimeScale = _replaySpeed;
        ShowReplayInfo();
    }

    private void JumpRound(int by)
    {
        int round = _session.State.Round + by;
        int at = _roundStarts.Where(entry => by < 0 ? entry.Key <= Math.Max(1, round) : entry.Key >= round)
            .OrderBy(entry => by < 0 ? -entry.Key : entry.Key).Select(entry => (int?)entry.Value).FirstOrDefault() ?? (by < 0 ? 0 : _replayIndex);
        ReplayJumpRequested?.Invoke(at, _replaySpeed);
    }

    /// <summary>The panels away (everything but the board and the replay bar), or back.</summary>
    private void ToggleClean()
    {
        _clean = !_clean;
        foreach (Node child in GetChildren())
        {
            if (child is CanvasItem item && child != _board.GetParent() && child is not ColorRect)
            {
                item.Visible = !_clean;
            }
        }

        if (_replayBar is not null)
        {
            // Clean hides the bar too: H (or Space, the arrow) still work, and H brings it all back.
            _replayBar.Visible = !_clean;
        }

        RefreshPanels();
    }

    /// <summary>Saves what's on screen as a picture, in the screenshots folder. Returns where.</summary>
    internal string TakeScreenshot()
    {
        string folder = ProjectSettings.GlobalizePath("user://screenshots");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"woad {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        // Nothing is drawn with no window (the click tests run headless).
        if (GetViewport().GetTexture()?.GetImage() is not { } image)
        {
            return "";
        }

        image.SavePng(path);
        Log($"Screenshot saved: {path}", Palette.TextDim);
        return path;
    }

    /// <summary>The replay's hotkeys: Space plays or pauses, H cleans the screen; F12 takes a screenshot anywhere.</summary>
    private bool ReplayKey(InputEventKey key)
    {
        if (key.Keycode == Key.F12)
        {
            TakeScreenshot();
            return true;
        }

        if (Replay is null)
        {
            return false;
        }

        switch (key.Keycode)
        {
            case Key.Space:
                ToggleReplay();
                return true;
            case Key.H:
                ToggleClean();
                return true;
            case Key.Right:
                _replayStepping = true;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Plays the replay's next move (and shows what was said before it), when it's time.</summary>
    private async void ReplayNext()
    {
        if (Replay is not { } replay || _replayPlayingMove || _busy || (!_replayPlaying && !_replayStepping))
        {
            return;
        }

        _replayStepping = false;
        _replayPlayingMove = true;
        try
        {
            // What was said (and who came and went) before this move.
            while (_replayNotes < replay.Record.Notes.Count && replay.Record.Notes[_replayNotes].AfterMove <= _replayIndex)
            {
                SessionNote note = replay.Record.Notes[_replayNotes++];
                switch (note.Kind)
                {
                    case SessionNoteKind.Chat when note.HeroId is int heroId:
                        OnChatArrived(heroId, note.Text, note.Language);
                        await Pause(0.6);
                        break;
                    case SessionNoteKind.Left:
                        Log($"{note.Name} left the table.", Palette.TextDim);
                        break;
                    case SessionNoteKind.Rejoined:
                        Log($"{note.Name} is back at the table.", Palette.TrimBright);
                        break;
                    case SessionNoteKind.Watching:
                        Log($"{note.Name} came to watch.", Palette.TextDim);
                        break;
                }
            }

            if (_replayIndex >= replay.Commands.Count)
            {
                _replayPlaying = false;
                Log("The end of the session.", Palette.TrimBright);
                ShowReplayInfo();
                if (replay.QuitAtEnd)
                {
                    await Pause(1.5);
                    Sfx.QuitGame(this);
                }

                return;
            }

            GameCommand command = replay.Commands[_replayIndex];
            _replayApplying = true;
            bool played = await ExecuteAsync(command);
            _replayApplying = false;
            if (!played)
            {
                _replayPlaying = false;
                Log($"The replay doesn't fit this version of the game: move {_replayIndex + 1} was refused ({_lastRefusal}).", Palette.Danger);
                return;
            }

            _replayIndex++;
            ShowReplayInfo();
        }
        finally
        {
            _replayApplying = false;
            _replayPlayingMove = false;
        }
    }

    /// <summary>A replay's own move being played (the rules, not a player, decide it).</summary>
    private bool _replayApplying;
}
