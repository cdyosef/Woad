using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Core.Tavern;
using Woad.Core.Woarld;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;
using Woad.Scenes.Tavern;
using Woad.Scenes.Woarld;

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

    /// <summary>Where the Dungeon Run's Woarld Map journey is saved.</summary>
    private string _journeyPath = "";

    /// <summary>The Dungeon Run's Woarld Map journey, loaded when first needed.</summary>
    private WoarldJourney? _journey;

    /// <summary>The party walking the Woarld Map this session, and who leads it.</summary>
    private List<Character> _woarldParty = [];
    private int _woarldLeader;

    /// <summary>News for the Woarld Map to show when it next opens.</summary>
    private string _woarldNews = "";

    /// <summary>The run was raided from the Woarld Map, so it goes back there afterwards.</summary>
    private bool _runFromMap;

    /// <summary>Playing Raiding Party: the map and taverns are the shared Woarld's, not the Dungeon Run's.</summary>
    private bool _raiding;

    /// <summary>Where the Raiding Party's shared Woarld is saved.</summary>
    private string _raidWorldPath = "";

    /// <summary>The Raiding Party's shared Woarld, loaded when first needed.</summary>
    private RaidWorld? _raidWorld;

    /// <summary>The saved raids.</summary>
    private RaidSaveStore _raidStore = null!;

    /// <summary>The raid being played, while raiding.</summary>
    private RaidSave? _raid;

    /// <summary>Where the run being played is saved: the Dungeon Run's slot, or the raid's own.</summary>
    private SavedRunFile _activeRunFile = null!;
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
        _journeyPath = throwawaySaves
            ? Path.Combine(saveFolder, "woarld", "dungeon-run.json")
            : ProjectSettings.GlobalizePath("user://woarld/dungeon-run.json");
        _raidWorldPath = throwawaySaves
            ? Path.Combine(saveFolder, "woarld", "raiding-party.json")
            : ProjectSettings.GlobalizePath("user://woarld/raiding-party.json");
        _raidStore = new RaidSaveStore(throwawaySaves ? Path.Combine(saveFolder, "raids") : ProjectSettings.GlobalizePath("user://raids"));
        _activeRunFile = _runFile;

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
        screen.DungeonRunRequested += () =>
        {
            _raiding = false;
            ShowDungeonSelect();
        };
        screen.RaidingPartyRequested += () =>
        {
            _raiding = true;
            ShowRaidList();
        };
        ShowMenu(screen);
    }

    private void ShowRaidList() => ShowRaidList("");

    /// <summary>Raiding Party's saved raids, to continue one or begin a new one.</summary>
    /// <param name="problem">Why a raid just failed to continue, if one did.</param>
    private async void ShowRaidList(string problem)
    {
        (IReadOnlyList<RaidSave> raids, IReadOnlyList<string> problems) = _raidStore.LoadAll();
        foreach (string unreadable in problems)
        {
            GD.PushWarning($"A raid couldn't be read: {unreadable}");
        }

        IReadOnlyList<Character> characters = (await _characters.LoadAllAsync()).Characters;
        string Names(RaidSave raid) => string.Join(", ", raid.Party.Select(id => characters.FirstOrDefault(character => character.Id == id)?.Name ?? "(deleted)"));
        var screen = new RaidListScreen
        {
            Raids = [.. raids],
            Problem = problem.Length > 0 ? problem : problems.Count > 0 ? $"{UiTheme.Count(problems.Count, "raid", "raids")} couldn't be read." : "",
            Describe = raid => $"{Names(raid)}. Day {raid.Place?.Day ?? 1}, " +
                               (_raidStore.RunFile(raid).Exists ? "in the middle of a dungeon." : raid.Location == RaidLocation.Inn ? "at The Waylay Inn." : "on the Woarld Map."),
        };
        screen.BackRequested += ShowModeSelect;
        screen.NewRaidRequested += () =>
        {
            _raid = null;
            _themeId = "";
            ShowRunSetup();
        };
        screen.ContinueRequested += ContinueRaid;
        screen.DeleteRequested += raid =>
        {
            try
            {
                _raidStore.Delete(raid);
            }
            catch (IOException exception)
            {
                GD.PushWarning($"{raid.Name} couldn't be deleted: {exception.Message}");
            }
        };
        ShowMenu(screen);
    }

    /// <summary>Picks a raid up where it was left: in a dungeon, at the inn or out on the map.</summary>
    private async void ContinueRaid(RaidSave raid)
    {
        IReadOnlyList<Character> saved = (await _characters.LoadAllAsync()).Characters;
        List<Character> party = [.. raid.Party.Select(id => saved.FirstOrDefault(character => character.Id == id)).OfType<Character>()];
        if (!party.Any(member => !member.IsCompanion))
        {
            ShowRaidList($"{raid.Name} can't go on: its players' characters have been deleted.");
            return;
        }

        _raiding = true;
        _raid = raid;
        _woarldParty = party;
        _woarldLeader = party.FindIndex(member => member.Id == raid.Leader && !member.IsCompanion) is var leader and >= 0 ? leader : party.FindIndex(member => !member.IsCompanion);
        _friendlyFire = raid.FriendlyFire;
        WoarldJourney journey = LoadRaidWorld().Journey;
        try
        {
            journey.Resume(raid.Place ?? AtTheInn(party.Count));
        }
        catch (ArgumentException)
        {
            journey.Resume(AtTheInn(party.Count));
        }

        journey.SetPartySize(party.Count);
        SavedRunFile runFile = _raidStore.RunFile(raid);
        if (runFile.Exists)
        {
            _runFromMap = true;
            ContinueRun(runFile, party);
        }
        else if (raid.Location == RaidLocation.Map)
        {
            ShowWoarldMap();
        }
        else
        {
            ShowTavern();
        }
    }

    /// <summary>A new raid's place: gathered at The Waylay Inn on the first day.</summary>
    private static JourneyPlace AtTheInn(int partySize)
    {
        GridPosition inn = WoarldMap.LoadDefault().Places.Single(place => place.Id == TavernDefinition.LoadDefault().PlaceId).Position;
        return new JourneyPlace(inn, [.. Enumerable.Repeat(inn, Math.Max(0, partySize - 1))], 1, 0, 0);
    }

    /// <summary>Saves the raid being played: its party and leader, and where and when it is.</summary>
    private void SaveRaid(RaidLocation? location = null)
    {
        if (!_raiding || _raid is null)
        {
            return;
        }

        _raid.Party.Clear();
        _raid.Party.AddRange(_woarldParty.Select(member => member.Id));
        _raid.Leader = _woarldParty.Count > 0 ? _woarldParty[Math.Clamp(_woarldLeader, 0, _woarldParty.Count - 1)].Id : default;
        _raid.FriendlyFire = _friendlyFire;
        _raid.Place = LoadRaidWorld().Journey.Place;
        if (location is { } where)
        {
            _raid.Location = where;
        }

        try
        {
            _raidStore.Save(_raid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"{_raid.Name} couldn't be saved: {exception.Message}");
        }
    }

    /// <summary>A hero's hearts in the raid, out of their full health.</summary>
    private string RaidHearts(Character hero) => $"{_raid!.HeartsOf(hero.Id, hero.MaxHearts(_content))}/{hero.MaxHearts(_content)}";

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
        screen.WoarldMapRequested += () =>
        {
            _themeId = "";
            ShowRunSetup();
        };
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
            DungeonName = _raiding ? TavernDefinition.LoadDefault().Name : _themeId.Length == 0 ? "The Woarld Map" : _content.Themes.TryGetValue(_themeId, out ThemeDefinition? theme) ? theme.Name : _themeId,
            ReplacesSavedRun = _themeId.Length > 0 && _runFile.Exists,
        };
        screen.BackRequested += () =>
        {
            if (_raiding)
            {
                ShowRaidList();
            }
            else
            {
                ShowDungeonSelect();
            }
        };
        screen.NewCharacterRequested += () => ShowEditor(null, ShowRunSetup);
        screen.StartRequested += (party, seed, friendlyFire, themeId) =>
        {
            _runFromMap = false;
            if (themeId.Length > 0)
            {
                StartRun(party, seed, friendlyFire, themeId);
                return;
            }

            _friendlyFire = friendlyFire;
            _woarldParty = [.. party];
            _woarldLeader = Math.Max(0, _woarldParty.FindIndex(hero => !hero.IsCompanion));
            if (_raiding)
            {
                // A new raid sets out from the inn, on its first day.
                LoadRaidWorld().Journey.Resume(AtTheInn(_woarldParty.Count));
                _raid = new RaidSave { Name = _raidStore.NameFor(_woarldParty[_woarldLeader].Name), Location = RaidLocation.Inn };
                SaveRaid();
                ShowTavern();
            }
            else
            {
                ShowWoarldMap();
            }
        };
        ShowMenu(screen);
    }

    /// <summary>Generates a dungeon for the party (carrying their current gear) and starts playing it.</summary>
    private void StartRun(IReadOnlyList<Character> party, ulong seed, bool friendlyFire, string themeId)
    {
        _friendlyFire = friendlyFire;
        _themeId = themeId;
        IReadOnlyList<HeroSetup> heroes = Party.ToHeroSetups(party);
        if (_raiding && _raid is not null)
        {
            // In a raid, heroes go in with the hearts they have left.
            heroes = [.. heroes.Select((hero, seat) => hero with { Hearts = _raid.HeartsOf(party[seat].Id, party[seat].MaxHearts(_content)) })];
            _activeRunFile = _raidStore.RunFile(_raid);
        }
        else
        {
            _activeRunFile = _runFile;
        }

        var save = new SavedRun { ThemeId = themeId, Seed = seed, FriendlyFire = friendlyFire, Heroes = heroes };
        WriteSave(save);
        ShowGame(save.CreateSetup(_content), party, save);
    }

    /// <summary>Picks the Dungeon Run's saved run up where it was left.</summary>
    private void ContinueRun()
    {
        _raiding = false;
        _runFromMap = false;
        ContinueRun(_runFile, null);
    }

    /// <summary>Picks a saved run up where it was left.</summary>
    /// <param name="party">The characters playing it (a raid's own); null loads them as they are now.</param>
    private async void ContinueRun(SavedRunFile file, IReadOnlyList<Character>? party)
    {
        SavedRun run;
        GameSession session;
        try
        {
            run = file.Load() ?? throw new InvalidDataException("It's gone.");
            session = run.Restore(_content);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            GD.PushWarning($"The saved run can't be continued: {exception.Message}");
            if (_raiding)
            {
                ShowRaidList($"The dungeon can't be continued: {exception.Message}");
            }
            else
            {
                ShowDungeonSelect(exception.Message);
            }

            return;
        }

        IReadOnlyList<Character> characters = party ?? (await _characters.LoadAllAsync()).Characters;
        List<Character> playing = [.. run.Heroes
            .Select(hero => characters.FirstOrDefault(character => character.Id == hero.CharacterId))
            .OfType<Character>()];
        _themeId = run.ThemeId;
        _friendlyFire = run.FriendlyFire;
        _activeRunFile = file;
        ShowGame(run.CreateSetup(_content), playing, run, session);
    }

    /// <param name="save">Where the run is saved as it's played, or null for a run that isn't (debug runs).</param>
    /// <param name="restored">The saved run, already replayed, to play on from.</param>
    private void ShowGame(RunSetup setup, IReadOnlyList<Character> party, SavedRun? save = null, GameSession? restored = null)
    {
        SavedRunFile runFile = _activeRunFile;
        var game = new GameScreen { Setup = setup, Content = _content, Restored = restored, SavesRun = save is not null };
        if (save is not null)
        {
            game.CommandPlayed += (command, state) =>
            {
                save.Record(command, state);
                WriteSave(save);
            };
            game.ExitRequested += () =>
            {
                if (_raiding)
                {
                    // The raid waits, mid-dungeon, to be continued.
                    SaveRaid(RaidLocation.Map);
                    ShowRaidList();
                }
                else if (_runFromMap)
                {
                    ShowWoarldMap();
                }
                else
                {
                    ShowDungeonSelect();
                }
            };
        }
        else
        {
            game.ExitRequested += ShowRunSetup;
        }

        game.RunFinished += results =>
        {
            if (save is not null)
            {
                runFile.Delete();
            }

            FinishRun(results, setup, party);
        };
        Show(game);
    }

    /// <summary>Walks the Woarld Map with the party picked for it: the Raiding Party's shared Woarld, or the Dungeon Run's.</summary>
    private void ShowWoarldMap()
    {
        RaidWorld? world = _raiding ? LoadRaidWorld() : null;
        WoarldJourney journey = world?.Journey ?? LoadJourney();
        journey.SetPartySize(_woarldParty.Count);
        var screen = new WoarldMapScreen
        {
            Journey = journey,
            Party = _woarldParty,
            Leader = _woarldLeader,
            Greeting = _woarldNews,
            ReplacesSavedRun = world is null && _runFile.Exists,
            BackTooltip = world is null ? "Back to the Dungeon Run list. Your travels are saved." : "Back to your raids. This raid and the Woarld are saved.",
            RestBoosts = world is null ? null : walkers => world.TakeRestBoosts([.. walkers.Select(hero => hero.Id)], TavernDefinition.LoadDefault()),
            HeartsText = _raid is null || world is null ? null : RaidHearts,
        };
        _woarldNews = "";
        SaveRaid(RaidLocation.Map);
        screen.BackRequested += () =>
        {
            SaveMapProgress();
            SaveRaid();
            if (world is null)
            {
                ShowDungeonSelect();
            }
            else
            {
                ShowRaidList();
            }
        };
        screen.JourneyChanged += () =>
        {
            _woarldLeader = screen.Leader;
            SaveMapProgress();
            SaveRaid();
        };
        screen.Rested += (walkers, rolls) =>
        {
            if (world is not null && _raid is not null)
            {
                foreach ((Character hero, RestRoll roll) in walkers.Zip(rolls))
                {
                    _raid.Heal(hero.Id, roll.Hearts, hero.MaxHearts(_content));
                }
            }
        };
        if (world is not null)
        {
            screen.PlaceEntered += place =>
            {
                if (place.Id == TavernDefinition.LoadDefault().PlaceId)
                {
                    SaveMapProgress();
                    ShowTavern();
                }
            };
        }

        screen.RaidRequested += place =>
        {
            journey.PassDay();
            SaveMapProgress();
            _runFromMap = true;
            StartRun(_woarldParty, (ulong)DateTime.UtcNow.Ticks, _friendlyFire, place.ThemeId!);
        };
        ShowMenu(screen);
    }

    /// <summary>Inside The Waylay Inn with the Raiding Party; the door leads out to the Woarld Map.</summary>
    private async void ShowTavern()
    {
        TavernDefinition tavern = TavernDefinition.LoadDefault();
        RaidWorld world = LoadRaidWorld();
        // The roster, with the party's own copies of its members, so changes show everywhere.
        List<Character> roster = [.. (await _characters.LoadAllAsync()).Characters
            .Select(saved => _woarldParty.FirstOrDefault(member => member.Id == saved.Id) ?? saved)];
        RaidSave raid = _raid!;
        raid.HealRooms(world.Journey.Day, id => roster.FirstOrDefault(character => character.Id == id)?.MaxHearts(_content) ?? 1);
        var screen = new TavernScreen
        {
            Tavern = tavern,
            World = world,
            Content = _content,
            Party = _woarldParty,
            Roster = roster,
            Leader = _woarldLeader,
            Greeting = _woarldNews,
            Raid = raid,
        };
        _woarldNews = "";
        SaveRaid(RaidLocation.Inn);
        screen.LeaveRequested += () =>
        {
            _woarldLeader = screen.Leader;
            ShowWoarldMap();
        };
        screen.PartyChanged += () =>
        {
            _woarldLeader = screen.Leader;
            SaveRaid();
        };
        screen.WorldChanged += () =>
        {
            SaveRaidWorld();
            SaveRaid();
        };
        screen.CharacterChanged += SaveCharacter;
        screen.RecruitHired += SaveCharacter;
        ShowMenu(screen);
    }

    private async void SaveCharacter(Character character)
    {
        try
        {
            await _characters.SaveAsync(character);
        }
        catch (IOException exception)
        {
            GD.PushWarning($"{character.Name} couldn't be saved: {exception.Message}");
        }
    }

    /// <summary>The Raiding Party's shared Woarld: as saved, or a new one if there's none (or it can't be read).</summary>
    private RaidWorld LoadRaidWorld()
    {
        if (_raidWorld is not null)
        {
            return _raidWorld;
        }

        WoarldMap map = WoarldMap.LoadDefault();
        try
        {
            if (File.Exists(_raidWorldPath))
            {
                return _raidWorld = RaidWorld.FromJson(File.ReadAllText(_raidWorldPath), map);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            GD.PushWarning($"The Raiding Party's Woarld couldn't be loaded, so it starts afresh: {exception.Message}");
        }

        return _raidWorld = RaidWorld.Begin(map);
    }

    private void SaveRaidWorld()
    {
        if (_raidWorld is not null)
        {
            WriteFile(_raidWorldPath, _raidWorld.ToJson(), "The Raiding Party's Woarld");
        }
    }

    /// <summary>Saves the map the party is walking: the Raiding Party's Woarld, or the Dungeon Run's journey.</summary>
    private void SaveMapProgress()
    {
        if (_raiding)
        {
            SaveRaidWorld();
        }
        else
        {
            SaveJourney();
        }
    }

    /// <summary>The Dungeon Run's journey: as saved, or a new one if there's none (or it can't be read).</summary>
    private WoarldJourney LoadJourney()
    {
        if (_journey is not null)
        {
            return _journey;
        }

        WoarldMap map = WoarldMap.LoadDefault();
        try
        {
            if (File.Exists(_journeyPath))
            {
                return _journey = WoarldJourney.FromJson(File.ReadAllText(_journeyPath), map);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            GD.PushWarning($"The Woarld Map journey couldn't be loaded, so it starts afresh: {exception.Message}");
        }

        return _journey = WoarldJourney.Begin(map, 1);
    }

    private void SaveJourney()
    {
        if (_journey is null)
        {
            return;
        }

        WriteFile(_journeyPath, _journey.ToJson(), "The Woarld Map journey");
    }

    /// <summary>Writes a save beside the old one, then swaps it in, so a crash mid-write never loses both.</summary>
    private static void WriteFile(string path, string contents, string what)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", contents);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"{what} couldn't be saved: {exception.Message}");
        }
    }

    private void WriteSave(SavedRun save)
    {
        try
        {
            _activeRunFile.Save(save);
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

        if (_raiding && _raid is not null)
        {
            _raid.ApplyRun(results, id => party.FirstOrDefault(member => member.Id == id)?.MaxHearts(_content) ?? 1);
        }

        if (results.Any(result => result.Won))
        {
            IReadOnlyList<WoarldPlace> opened = (_raiding ? LoadRaidWorld().Journey : LoadJourney()).MarkBeaten(setup.ThemeId);
            SaveMapProgress();
            if (opened.Count > 0)
            {
                _woarldNews = $"{string.Join(" and ", opened.Select(place => place.Name))} has opened! Find it on the Woarld Map.";
            }
        }

        Music.Play(results.Any(result => result.Won) ? "victory" : "defeat", loop: false);
        var screen = new ResultsScreen { Rows = rows, Content = _content, SaveProblem = saveProblem };
        screen.DoneRequested += () =>
        {
            if (_runFromMap)
            {
                ShowWoarldMap();
            }
            else
            {
                ShowRunSetup();
            }
        };
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
