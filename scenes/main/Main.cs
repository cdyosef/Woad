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
/// The app's root: owns the content and the character store, swaps between screens, and saves
/// each character's results when a run ends. Debug flags: <c>--uitest</c>, <c>--floortest</c> and
/// <c>--autoplay</c> jump straight into a run with the debug party; <c>--menutest</c> and
/// <c>--resultstest</c> click through the menus using a throwaway save folder.
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
        bool throwawaySaves = args.Contains("--menutest") || args.Contains("--resultstest");
        string saveFolder = throwawaySaves
            ? Path.Combine(Path.GetTempPath(), "woad-uitest", Guid.NewGuid().ToString("N"))
            : ProjectSettings.GlobalizePath("user://characters");
        _characters = new JsonCharacterRepository(saveFolder, _content);

        AudioSettings.Load(throwawaySaves ? Path.Combine(saveFolder, "settings.cfg") : "user://settings.cfg");
        AddChild(new Sfx());
        AddChild(new Music());

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
            int partyAt = Array.IndexOf(args, "--party");
            int size = partyAt >= 0 && partyAt + 1 < args.Length && int.TryParse(args[partyAt + 1], out int n) ? Math.Clamp(n, 1, GameRules.MaxHeroes) : DemoRun.DebugHeroes.Count;
            ShowGame(DungeonGenerator.CreateRun(_content, ThemeId, DemoRun.DebugParty(size), seed), []);
        }
        else if (args.Contains("--resultstest"))
        {
            RunResultsTest(saveFolder);
        }
        else
        {
            ShowMainMenu();
        }

        if (args.Contains("--menutest"))
        {
            RunMenuTest(saveFolder);
        }
    }

    public override void _ExitTree()
    {
        Theme = null;
        GameArt.Release();
        UiTheme.Release();
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
        menu.SettingsRequested += ShowSettings;
        ShowMenu(menu);
    }

    /// <summary>Shows one of the menu screens, which share the title music.</summary>
    private void ShowMenu(Control screen)
    {
        Music.Play("title");
        Show(screen);
    }

    private void ShowCharacters()
    {
        var screen = new CharacterListScreen { Repository = _characters, Content = _content };
        screen.BackRequested += ShowMainMenu;
        screen.NewRequested += () => ShowEditor(null, ShowCharacters);
        screen.EditRequested += character => ShowEditor(character, ShowCharacters);
        ShowMenu(screen);
    }

    private void ShowSettings()
    {
        var screen = new SettingsScreen();
        screen.BackRequested += ShowMainMenu;
        ShowMenu(screen);
    }

    private void ShowEditor(Character? editing, Action whenClosed)
    {
        var editor = new CharacterEditor { Repository = _characters, Content = _content, Editing = editing };
        editor.Closed += whenClosed;
        ShowMenu(editor);
    }

    private void ShowRunSetup()
    {
        var screen = new RunSetupScreen { Repository = _characters };
        screen.BackRequested += ShowMainMenu;
        screen.NewCharacterRequested += () => ShowEditor(null, ShowRunSetup);
        screen.StartRequested += StartRun;
        ShowMenu(screen);
    }

    /// <summary>Generates a dungeon for the party (carrying their current gear) and starts playing it.</summary>
    private void StartRun(IReadOnlyList<Character> party, ulong seed) =>
        ShowGame(DungeonGenerator.CreateRun(_content, ThemeId, [.. party.Select(character => character.ToHeroSetup())], seed), party);

    private void ShowGame(RunSetup setup, IReadOnlyList<Character> party)
    {
        var game = new GameScreen { Setup = setup, Content = _content };
        game.ExitRequested += ShowRunSetup;
        game.RunFinished += results => FinishRun(results, setup, party);
        Show(game);
    }

    /// <summary>Saves what each character earned, then shows the results.</summary>
    private async void FinishRun(IReadOnlyList<HeroRunResult> results, RunSetup setup, IReadOnlyList<Character> party)
    {
        var rows = new List<ResultRow>();
        string? saveProblem = null;
        foreach (HeroRunResult result in results)
        {
            Character? character = party.FirstOrDefault(member => member.Id == result.CharacterId);
            RunReward? reward = character?.ApplyRunResult(result);
            if (character is not null)
            {
                try
                {
                    await _characters.SaveAsync(character);
                }
                catch (IOException exception)
                {
                    saveProblem = exception.Message;
                }
            }

            rows.Add(new ResultRow(result, character, reward));
        }

        Music.Play(results.Any(result => result.Won) ? "victory" : "defeat", loop: false);
        var screen = new ResultsScreen { Rows = rows, Content = _content, SaveProblem = saveProblem };
        screen.DoneRequested += ShowRunSetup;
        screen.PlayAgainRequested += () =>
        {
            ulong seed = (ulong)DateTime.UtcNow.Ticks;
            if (party.Count > 0)
            {
                StartRun(party, seed);
            }
            else
            {
                ShowGame(DungeonGenerator.CreateRun(_content, setup.ThemeId, setup.Heroes, seed), party);
            }
        };
        Show(screen);
    }
}
