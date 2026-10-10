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

        Label title = Add(UiTheme.Label(new Vector2(0, 52), new Vector2(Width, 60), "WOAD", 52, Palette.TrimBright));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        Label subtitle = Add(UiTheme.Label(new Vector2(0, 118), new Vector2(Width, 16), "A dungeon crawl for one to four heroes", 10, Palette.TextDim));
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;

        PlayButton = Add(UiTheme.Button(new Vector2(240, 164), new Vector2(160, 26), "Play"));
        PlayButton.Pressed += () => PlayRequested?.Invoke();
        CharactersButton = Add(UiTheme.Button(new Vector2(240, 198), new Vector2(160, 26), "Characters"));
        CharactersButton.Pressed += () => CharactersRequested?.Invoke();
        Button quit = Add(UiTheme.Button(new Vector2(240, 232), new Vector2(160, 26), "Quit"));
        quit.Pressed += () => GetTree().Quit();

        Label footer = Add(UiTheme.Label(new Vector2(0, 326), new Vector2(Width, 12), "Early build: one demo room, local hot-seat", color: Palette.TextDim));
        footer.HorizontalAlignment = HorizontalAlignment.Center;
    }
}
