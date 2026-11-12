using Godot;
using Woad.Core.Saves;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Net;

/// <summary>
/// The recorded sessions, newest first: when, the dungeon, who played, how many moves. Watch one, record it to a video
/// (it plays itself in a window and is written to the videos folder, frame by frame), or open the folder they're kept
/// in. One made by another version of the game is marked: it may no longer play back.
/// </summary>
public partial class ReplayListScreen : MenuScreen
{
    /// <summary>Where the replays are.</summary>
    public string Folder { get; init; } = "";

    public event Action? BackRequested;

    /// <summary>Watch a replay (its file).</summary>
    public event Action<string>? WatchRequested;

    /// <summary>Record a replay to a video (its file).</summary>
    public event Action<string>? VideoRequested;

    private readonly List<(string Path, SessionRecord? Record, string Problem)> _replays = [];
    private int _selected = -1;
    private Button _watch = null!;
    private Button _video = null!;
    private Label _note = null!;

    internal int Count => _replays.Count;

    internal Button WatchButton => _watch;

    internal void Select(int index)
    {
        _selected = index;
        Refresh();
    }

    private VBoxContainer _list = null!;

    public override void _Ready()
    {
        BuildFrame("REPLAYS");
        var scroll = Add(new ScrollContainer { Position = new Vector2(20, 44), Size = new Vector2(600, 240), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        _list = new VBoxContainer { CustomMinimumSize = new Vector2(586, 0) };
        scroll.AddChild(_list);
        _note = Add(UiTheme.Label(new Vector2(20, 290), new Vector2(600, 24), "", color: Palette.TextDim));
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        Button back = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        back.Pressed += () => BackRequested?.Invoke();
        Button folder = Add(UiTheme.Button(new Vector2(116, 316), new Vector2(110, 26), "Open folder"));
        folder.TooltipText = "Open the folder the replays are kept in (copy one there from a friend to watch theirs).";
        folder.Pressed += () =>
        {
            Directory.CreateDirectory(Folder);
            OS.ShellOpen(Folder);
        };
        _video = Add(UiTheme.Button(new Vector2(396, 316), new Vector2(112, 26), "Record video"));
        _video.TooltipText = "Plays the replay in a window, clean of panels, and writes it to a video in the videos folder.";
        _video.Pressed += () => VideoRequested?.Invoke(_replays[_selected].Path);
        _watch = Add(UiTheme.Button(new Vector2(516, 316), new Vector2(102, 26), "Watch"));
        _watch.Pressed += () => WatchRequested?.Invoke(_replays[_selected].Path);

        if (Directory.Exists(Folder))
        {
            foreach (string path in Directory.GetFiles(Folder, "*.woad-replay.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    SessionRecord record = SessionRecord.FromJson(File.ReadAllText(path));
                    _replays.Add((path, record, record.GameVersion == NetLink.GameVersion ? "" : $"made with {record.GameVersion}: may not play back on this version"));
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException)
                {
                    _replays.Add((path, null, exception.Message));
                }
            }
        }

        _selected = _replays.FindIndex(replay => replay.Record is not null);
        Refresh();
    }

    private void Refresh()
    {
        foreach (Node old in _list.GetChildren())
        {
            _list.RemoveChild(old);
            old.QueueFree();
        }

        if (_replays.Count == 0)
        {
            _list.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(586, 28), "No replays yet. Every run played online is recorded by whoever hosts it.", color: Palette.TextDim));
        }

        for (int i = 0; i < _replays.Count; i++)
        {
            (string path, SessionRecord? record, string problem) = _replays[i];
            string name = Path.GetFileName(path).Replace(".woad-replay.json", "");
            string what = record is null ? problem : $"{string.Join(", ", record.Players)}   {record.Moves.Count} moves{(problem.Length > 0 ? "   (other version)" : "")}";
            Button row = UiTheme.CompactButton(Vector2.Zero, new Vector2(586, 22), $"{name}   {what}");
            row.CustomMinimumSize = new Vector2(586, 22);
            row.Name = $"Replay{i}";
            row.ToggleMode = true;
            row.SetPressedNoSignal(i == _selected);
            row.Disabled = record is null;
            int index = i;
            row.Pressed += () => Select(index);
            _list.AddChild(row);
        }

        bool ok = _selected >= 0 && _replays[_selected].Record is not null;
        _watch.Disabled = _video.Disabled = !ok;
        _note.Text = ok && _replays[_selected].Problem.Length > 0 ? $"This one was {_replays[_selected].Problem}."
            : "Watching: Space plays or pauses, the arrow steps a move, H hides the panels, F12 takes a screenshot; right-drag and the wheel move the camera.";
    }
}
