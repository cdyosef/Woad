using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

public partial class MainMenu : MenuScreen
{
    public event Action? PlayRequested;

    public event Action? CharactersRequested;

    internal Button PlayButton { get; private set; } = null!;

    internal Button CharactersButton { get; private set; } = null!;

    public override void _Ready()
    {
        BuildFrame("");

        Label title = Add(UiTheme.Heading(UiTheme.Label(new Vector2(0, 48), new Vector2(Width, 64), "WOAD", 52, Palette.TrimBright), 6));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        Label subtitle = Add(UiTheme.Label(new Vector2(0, 118), new Vector2(Width, 16), "A dungeon crawl for one to four heroes", 10, Palette.TextDim));
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;

        PlayButton = Add(UiTheme.Button(new Vector2(240, 164), new Vector2(160, 26), "Play"));
        PlayButton.Pressed += () => PlayRequested?.Invoke();
        CharactersButton = Add(UiTheme.Button(new Vector2(240, 198), new Vector2(160, 26), "Characters"));
        CharactersButton.Pressed += () => CharactersRequested?.Invoke();
        Button quit = Add(UiTheme.Button(new Vector2(240, 232), new Vector2(160, 26), "Quit"));
        quit.Pressed += () => Sfx.QuitGame(this);

        Label footer = Add(UiTheme.Label(new Vector2(0, 326), new Vector2(Width, 12), "Early build: The Emerald Keep, local hot-seat for 1-4 players", color: Palette.TextDim));
        footer.HorizontalAlignment = HorizontalAlignment.Center;
    }
}
