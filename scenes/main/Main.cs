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
using Woad.Core.Net;
using Woad.Scenes.Menu;
using Woad.Scenes.Net;
using Woad.Scenes.Ui;
using Woad.Scenes.Tavern;
using Woad.Scenes.Tracker;
using Woad.Scenes.Woarld;

namespace Woad.Scenes;

/// <summary>
/// The app's root: owns the content and the character store, swaps between screens, and saves
/// each character's results when a run ends. Debug flags: <c>--uitest</c>, <c>--floortest</c>, <c>--hazardtest</c>, <c>--keeltest</c>, <c>--firetest</c>, <c>--cameratest</c>, <c>--bombtest</c>, <c>--rumbletest</c>, <c>--offhandtest</c>, <c>--toolstest</c>, <c>--smashtest</c>, <c>--clicktest</c>, <c>--loottest</c>, <c>--dietypetest</c>, <c>--tradetest</c>, <c>--monstertest</c>, <c>--dentest</c>, <c>--merchanttest</c>, <c>--wanderertest</c>, <c>--encountertest</c>, <c>--skirmishtest</c>, <c>--gatetest</c> and
/// <c>--autoplay</c> jump straight into a run with the debug party (<c>--party N</c> heroes,
/// <c>--theme id</c> for another dungeon); <c>--menutest</c> and
/// <c>--resultstest</c> click through the menus using a throwaway save folder; <c>--tracker</c> opens the
/// Music Tracker, and <c>--trackertest</c> writes and exports a song with it into a throwaway folder.
/// <c>--paint</c> opens Pixel Paint, and <c>--pixeltest</c> paints and saves a figure with it into a throwaway folder.
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

	/// <summary>The party screen's friendly fire switch, remembered between runs this session: on to begin with.</summary>
	private bool _friendlyFire = true;
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

	/// <summary>The fight out on the Woarld Map being played, if the run is one: settled on the map when it ends.</summary>
	private SkirmishSpec? _skirmish;

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
		_backdrop = new ColorRect { Color = Palette.Background, Size = new Vector2(640, 360), MouseFilter = MouseFilterEnum.Ignore };
		AddChild(_backdrop);
		GetViewport().SizeChanged += FitScreen;
		FitCanvas();
		_content = ContentCatalog.LoadDefault();

		string[] args = OS.GetCmdlineUserArgs();
		bool throwawaySaves = args.Contains("--menutest") || args.Contains("--resultstest") || args.Contains("--nethosttest") || args.Contains("--netguesttest") || args.Contains("--netrejointest") || args.Contains("--netoldtest") || args.Contains("--replaytest");
		string saveFolder = throwawaySaves
			? Path.Combine(Path.GetTempPath(), "woad-uitest", Guid.NewGuid().ToString("N"))
			: ProjectSettings.GlobalizePath("user://characters");
		_throwawaySaves = throwawaySaves;
		_saveFolder = saveFolder;
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
		_net = new NetLink { Name = "Net" };
		AddChild(_net);
		_net.RunStarted += (run, seats) => StartNetRun(run, seats, joining: false);
		_net.RunJoined += (run, seats) => StartNetRun(run, seats, joining: true);
		_net.PlayerArrived += OnNetPlayerArrived;
		_net.PlayerLeft += (_, name) =>
		{
			_netSession?.Note(SessionNoteKind.Left, name, _netRecord?.Commands.Count ?? 0);
			WriteReplay();
		};
		_net.ChatArrived += (heroId, text, language) =>
		{
			if (_netSession is { } session && _netRecord is { } run)
			{
				session.Note(SessionNoteKind.Chat, _screen is GameScreen playing ? playing.Session.State.Heroes.FirstOrDefault(hero => hero.Id == heroId)?.Name ?? "" : "", run.Commands.Count, heroId, text, language);
				WriteReplay();
			}
		};

		ulong seed = (ulong)DateTime.UtcNow.Ticks;
		if (args.Contains("--crestsheet"))
		{
			AddChild(new CrestSheet());
		}
		else if (args.Contains("--uitest"))
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
		else if (args.Contains("--smashtest"))
		{
			Show(new GameScreen { Setup = DemoRun.SmashDemo(seed), Content = _content });
		}
		else if (args.Contains("--leavetest"))
		{
			Show(new GameScreen { Setup = DemoRun.Leaver(seed), Content = _content });
		}
		else if (args.Contains("--auctiontest"))
		{
			RunAuctionTest();
		}
		else if (args.Contains("--clodtest"))
		{
			Show(new GameScreen { Setup = DemoRun.Clod(seed), Content = _content });
		}
		else if (args.Contains("--bagtest"))
		{
			Show(new GameScreen { Setup = DemoRun.Bags(seed), Content = _content });
		}
		else if (args.Contains("--torchtest"))
		{
			Show(new GameScreen { Setup = DemoRun.Torch(seed), Content = _content });
		}
		else if (args.Contains("--bathstest"))
		{
			Show(new GameScreen { Setup = DemoRun.Baths(seed), Content = _content });
		}
		else if (args.Contains("--painttest"))
		{
			Show(new GameScreen { Setup = DemoRun.Paint(seed), Content = _content });
		}
		else if (args.Contains("--toolstest"))
		{
			Show(new GameScreen { Setup = DemoRun.Tools(seed), Content = _content });
		}
		else if (args.Contains("--offhandtest"))
		{
			Show(new GameScreen { Setup = DemoRun.OffHand(seed), Content = _content });
		}
		else if (args.Contains("--rumbletest"))
		{
			Show(new GameScreen { Setup = DemoRun.Duel(seed), Content = _content });
		}
		else if (args.Contains("--tradetest"))
		{
			Show(new GameScreen { Setup = DemoRun.Camp(seed), Content = _content });
		}
		else if (args.Contains("--bosstest"))
		{
			Show(new GameScreen { Setup = DemoRun.BossRoom(seed), Content = _content });
		}
		else if (args.Contains("--gravestest"))
		{
			Show(new GameScreen { Setup = DemoRun.Graves(seed), Content = _content });
		}
		else if (args.Contains("--ritualtest"))
		{
			Show(new GameScreen { Setup = DemoRun.Graves(seed, ritualRounds: 2), Content = _content });
		}
		else if (args.Contains("--partytest"))
		{
			Show(new GameScreen { Setup = DemoRun.PartyCheck(seed), Content = _content });
		}
		else if (args.Contains("--companiontest"))
		{
			Show(new GameScreen { Setup = DemoRun.Escort(seed), Content = _content });
		}
		else if (args.Contains("--spelltest"))
		{
			Show(new GameScreen { Setup = DemoRun.Spells(seed), Content = _content });
		}
		else if (args.Contains("--magictest"))
		{
			Show(new GameScreen { Setup = DemoRun.Magic(seed), Content = _content });
		}
		else if (args.Contains("--terraintest"))
		{
			Show(new GameScreen { Setup = DemoRun.Terrain(seed), Content = _content });
		}
		else if (args.Contains("--wingstest"))
		{
			Show(new GameScreen { Setup = DemoRun.Wings(seed), Content = _content });
		}
		else if (args.Contains("--dietypetest"))
		{
			Show(new GameScreen { Setup = DemoRun.Elements(seed), Content = _content });
		}
		else if (args.Contains("--encountertest"))
		{
			RunEncounterTest();
		}
		else if (args.Contains("--skirmishtest"))
		{
			RunSkirmishTest(seed);
		}
		else if (args.Contains("--gatetest"))
		{
			RunGateTest();
		}
		else if (args.Contains("--wanderertest"))
		{
			RunWandererTest();
		}
		else if (args.Contains("--merchanttest"))
		{
			Show(new GameScreen { Setup = DemoRun.Market(seed), Content = _content });
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
		else if (args.Contains("--tracker") || args.Contains("--trackertest"))
		{
			ShowTracker(args.Contains("--trackertest"));
		}
		else if (args.Contains("--paint") || args.Contains("--pixeltest"))
		{
			ShowPaint(args.Contains("--pixeltest"));
		}
		else if (Array.IndexOf(args, "--replay") is var replayAt and >= 0 && replayAt + 1 < args.Length)
		{
			// Watching a replay straight away (as the video recorder does): from a move, perhaps clean, quitting at the end.
			int fromAt = Array.IndexOf(args, "--from");
			int from = fromAt >= 0 && fromAt + 1 < args.Length && int.TryParse(args[fromAt + 1], out int move) ? move : 0;
			WatchReplay(args[replayAt + 1], from, autoPlay: true, clean: args.Contains("--clean"), quitAtEnd: true, speed: 1);
		}
		else if (args.Contains("--locktest"))
		{
			RunLockTest();
		}
		else if (args.Contains("--sizetest"))
		{
			RunSizeTest(seed);
		}
		else if (args.Contains("--replaytest"))
		{
			RunReplayTest(saveFolder);
		}
		else if (args.Contains("--nethosttest"))
		{
			RunNetHostTest(saveFolder);
		}
		else if (args.Contains("--netguesttest"))
		{
			RunNetGuestTest(saveFolder);
		}
		else if (args.Contains("--netrejointest"))
		{
			RunNetRejoinTest(saveFolder);
		}
		else if (args.Contains("--netoldtest"))
		{
			RunNetOldTest(saveFolder);
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
		Looks.Release();
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
		FitScreen();
	}

	/// <summary>
	/// On a screen of another shape the canvas grows (the pixels stay whole): the game fills it, and a menu sits in
	/// the middle of it.
	/// </summary>
	private void FitScreen()
	{
		Vector2 size = GetViewportRect().Size;
		_backdrop.Size = size;
		if (_screen is not null and not GameScreen)
		{
			_screen.Position = ((size - new Vector2(MenuScreen.Width, MenuScreen.Height)) / 2).Floor();
		}
	}

	private ColorRect _backdrop = null!;

	private Vector2I _windowSize;

	/// <summary>The window's size is checked each frame (the canvas doesn't change with it, so nothing says when it does).</summary>
	public override void _Process(double delta)
	{
		if (GetWindow().Size != _windowSize)
		{
			FitCanvas();
		}
	}

	/// <summary>
	/// Fits the canvas to the window: the biggest whole number of screen pixels to a game pixel that fits 640 by 360,
	/// and the canvas as big as the window then holds (720 by 450 on a 16:10 screen at four or two to a pixel). The
	/// pixels stay square and whole; the game fills the window, but for a sliver less than one game pixel wide.
	/// </summary>
	private void FitCanvas()
	{
		Window window = GetWindow();
		Vector2I screen = _windowSize = window.Size;
		int scale = Math.Max(1, Math.Min(screen.X / (int)MenuScreen.Width, screen.Y / (int)MenuScreen.Height));
		var canvas = new Vector2I(Math.Max((int)MenuScreen.Width, screen.X / scale), Math.Max((int)MenuScreen.Height, screen.Y / scale));
		if (window.ContentScaleSize != canvas)
		{
			window.ContentScaleSize = canvas;
		}
	}

	private void ShowMainMenu()
	{
		var menu = new MainMenu();
		menu.PlayRequested += ShowModeSelect;
		menu.CharactersRequested += ShowCharacters;
		menu.SettingsRequested += ShowSettings;
		menu.TrackerRequested += () => ShowTracker();
		menu.PaintRequested += () => ShowPaint();
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
		screen.WoarldTreeRequested += ShowLobby;
		ShowMenu(screen);
	}

	// ---- Online: the Woarld Tree ---------------------------------------------------------------

	/// <summary>The connection for playing online; it outlives the screens.</summary>
	private NetLink _net = null!;

	/// <summary>The characters this player brought to the table, for the run's results.</summary>
	private IReadOnlyList<Character> _netParty = [];

	private void ShowLobby()
	{
		_raiding = false;
		_runFromMap = false;
		var lobby = new LobbyScreen
		{
			Link = _net,
			Repository = _characters,
			Content = _content,
			Dungeons = [.. _content.Themes.Values
				.OrderBy(theme => Array.IndexOf(DungeonOrder, theme.Id) is var at and >= 0 ? at : int.MaxValue)
				.ThenBy(theme => theme.Name, StringComparer.Ordinal)
				.Select(theme => (theme.Id, theme.Name))],
		};
		lobby.BackRequested += ShowModeSelect;
		lobby.ReplaysRequested += ShowReplays;
		lobby.StartRequested += themeId => HostNetRun(themeId, (ulong)DateTime.UtcNow.Ticks);
		ShowMenu(lobby);
	}

	/// <summary>The host seats everyone at the table and begins the run for them all.</summary>
	private void HostNetRun(string themeId, ulong seed)
	{
		(IReadOnlyList<HeroSetup> heroes, SeatMap? seats, string? problem) = NetTable.Seat(
			[.. _net.Players.Select(player => (player.Peer, _net.Parties.TryGetValue(player.Peer, out string? party) ? SavedRun.HeroesFromJson(party) : (IReadOnlyList<HeroSetup>)[]))]);
		if (seats is null)
		{
			GD.PushWarning($"Net: the table can't be seated: {problem}");
			return;
		}

		var run = new SavedRun { ThemeId = themeId, Seed = seed, Heroes = heroes };
		// Whose seats are whose, by name, so someone who drops out can take theirs up again.
		_netSeatsByName = _net.Players.ToDictionary(player => player.Name, player => (IReadOnlyList<int>)[.. seats.PeerBySeat.Where(entry => entry.Value == player.Peer).Select(entry => entry.Key)]);
		_net.StartRun(run.ToJson(), System.Text.Json.JsonSerializer.Serialize(seats.PeerBySeat));
	}

	/// <summary>On the host, the run being played online: its start and every move so far (what someone joining is sent).</summary>
	private SavedRun? _netRecord;

	private NetRun? _netRun;

	private bool _throwawaySaves;

	private string _saveFolder = "";

	/// <summary>On the host: the session being recorded, and where.</summary>
	private SessionRecord? _netSession;

	private string? _netSessionPath;

	private void ShowReplays()
	{
		var screen = new ReplayListScreen { Folder = ReplayFolder };
		screen.BackRequested += ShowLobby;
		screen.WatchRequested += path => WatchReplay(path, 0, autoPlay: false, clean: false, quitAtEnd: false, speed: 1);
		screen.VideoRequested += RecordVideo;
		ShowMenu(screen);
	}

	/// <summary>Watches a recorded session from a move.</summary>
	private void WatchReplay(string path, int fromMove, bool autoPlay, bool clean, bool quitAtEnd, float speed)
	{
		SessionRecord record;
		GameSession restored;
		try
		{
			record = SessionRecord.FromJson(File.ReadAllText(path));
			restored = record.PlayTo(_content, fromMove);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException)
		{
			GD.PushWarning($"Can't play the replay: {exception.Message}");
			if (quitAtEnd)
			{
				Sfx.QuitGame(this, 1);
			}
			else
			{
				ShowReplays();
			}

			return;
		}

		SavedRun run = record.RunSoFar();
		var game = new GameScreen
		{
			Setup = run.CreateSetup(_content),
			Content = _content,
			Restored = restored,
			Replay = new ReplayRun(record, run.Commands, fromMove, autoPlay, clean, quitAtEnd, speed),
		};
		game.ExitRequested += () =>
		{
			Engine.TimeScale = 1;
			ShowReplays();
		};
		game.ReplayJumpRequested += (move, speedNow) => WatchReplay(path, move, autoPlay: false, clean: game.IsClean, quitAtEnd: false, speed: speedNow);
		game.RunFinished += _ => { };
		Show(game);
	}

	/// <summary>Where recorded videos are written.</summary>
	private static string VideoFolder => ProjectSettings.GlobalizePath("user://videos");

	/// <summary>
	/// Records a replay to a video: another window of the game plays it from the start, clean of panels, and Godot's
	/// movie writer saves every frame (at a steady 30 a second, however long each takes to draw) to an AVI file.
	/// </summary>
	private void RecordVideo(string path)
	{
		Directory.CreateDirectory(VideoFolder);
		string video = Path.Combine(VideoFolder, Path.GetFileName(path).Replace(".woad-replay.json", ".avi"));
		int process = OS.CreateProcess(OS.GetExecutablePath(),
			["--path", ProjectSettings.GlobalizePath("res://"), "--write-movie", video, "--fixed-fps", "30", "--", "--replay", path, "--clean"]);
		if (process > 0)
		{
			OS.ShellOpen(VideoFolder);
		}
	}
	private string ReplayFolder => _throwawaySaves ? Path.Combine(_saveFolder, "replays") : ProjectSettings.GlobalizePath("user://replays");

	private string ReplayPath(string themeId) =>
		Path.Combine(ReplayFolder, $"{DateTime.Now:yyyy-MM-dd HHmm} {(_content.Themes.TryGetValue(themeId, out var theme) ? theme.Name : themeId)}.woad-replay.json");

	/// <summary>Writes the session so far (after every move, so a crash loses nothing).</summary>
	private void WriteReplay()
	{
		if (_netSession is null || _netSessionPath is null)
		{
			return;
		}

		try
		{
			Directory.CreateDirectory(ReplayFolder);
			File.WriteAllText(_netSessionPath, _netSession.ToJson());
		}
		catch (IOException exception)
		{
			GD.PushWarning($"Couldn't write the replay: {exception.Message}");
		}
	}

	/// <summary>On the host: each player's seats in the run, by their name.</summary>
	private Dictionary<string, IReadOnlyList<int>> _netSeatsByName = [];

	/// <summary>
	/// On the host: someone sat down at the table. With a run under way, they're sent it so far: one who dropped out
	/// (by their name) takes up their seats again; anyone else watches.
	/// </summary>
	private void OnNetPlayerArrived(int peer, string name)
	{
		if (_netRecord is null || _netRun is null || _screen is not GameScreen game || game.Net != _netRun)
		{
			return;
		}

		bool back = _netSeatsByName.TryGetValue(name, out IReadOnlyList<int>? seats) && seats.Count > 0;
		foreach (int seat in back ? seats! : [])
		{
			_netRun.Seats.Assign(seat, peer);
		}

		_netSession?.Note(back ? SessionNoteKind.Rejoined : SessionNoteKind.Watching, name, _netRecord.Commands.Count);
		WriteReplay();

		_net.SendRun(peer, _netRecord.ToJson(), System.Text.Json.JsonSerializer.Serialize(_netRun.Seats.PeerBySeat));
		game.NetSeatsChanged(name, back);
	}

	/// <summary>A run begins at the table (here, or from the host): everyone builds the same run from the same start.</summary>
	/// <param name="joining">Joining (or rejoining) a run under way: it's rebuilt from its moves so far.</param>
	private void StartNetRun(string runJson, string seatsJson, bool joining)
	{
		SavedRun run = SavedRun.FromJson(runJson);
		var seats = new SeatMap(System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, int>>(seatsJson) ?? []);
		if (_screen is LobbyScreen lobby)
		{
			_netParty = [.. lobby.Picked];
		}

		_raiding = false;
		_runFromMap = false;
		_themeId = run.ThemeId;
		RunSetup setup = run.CreateSetup(_content);
		GameSession? restored = null;
		if (joining)
		{
			try
			{
				restored = run.Restore(_content);
			}
			catch (InvalidDataException exception)
			{
				_net.Leave();
				ShowLobby();
				GD.PushWarning($"Net: couldn't take up the run: {exception.Message}");
				return;
			}
		}

		_netRun = new NetRun(_net, seats, run.Commands.Count);
		_netRecord = _net.IsHost ? run : null;
		var game = new GameScreen { Setup = setup, Content = _content, Net = _netRun, Restored = restored };
		if (_netRecord is { } record)
		{
			// The host records the whole session, to watch again: every move and who made it, and what was said.
			NetRun netRun = _netRun;
			_netSession = new SessionRecord { GameVersion = NetLink.GameVersion, Run = record.ToJson(), Players = [.. _net.Players.Select(player => player.Name)] };
			_netSessionPath = ReplayPath(record.ThemeId);
			SessionRecord session = _netSession;
			game.CommandPlayed += (command, state) =>
			{
				record.Record(command, state);
				int? peer = state.Heroes.FirstOrDefault(hero => hero.Id == command.HeroId) is { } hero ? netRun.Seats.PeerOf(state, hero) : null;
				session.Record(record, _net.Players.FirstOrDefault(player => player.Peer == peer)?.Name ?? "?");
				WriteReplay();
			};
			_net.MaySpeakAs = (peer, heroId) => netRun.Seats.Controls(peer, game.Session.State, heroId);
		}

		game.ExitRequested += () =>
		{
			_net.Leave();
			ShowLobby();
		};
		IReadOnlyList<Character> party = _netParty;
		game.RunFinished += results =>
		{
			WriteReplay();
			_netRecord = null;
			_netRun = null;
			_netSession = null;
			_net.MaySpeakAs = null;
			FinishRun(results, setup, party);
		};
		Show(game);
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

	/// <summary>A hero's hearts in the raid, and their full health.</summary>
	private (int Now, int Max) RaidHearts(Character hero) => (_raid!.HeartsOf(hero.Id, hero.MaxHearts(_content)), hero.MaxHearts(_content));

	/// <summary>A hero loses a heart out on the Woarld Map (never the last).</summary>
	private void WoundOnMap(Character hero)
	{
		int max = hero.MaxHearts(_content);
		_raid!.SetHearts(hero.Id, Math.Max(1, _raid.HeartsOf(hero.Id, max) - 1), max);
		SaveRaid(RaidLocation.Map);
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
	/// <param name="skirmish">A fight out on the Woarld Map instead of a dungeon.</param>
	private void StartRun(IReadOnlyList<Character> party, ulong seed, bool friendlyFire, string themeId, SkirmishSpec? skirmish = null)
	{
		_skirmish = skirmish;
		_friendlyFire = friendlyFire;
		_themeId = themeId;
		IReadOnlyList<HeroSetup> heroes = Party.ToHeroSetups(party);
		if (_raiding && _raid is not null)
		{
			// In a raid, heroes go in with the hearts they have left.
			// ...and with the bonds between them (a leaving companion is easier to talk round by a friend).
			RaidWorld bonds = LoadRaidWorld();
			heroes = [.. heroes.Select((hero, seat) => hero with
			{
				Hearts = _raid.HeartsOf(party[seat].Id, party[seat].MaxHearts(_content)),
				Bonds = Enumerable.Range(0, party.Count).Where(other => other != seat && bonds.Bond(party[seat].Id, party[other].Id) > 0)
					.ToDictionary(other => other, other => bonds.Bond(party[seat].Id, party[other].Id)),
			})];
			_activeRunFile = _raidStore.RunFile(_raid);
		}
		else
		{
			_activeRunFile = _runFile;
		}

		// The Graves, raided: as Father Ambrose's quests have left them.
		(int ritualRounds, bool cursed) = _raiding && skirmish is null && _content.GetTheme(themeId).Graveyard is not null ? LoadRaidWorld().GraveyardOmens() : (0, false);
		var save = new SavedRun
		{
			ThemeId = themeId, Seed = seed, FriendlyFire = friendlyFire, Heroes = heroes, MerchantGuildLevel = LoadRaidWorld().GuildLevel, Skirmish = skirmish, RitualRounds = ritualRounds, Cursed = cursed,
			UniquesClaimed = _raiding && _raid is not null ? [.. _raid.UniquesClaimed] : [],
		};
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
				// Left where it is, it would bar the way back into the raid for good: it's set aside, and
				// the party picks up where it stood before it (on the Woarld Map, or at the inn).
				file.SetAside();
				ShowRaidList($"The fight couldn't be continued, so it was set aside and the party carries on from before it. ({exception.Message})");
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
		_skirmish = run.Skirmish;
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
			Hearts = _raid is null || world is null ? null : RaidHearts,
			Wound = _raid is null || world is null ? null : WoundOnMap,
			World = world,
			Chest = world is null ? null : _raid?.Chest,
			Content = _content,
		};
		screen.LevelUpRequested += character =>
		{
			SaveMapProgress();
			SaveRaidWorld();
			ShowEditor(character, ShowWoarldMap);
		};
		screen.FightRequested += spec =>
		{
			SaveMapProgress();
			SaveRaidWorld();
			_runFromMap = true;
			StartRun(_woarldParty, (ulong)DateTime.UtcNow.Ticks, _friendlyFire, spec.ThemeId, spec);
		};
		screen.CharactersChanged += async heroes =>
		{
			SaveRaidWorld();
			SaveRaid();
			foreach (Character hero in heroes)
			{
				try
				{
					await _characters.SaveAsync(hero);
				}
				catch (IOException exception)
				{
					GD.PushWarning($"Couldn't save {hero.Name}: {exception.Message}");
				}
			}
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
		screen.LevelUpRequested += character =>
		{
			_woarldLeader = screen.Leader;
			SaveRaid();
			ShowEditor(character, ShowTavern);
		};
		Show(screen);
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

	/// <summary>
	/// After a fight out on the Woarld Map: if the party won, what they fought is gone from the map. A
	/// band that had a merchant cornered sets them free (the Guild is grateful); a toll post's company
	/// were bandits (the Law is grateful) or real guards (the Law remembers).
	/// </summary>
	private void SettleSkirmish(IReadOnlyList<HeroRunResult> results, IReadOnlyList<Character> party)
	{
		if (_skirmish is not { } fight || !_raiding)
		{
			_skirmish = null;
			return;
		}

		_skirmish = null;
		if (!results.Any(result => result.Status == Woad.Core.Model.HeroStatus.Escaped))
		{
			return;
		}

		RaidWorld world = LoadRaidWorld();
		if (results.All(result => result.Status != Woad.Core.Model.HeroStatus.Escaped || result.Retreated))
		{
			// Everyone left standing got away: the fight is still there, and the party back the way it came.
			if (fight.CameFrom is { } came)
			{
				world.Journey.Step(came.Opposite());
			}

			foreach (Character hero in party)
			{
				hero.ChangeStanding(cowards: 1);
				_ = _characters.SaveAsync(hero);
			}

			SaveRaidWorld();
			_woarldNews = $"You got away. {fight.Title.Split(':').Last().Trim()} is still out there. (Cowards +1)";
			return;
		}

		WoarldDangers dangers = world.Dangers(_content);
		var news = new List<string>();
		switch (fight.Kind)
		{
			case EncounterKind.Band when world.DefeatBand(_content, fight.BandId) is { } saved:
				foreach (Character hero in party)
				{
					hero.ChangeStanding(guild: 1);
				}

				news.Add($"{saved} is saved from the band! The Merchant Guild is grateful (Guild +1).");
				break;

			case EncounterKind.Toll when dangers.TollAt(fight.Spot) is { } toll:
				dangers.Tolls.Remove(toll);
				foreach (Character hero in party)
				{
					hero.ChangeStanding(law: toll.Bandits ? 3 : -2);
				}

				news.Add(toll.Bandits ? "They were bandits all along: the road is clear, and the Law is grateful (Law +3)." : "They were real guards. The Law won't forget this (Law -2).");
				break;

			case EncounterKind.Ambush:
				dangers.Ambushes.RemoveAll(ambush => ambush.Position == fight.Spot);
				break;
		}

		SaveRaidWorld();
		foreach (Character hero in party)
		{
			_ = _characters.SaveAsync(hero);
		}

		if (news.Count > 0)
		{
			_woarldNews = string.Join(" ", news);
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
			_raid.ClaimUniques(results, _content);
			if (setup.RitualRounds > 0)
			{
				LoadRaidWorld().SettleRitual(results.Any(result => result.RitualStopped), results.Any(result => result.PortalOpened));
				SaveRaidWorld();
			}
		}

		// Merchants brought out alive join the Guild, which rises a level for each, and remember who helped them.
		List<string> rescuedNames = results.SelectMany(result => result.MerchantsRescued.Select(rescued => rescued.Name)).Distinct().ToList();
		if (rescuedNames.Count > 0)
		{
			RaidWorld world = LoadRaidWorld();
			List<Guid> helpers = [.. results.Select(result => result.CharacterId).OfType<Guid>()];
			IReadOnlyList<string> wares = world.RescueMerchants(rescuedNames.Select(name => new RememberedMerchant(
				name,
				results.FirstOrDefault(result => result.MerchantsRescued.Any(rescued => rescued.Name == name && rescued.FreedByThisHero))?.CharacterId,
				helpers,
				world.Journey.Day)), _content);
			SaveRaidWorld();
			_woarldNews = $"{string.Join(" and ", rescuedNames)} joined the Merchant Guild, now level {world.GuildLevel}" +
						  (wares.Count > 0 ? $": new wares, {string.Join(", ", wares.Select(ware => _content.GetCard(ware).Name))}." : ".");
		}

		// After a dungeon: hired companions are paid by whoever they follow, win or lose, and a party that won grows closer.
		string settled = "";
		IReadOnlyList<(Character Companion, Character Owner)> unpaid = [];
		if (!setup.Skirmish)
		{
			Character? Of(int seat) => results.FirstOrDefault(result => result.Seat == seat) is { CharacterId: Guid id } ? party.FirstOrDefault(member => member.Id == id) : null;
			var news = new List<string>();
			foreach (HeroRunResult result in results)
			{
				if (Of(result.Seat) is not { IsCompanion: true } companion)
				{
					continue;
				}

				// Whatever they meant to do setting out, it's settled now: gone, or with the party again.
				companion.SetLeaving(false);
				if (result.Wage > 0)
				{
					companion.SetWage(result.Wage);
				}

				if (result.Departed)
				{
					// They walked out: back at the inn, to be found (and hired) there.
					Guid? raidLeader = _woarldParty.Count > 0 ? _woarldParty[Math.Clamp(_woarldLeader, 0, _woarldParty.Count - 1)].Id : null;
					_woarldParty.RemoveAll(member => member.Id == companion.Id);
					_woarldLeader = Math.Max(0, _woarldParty.FindIndex(member => member.Id == raidLeader));
					news.Add($"{companion.Name} walked out, back to the inn.");
				}
				else if (result.OwnerSeat is int now && now != setup.Heroes[result.Seat].OwnerSeat && Of(now) is { } leader)
				{
					// Paid or talked round by another player: they follow them now.
					FollowInParty(companion, leader);
				}

				_ = _characters.SaveAsync(companion);
			}

			List<(Character Member, Character? Owner)> went = [.. results.Where(result => result.CharacterId is not null && !result.Departed)
				.Select(result => (Member: Of(result.Seat), Owner: result.OwnerSeat is int owner ? Of(owner) : null))
				.Where(entry => entry.Member is not null).Select(entry => (entry.Member!, entry.Owner))];
			RaidWorld? bonds = _raiding ? LoadRaidWorld() : null;
			RunSettlement settlement = TavernServices.SettleRun(went, results.Any(result => result.Won), bonds);
			settled = string.Join(" ", news.Concat(settlement.News));
			// Out of a raid there's no party to bid, nor bonds: unpaid companions just carry on.
			unpaid = _raiding && _raid is not null ? settlement.Unpaid : [];
			if (bonds is not null)
			{
				SaveRaidWorld();
				SaveRaid();
			}

			foreach (Character member in went.SelectMany(entry => new[] { entry.Member, entry.Owner }).OfType<Character>().Distinct())
			{
				_ = _characters.SaveAsync(member);
			}
		}

		if (setup.Skirmish)
		{
			SettleSkirmish(results, party);
		}
		else if (results.Any(result => result.Won))
		{
			IReadOnlyList<WoarldPlace> opened = (_raiding ? LoadRaidWorld().Journey : LoadJourney()).MarkBeaten(setup.ThemeId);
			SaveMapProgress();
			if (opened.Count > 0)
			{
				_woarldNews = $"{string.Join(" and ", opened.Select(place => place.Name))} has opened! Find it on the Woarld Map.";
			}
		}

		Music.Play(results.Any(result => result.Won) ? "victory" : "defeat", loop: false);
		var screen = new ResultsScreen { Rows = rows, Content = _content, SaveProblem = saveProblem, Settled = settled };
		void MoveOn()
		{
			if (_net.IsOnline)
			{
				// Back to the table, for another.
				ShowLobby();
			}
			else if (_runFromMap)
			{
				ShowWoarldMap();
			}
			else
			{
				ShowRunSetup();
			}
		}

		screen.DoneRequested += () =>
		{
			// Companions who went unpaid go up for offers from the rest of the party before it moves on.
			if (unpaid.Count == 0 || _raid is null)
			{
				MoveOn();
				return;
			}

			var auction = new WageAuctionScreen { Unpaid = unpaid, Party = [.. _woarldParty], Leader = _raid.Leader, World = LoadRaidWorld() };
			auction.Settled += (companion, result) =>
			{
				if (result.NewLeader is { } leader)
				{
					FollowInParty(companion, leader);
				}

				_ = _characters.SaveAsync(companion);
				SaveRaid();
			};
			auction.DoneRequested += MoveOn;
			Show(auction);
		};
		Show(screen);
	}

	/// <summary>Seats a companion right after the player they follow now (a companion belongs to the nearest player seated before it).</summary>
	private void FollowInParty(Character companion, Character leader)
	{
		Guid? raidLeader = _woarldParty.Count > 0 ? _woarldParty[Math.Clamp(_woarldLeader, 0, _woarldParty.Count - 1)].Id : null;
		if (!_woarldParty.Any(member => member.Id == leader.Id) || _woarldParty.RemoveAll(member => member.Id == companion.Id) == 0)
		{
			return;
		}

		_woarldParty.Insert(_woarldParty.FindIndex(member => member.Id == leader.Id) + 1, companion);
		_woarldLeader = Math.Max(0, _woarldParty.FindIndex(member => member.Id == raidLeader));
	}
}
