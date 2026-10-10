using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Rules;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;

namespace Woad.Scenes;

/// <summary>
/// The app's root: owns the content and the character store, and swaps between screens.
/// Debug flags: <c>--uitest</c> and <c>--autoplay</c> jump straight into a run with the debug
/// party; <c>--menutest</c> clicks through the menus using a throwaway save folder.
/// </summary>
public partial class Main : Control
{
    /// <summary>The only dungeon so far. Choosing among themes comes with more dungeons.</summary>
    private const string ThemeId = "emerald_keep";

    private ContentCatalog _content = null!;
    private ICharacterRepository _characters = null!;
    private Control? _screen;

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        _content = ContentCatalog.LoadDefault();

        string[] args = OS.GetCmdlineUserArgs();
        bool menuTest = args.Contains("--menutest");
        string saveFolder = menuTest
            ? Path.Combine(Path.GetTempPath(), "woad-menutest", Guid.NewGuid().ToString("N"))
            : ProjectSettings.GlobalizePath("user://characters");
        _characters = new JsonCharacterRepository(saveFolder, _content);

        ulong seed = (ulong)DateTime.UtcNow.Ticks;
        if (args.Contains("--uitest"))
        {
            Show(new GameScreen { Setup = DemoRun.SingleRoom(seed), Content = _content });
        }
        else if (args.Contains("--floortest"))
        {
            Show(new GameScreen { Setup = DemoRun.DoorsAndStairs(seed), Content = _content });
        }
        else if (args.Contains("--autoplay"))
        {
            ShowGame(DemoRun.DebugHeroes, seed);
        }
        else
        {
            ShowMainMenu();
        }

        if (menuTest)
        {
            RunMenuTest(saveFolder);
        }
    }

    private void Show(Control screen)
    {
        if (_screen is not null)
        {
            _screen.Visible = false;
            _screen.QueueFree();
        }

        _screen = screen;
        AddChild(screen);
    }

    private void ShowMainMenu()
    {
        var menu = new MainMenu();
        menu.PlayRequested += ShowRunSetup;
        menu.CharactersRequested += ShowCharacters;
        Show(menu);
    }

    private void ShowCharacters()
    {
        var screen = new CharacterListScreen { Repository = _characters, Content = _content };
        screen.BackRequested += ShowMainMenu;
        screen.NewRequested += () => ShowEditor(null, ShowCharacters);
        screen.EditRequested += character => ShowEditor(character, ShowCharacters);
        Show(screen);
    }

    private void ShowEditor(Character? editing, Action whenClosed)
    {
        var editor = new CharacterEditor { Repository = _characters, Content = _content, Editing = editing };
        editor.Closed += whenClosed;
        Show(editor);
    }

    private void ShowRunSetup()
    {
        var screen = new RunSetupScreen { Repository = _characters };
        screen.BackRequested += ShowMainMenu;
        screen.NewCharacterRequested += () => ShowEditor(null, ShowRunSetup);
        screen.StartRequested += ShowGame;
        Show(screen);
    }

    /// <summary>Generates a dungeon for the party and starts playing it.</summary>
    private void ShowGame(IReadOnlyList<HeroSetup> heroes, ulong seed)
    {
        RunSetup NewRun(ulong runSeed) => DungeonGenerator.CreateRun(_content, ThemeId, heroes, runSeed);

        var game = new GameScreen { Setup = NewRun(seed), Content = _content, NewRun = NewRun };
        game.ExitRequested += ShowRunSetup;
        Show(game);
    }
}
