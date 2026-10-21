using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;

namespace Woad.Scenes;

/// <summary>
/// The app's root: owns the content and the character store, swaps between screens, and saves
/// each character's results when a run ends. Debug flags: <c>--uitest</c>, <c>--floortest</c>, <c>--hazardtest</c>, <c>--keeltest</c>, <c>--firetest</c>, <c>--cameratest</c>, <c>--bombtest</c>, <c>--rumbletest</c>, <c>--clicktest</c>, <c>--loottest</c>, <c>--tradetest</c>, <c>--monstertest</c>, <c>--dentest</c> and
/// <c>--autoplay</c> jump straight into a run with the debug party (<c>--party N</c> heroes,
/// <c>--theme id</c> for another dungeon); <c>--menutest</c> and
/// <c>--resultstest</c> click through the menus using a throwaway save folder.
/// </summary>
public partial class Main : Control
{
    /// <summary>The first dungeon, and the one debug runs use unless --theme says otherwise.</summary>
    private const string FirstThemeId = "emerald_keep";

    /// <summary>The dungeons in the order the Dungeon Run list offers them: the Emerald Keep first.</summary>
    private static readonly string[] DungeonOrder = ["emerald_keep", "sunken_temple"];

    /// <summary>The dungeon picked on the Dungeon Run list, for the party screen and the run.</summary>
    private string _themeId = FirstThemeId;

    private ContentCatalog _content = null!;

    /// <summary>The party screen's friendly fire switch, remembered between runs this session.</summary>
    private bool _friendlyFire;
    private ICharacterRepository _characters = null!;

    /// <summary>The Dungeon Run in progress, saved after every move.</summary>
    private SavedRunFile _runFile = null!;
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
        _runFile = new SavedRunFile(throwawaySaves
            ? Path.Combine(saveFolder, "runs", "current-run.json")
            : ProjectSettings.GlobalizePath("user://runs/current-run.json"));

        AudioSettings.Load(throwawaySaves ? Path.Combine(saveFolder, "settings.cfg") : "user://settings.cfg");
        AddChild(new Sfx());
        AddChild(new Music());

        ulong seed = (ulong)DateTime.UtcNow.Ticks;
        if (args.Contains("--uitest"))
        {
            Show(new GameScreen { Setup = DemoRun.SingleRoom(seed), Content = _content });
        }
        else if (args.Contains("--hazardtest"))
        {
            Show(new GameScreen { Setup = DemoRun.Hazards(seed), Content = _content });
        }
        else if (args.Contains("--floortest"))
        {
            Show(new GameScreen { Setup = DemoRun.DoorsAndStairs(seed), Content = _content });
        }
        else if (args.Contains("--keeltest"))
        {
            Show(new GameScreen { Setup = DemoRun.Keeling(seed), Content = _content });
        }
        else if (args.Contains("--firetest"))
        {
            Show(new GameScreen { Setup = DemoRun.Fire(seed), Content = _content });
        }
        else if (args.Contains("--cameratest"))
        {
            Show(new GameScreen { Setup = DemoRun.BigHall(seed), Content = _content });
        }
        else if (args.Contains("--bombtest"))
        {
            Show(new GameScreen { Setup = DemoRun.Bombs(seed), Content = _content });
        }
        else if (args.Contains("--rumbletest"))
        {
            Show(new GameScreen { Setup = DemoRun.Duel(seed), Content = _content });
        }
        else if (args.Contains("--tradetest"))
        {
            Show(new GameScreen { Setup = DemoRun.Camp(seed), Content = _content });
        }
        else if (args.Contains("--dentest"))
        {
            Show(new GameScreen { Setup = DemoRun.Den(seed), Content = _content });
        }
        else if (args.Contains("--monstertest"))
        {
            Show(new GameScreen { Setup = DemoRun.Menagerie(seed), Content = _content });
        }
        else if (args.Contains("--loottest"))
        {
            Show(new GameScreen { Setup = DemoRun.Treasury(seed), Content = _content });
        }
        else if (args.Contains("--clicktest"))
        {
            Show(new GameScreen { Setup = DemoRun.Stalk(seed), Content = _content });
        }
        else if (args.Contains("--autoplay"))
        {
            int partyAt = Array.IndexOf(args, "--party");
            int size = partyAt >= 0 && partyAt + 1 < args.Length && int.TryParse(args[partyAt + 1], out int n) ? Math.Clamp(n, 1, GameRules.MaxHeroes) : DemoRun.DebugHeroes.Count;
            int themeAt = Array.IndexOf(args, "--theme");
            string theme = themeAt >= 0 && themeAt + 1 < args.Length ? args[themeAt + 1] : FirstThemeId;
            ShowGame(DungeonGenerator.CreateRun(_content, theme, DemoRun.DebugParty(size), seed), []);
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
        menu.PlayRequested += ShowModeSelect;
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

    private void ShowModeSelect()
    {
        var screen = new ModeSelectScreen();
        screen.BackRequested += ShowMainMenu;
        screen.DungeonRunRequested += ShowDungeonSelect;
        ShowMenu(screen);
    }

    private void ShowDungeonSelect() => ShowDungeonSelect(null);

    /// <param name="saveProblem">Why the saved run just failed to continue, if it did.</param>
    private void ShowDungeonSelect(string? saveProblem)
    {
        string? savedRun = null;
        try
        {
            if (saveProblem is null && _runFile.Load() is { } run)
            {
                string dungeon = _content.Themes.TryGetValue(run.ThemeId, out ThemeDefinition? theme) ? theme.Name : run.ThemeId;
                savedRun = $"{dungeon}: {string.Join(", ", run.Heroes.Select(hero => hero.Name))}. Floor {run.Floor}, round {run.Round}.";
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            saveProblem = exception.Message;
        }

        var screen = new DungeonSelectScreen
        {
            SavedRun = savedRun,
            SaveProblem = saveProblem is null ? null : "Your saved run can't be continued. " + saveProblem,
            Dungeons = [.. _content.Themes.Values
                .OrderBy(theme => Array.IndexOf(DungeonOrder, theme.Id) is var at and >= 0 ? at : int.MaxValue)
                .ThenBy(theme => theme.Name, StringComparer.Ordinal)
                .Select(theme => (theme.Id, theme.Name, theme.Description))],
        };
        screen.BackRequested += ShowModeSelect;
        screen.ContinueRequested += ContinueRun;
        screen.DungeonChosen += themeId =>
        {
            _themeId = themeId;
            ShowRunSetup();
        };
        ShowMenu(screen);
    }

    private void ShowRunSetup()
    {
        var screen = new RunSetupScreen
        {
            Repository = _characters,
            FriendlyFire = _friendlyFire,
            ThemeId = _themeId,
            DungeonName = _content.Themes.TryGetValue(_themeId, out ThemeDefinition? theme) ? theme.Name : _themeId,
            ReplacesSavedRun = _runFile.Exists,
        };
        screen.BackRequested += ShowDungeonSelect;
        screen.NewCharacterRequested += () => ShowEditor(null, ShowRunSetup);
        screen.StartRequested += StartRun;
        ShowMenu(screen);
    }

    /// <summary>Generates a dungeon for the party (carrying their current gear) and starts playing it.</summary>
    private void StartRun(IReadOnlyList<Character> party, ulong seed, bool friendlyFire, string themeId)
    {
        _friendlyFire = friendlyFire;
        _themeId = themeId;
        var save = new SavedRun { ThemeId = themeId, Seed = seed, FriendlyFire = friendlyFire, Heroes = Party.ToHeroSetups(party) };
        WriteSave(save);
        ShowGame(save.CreateSetup(_content), party, save);
    }

    /// <summary>Picks the saved run up where it was left, with its characters as they are now.</summary>
    private async void ContinueRun()
    {
        SavedRun run;
        GameSession session;
        try
        {
            run = _runFile.Load() ?? throw new InvalidDataException("It's gone.");
            session = run.Restore(_content);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            GD.PushWarning($"The saved run can't be continued: {exception.Message}");
            ShowDungeonSelect(exception.Message);
            return;
        }

        CharacterLoadResult saved = await _characters.LoadAllAsync();
        List<Character> party = [.. run.Heroes
            .Select(hero => saved.Characters.FirstOrDefault(character => character.Id == hero.CharacterId))
            .OfType<Character>()];
        _themeId = run.ThemeId;
        _friendlyFire = run.FriendlyFire;
        ShowGame(run.CreateSetup(_content), party, run, session);
    }

    /// <param name="save">Where the run is saved as it's played, or null for a run that isn't (debug runs).</param>
    /// <param name="restored">The saved run, already replayed, to play on from.</param>
    private void ShowGame(RunSetup setup, IReadOnlyList<Character> party, SavedRun? save = null, GameSession? restored = null)
    {
        var game = new GameScreen { Setup = setup, Content = _content, Restored = restored, SavesRun = save is not null };
        if (save is not null)
        {
            game.CommandPlayed += (command, state) =>
            {
                save.Record(command, state);
                WriteSave(save);
            };
            game.ExitRequested += ShowDungeonSelect;
        }
        else
        {
            game.ExitRequested += ShowRunSetup;
        }

        game.RunFinished += results =>
        {
            if (save is not null)
            {
                _runFile.Delete();
            }

            FinishRun(results, setup, party);
        };
        Show(game);
    }

    private void WriteSave(SavedRun save)
    {
        try
        {
            _runFile.Save(save);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"The run couldn't be saved: {exception.Message}");
        }
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
                StartRun(party, seed, setup.FriendlyFire, setup.ThemeId);
            }
            else
            {
                ShowGame(DungeonGenerator.CreateRun(_content, setup.ThemeId, setup.Heroes, seed) with { FriendlyFire = setup.FriendlyFire }, party);
            }
        };
        Show(screen);
    }
}
