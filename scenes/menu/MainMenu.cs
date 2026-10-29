using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

public partial class MainMenu : MenuScreen
{
    public event Action? PlayRequested;

    public event Action? CharactersRequested;

    public event Action? SettingsRequested;

    /// <summary>The Music Tracker, offered only when the game runs from the Godot editor.</summary>
    public event Action? TrackerRequested;

    internal Button PlayButton { get; private set; } = null!;

    internal Button CharactersButton { get; private set; } = null!;

    internal Button SettingsButton { get; private set; } = null!;

    public override void _Ready()
    {
        BuildFrame("");

        Label title = Add(UiTheme.Heading(UiTheme.Label(new Vector2(0, 48), new Vector2(Width, 64), "WOAD", 52, Palette.TrimBright), 6));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        Label subtitle = Add(UiTheme.Label(new Vector2(0, 118), new Vector2(Width, 16), "A dungeon crawl for one to eight heroes", 10, Palette.TextDim));
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;

        PlayButton = Add(UiTheme.Button(new Vector2(240, 164), new Vector2(160, 26), "Play"));
        PlayButton.Pressed += () => PlayRequested?.Invoke();
        CharactersButton = Add(UiTheme.Button(new Vector2(240, 198), new Vector2(160, 26), "Characters"));
        CharactersButton.Pressed += () => CharactersRequested?.Invoke();
        SettingsButton = Add(UiTheme.Button(new Vector2(240, 232), new Vector2(160, 26), "Settings"));
        SettingsButton.Pressed += () => SettingsRequested?.Invoke();
        Button quit = Add(UiTheme.Button(new Vector2(240, 266), new Vector2(160, 26), "Quit"));
        quit.Pressed += () => Sfx.QuitGame(this);
        if (OS.HasFeature("editor"))
        {
            Button tracker = Add(UiTheme.CompactButton(new Vector2(260, 300), new Vector2(120, 18), "Music Tracker"));
            tracker.TooltipText = "Write chiptune songs for the game (only when running from the Godot editor)";
            tracker.Pressed += () => TrackerRequested?.Invoke();
        }

        string version = ProjectSettings.GetSetting("application/config/version").AsString();
        Label footer = Add(UiTheme.Label(new Vector2(0, 326), new Vector2(Width, 12), $"Woad {version}   Local hot-seat for 1-8 players", color: Palette.TextDim));
        footer.HorizontalAlignment = HorizontalAlignment.Center;
    }
}
