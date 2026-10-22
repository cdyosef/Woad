using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>
/// The Dungeon Run list: continue the saved run, pick a dungeon to start a new one, or open the
/// Woarld Map to choose among the dungeons unlocked there.
/// </summary>
public partial class DungeonSelectScreen : MenuScreen
{
    private readonly List<Button> _dungeonButtons = [];

    /// <summary>The dungeons to offer (id, name and a line about each), in order.</summary>
    public IReadOnlyList<(string Id, string Name, string Description)> Dungeons { get; init; } = [];

    /// <summary>A line about the saved run (dungeon, party, how far), or null if there's none to continue.</summary>
    public string? SavedRun { get; init; }

    /// <summary>Why there's no run to continue, when a save couldn't be read.</summary>
    public string? SaveProblem { get; init; }

    public event Action? BackRequested;

    public event Action? ContinueRequested;

    public event Action? WoarldMapRequested;

    /// <summary>A dungeon was picked: its theme id.</summary>
    public event Action<string>? DungeonChosen;

    internal Button ContinueButton { get; private set; } = null!;

    internal Button WoarldMapButton { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

    internal Button DungeonButton(int index) => _dungeonButtons[index];

    public override void _Ready()
    {
        BuildFrame("DUNGEON RUN");

        ContinueButton = Add(UiTheme.Button(new Vector2(40, 46), new Vector2(200, 26), "Continue current run"));
        ContinueButton.Disabled = SavedRun is null;
        ContinueButton.Pressed += () => ContinueRequested?.Invoke();
        string line = SavedRun ?? SaveProblem ?? "No run saved. A run saves as you play, until it ends.";
        ContinueButton.TooltipText = line;
        Color color = SavedRun is not null ? Palette.Text : SaveProblem is not null ? Palette.Danger : Palette.TextDim;
        Add(UiTheme.Label(new Vector2(254, 42), new Vector2(350, 34), line, color: color)).VerticalAlignment = VerticalAlignment.Center;

        Caption(new Vector2(40, 86), "DUNGEONS");
        for (int i = 0; i < Dungeons.Count; i++)
        {
            (string id, string name, string description) = Dungeons[i];
            float y = 100 + i * 40;
            Button button = Add(UiTheme.Button(new Vector2(40, y), new Vector2(200, 30), name));
            button.Pressed += () => DungeonChosen?.Invoke(id);
            _dungeonButtons.Add(button);
            Add(UiTheme.Label(new Vector2(254, y), new Vector2(350, 30), description)).VerticalAlignment = VerticalAlignment.Center;
        }

        WoarldMapButton = Add(UiTheme.Button(new Vector2(40, 270), new Vector2(200, 26), "Open Woarld Map"));
        WoarldMapButton.Pressed += () => WoarldMapRequested?.Invoke();
        WoarldMapButton.TooltipText = "Walk the Woarld with your party and raid the dungeons you've opened.";
        Add(UiTheme.Label(new Vector2(254, 270), new Vector2(350, 26), "Walk the Woarld with your party and raid any dungeon you've opened.")).VerticalAlignment = VerticalAlignment.Center;

        BackButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();
    }
}
