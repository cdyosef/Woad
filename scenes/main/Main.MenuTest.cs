using Godot;
using Woad.Core.Characters;
using Woad.Core.Grid;
using Woad.Core.Woarld;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Saves;
using Woad.Core.Tavern;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;
using Woad.Scenes.Tavern;
using Woad.Scenes.Woarld;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --menutest</c>): creates two characters and a companion, goes through Select
/// Mode and the Dungeon Run list, seats them, answers Begin Raid?, starts a run, orders the
/// companion, leaves the run and deletes a character, all with synthetic clicks and typing against
/// a throwaway save folder.
/// </summary>
public partial class Main
{
    private async void RunMenuTest(string saveFolder)
    {
        try
        {
            MainMenu menu = await Screen<MainMenu>();
            Check(Music.Current == "title", "the menus play the title music");
            await Click(this, menu.CharactersButton);

            CharacterListScreen list = await LoadedList();
            Check(list.CharacterCount == 0, "a fresh save folder has no characters");

            await Click(this, list.NewButton);
            CharacterEditor editor = await Screen<CharacterEditor>();
            await Click(this, editor.NameEdit);
            await Type(this, "Morgan");
            Check(editor.NameEdit.Text == "Morgan", "typing fills in the name");
            Check(!editor.SaveButton.Disabled, "a valid name enables Create");
            await Click(this, editor.PortraitButton(2));
            await Click(this, editor.StyleButton(0, next: true));
            Check(editor.Appearance.Look == "knight" && editor.StyleButton(3, next: true).Disabled, "the next look is the Knight, whose face is hidden (no skin color)");
            await Click(this, editor.StyleButton(0, next: true));
            await Click(this, editor.StyleButton(1, next: true));
            await Click(this, editor.StyleButton(3, next: false));
            Check(editor.Appearance is { Look: "ranger", Main: "ember", Skin: "deep" }, "the arrows pick the look, main color and skin");
            string[] headings = ["HEARTS", "WEND", "RUMBLE", "DEFENSE", "OD HAND", "LUCK", "MAGIC"];
            bool explained = true;
            for (int row = 0; row < headings.Length; row++)
            {
                Vector2 statName = editor.GetGlobalRect().Position + new Vector2(30, 164 + row * 19 + 8);
                GetViewport().PushInput(new InputEventMouseMotion { Position = statName, GlobalPosition = statName }, inLocalCoords: true);
                await NextFrame(this);
                explained &= editor.PreviewText.StartsWith(headings[row]);
                await NextFrame(this);
                await NextFrame(this);
            }

            Check(explained, "hovering each stat's symbol or name explains it");

            GetViewport().PushInput(new InputEventMouseMotion { Position = new Vector2(600, 20), GlobalPosition = new Vector2(600, 20) }, inLocalCoords: true);
            await NextFrame(this);
            Check(!editor.PreviewText.StartsWith("LUCK"),"and moving away brings back the summary");
            // A rank of Magic asks which basic spell it teaches; taking it back forgets the spell.
            await Click(this, editor.PlusButton(StatKind.Magic));
            Check(editor.SpellPicker.Visible && editor.SpellPicker.ButtonFor("hurl_fire") is not null, "raising Magic asks for a basic spell");
            await Click(this, editor.SpellPicker.ButtonFor("hurl_fire")!);
            Check(editor.PointsLeft == 4 && editor.PreviewText.Contains("Hurl Fire"), "picking one buys the rank (2 points) and shows the spell");
            await Click(this, editor.MinusButton(StatKind.Magic));
            Check(editor.PointsLeft == 6 && !editor.PreviewText.Contains("Hurl Fire"), "taking the rank back forgets the spell");
            await Click(this, editor.PlusButton(StatKind.Hearts));
            await Click(this, editor.PlusButton(StatKind.Hearts));
            await Click(this, editor.PlusButton(StatKind.AttackDice));
            Check(editor.PointsLeft == 1, "point-buy spends 1 + 1 + 3 of 6 points");
            Check(editor.PlusButton(StatKind.AttackDice).Disabled, "level 1 allows only one extra Rumble die");
            Check(editor.PlusButton(StatKind.DefenseDice).Disabled, "you can't spend points you don't have");
            await Click(this, editor.SaveButton);

            list = await LoadedList();
            Check(list.CharacterCount == 1, "the new character is listed");
            Check(Directory.GetFiles(saveFolder, "*.json").Length == 1, "the character is saved to disk");

            await Click(this, list.Row(0));
            await Click(this, list.EditButton);
            editor = await Screen<CharacterEditor>();
            Check(editor.Appearance is { Look: "ranger", Main: "ember", Skin: "deep" }, "editing opens with the character's look");
            await Click(this, editor.StyleButton(0, next: true));
            await Click(this, editor.StyleButton(0, next: true));
            await Click(this, editor.StyleButton(4, next: true));
            Check(editor.Appearance is { Look: "mage", Hair: "black" }, "an existing character can change look and hair");
            await Click(this, editor.SaveButton);
            list = await LoadedList();
            string saved = File.ReadAllText(Directory.GetFiles(saveFolder, "*.json").Single());
            Check(saved.Contains("\"look\": \"mage\"") && saved.Contains("\"hair\": \"black\"") && saved.Contains("\"main\": \"ember\""), "saving the edit writes the new look to disk");

            await Click(this, list.NewButton);
            editor = await Screen<CharacterEditor>();
            await Click(this, editor.NameEdit);
            await Type(this, "Wren");
            await Click(this, editor.SaveButton);
            list = await LoadedList();
            Check(list.CharacterCount == 2, "a second character is listed");

            await Click(this, list.NewButton);
            editor = await Screen<CharacterEditor>();
            await Click(this, editor.NameEdit);
            await Type(this, "Pip");
            await Click(this, editor.CompanionButton);
            await Click(this, editor.SaveButton);
            list = await LoadedList();
            Check(list.CharacterCount == 3, "a companion is listed too");

            await Click(this, list.BackButton);
            menu = await Screen<MainMenu>();
            await Click(this, menu.SettingsButton);
            SettingsScreen settings = await Screen<SettingsScreen>();
            float? loudMusicDb = AudioSettings.BusVolumeDb(AudioSettings.MusicBus);
            await ClickSliderAt(settings.MusicSlider, 0.25f);
            Check(AudioSettings.MusicVolume is > 0.1f and < 0.4f, "clicking a quarter of the way along the music slider turns the music down");
            Check(AudioSettings.BusVolumeDb(AudioSettings.MusicBus) < loudMusicDb, "the music bus gets quieter");
            await ClickSliderAt(settings.SoundSlider, 0);
            Check(AudioSettings.SfxVolume == 0 && AudioSettings.BusVolumeDb(AudioSettings.SfxBus) is null, "the sound slider's far left mutes sound effects");
            float savedMusic = AudioSettings.MusicVolume;
            await Click(this, settings.BackButton);
            menu = await Screen<MainMenu>();
            string settingsFile = Path.Combine(saveFolder, "settings.cfg");
            AudioSettings.SetMusicVolume(1);
            AudioSettings.Load(settingsFile);
            Check(AudioSettings.MusicVolume == savedMusic && AudioSettings.SfxVolume == 0, "leaving Settings saves the volumes, and they load again");

            await Click(this, menu.PlayButton);
            ModeSelectScreen modes = await Screen<ModeSelectScreen>();
            Check(!modes.DungeonRunButton.Disabled, "Play opens Select Mode, with Dungeon Run ready");
            Check(!modes.RaidingPartyButton.Disabled && new[] { modes.FrontierRaidButton, modes.BalladButton, modes.MapEditorButton, modes.WoarldTreeButton }.All(button => button.Disabled),
                "Raiding Party is ready too; the modes still to come are greyed out");
            await Click(this, modes.BackButton);
            menu = await Screen<MainMenu>();
            await Click(this, menu.PlayButton);
            modes = await Screen<ModeSelectScreen>();
            await Click(this, modes.DungeonRunButton);
            DungeonSelectScreen dungeons = await Screen<DungeonSelectScreen>();
            Check(dungeons.ContinueButton.Disabled && !dungeons.WoarldMapButton.Disabled, "with no saved run, Continue is greyed out; the Woarld Map is open");
            Check(dungeons.DungeonButton(0).Text == "The Emerald Keep" && dungeons.DungeonButton(1).Text == "The Sunken Temple", "the Dungeon Run list starts with the Emerald Keep, then the Sunken Temple");
            await Click(this, dungeons.DungeonButton(1));
            RunSetupScreen setup = await Screen<RunSetupScreen>();
            Check(setup.ThemeId == "sunken_temple" && setup.DungeonLabel.Text.EndsWith("The Sunken Temple"), "picking the Sunken Temple takes the party there");
            await WaitFor(this, () => setup.IsLoaded, "the roster to load");
            await Click(this, setup.BackButton);
            dungeons = await Screen<DungeonSelectScreen>();
            await Click(this, dungeons.DungeonButton(0));
            setup = await Screen<RunSetupScreen>();
            await WaitFor(this, () => setup.IsLoaded, "the roster to load");
            Check(setup.StartButton.Disabled, "Start needs at least one player");
            await Click(this, setup.RosterRow(2));
            Check(setup.PartySize == 0, "a companion can't be seated before a player");
            await Click(this, setup.RosterRow(1));
            await Click(this, setup.RosterRow(2));
            await Click(this, setup.RosterRow(0));
            Check(setup.PartySize == 3, "clicking characters seats them");
            Check(setup.FriendlyFire && setup.FriendlyFireButton.Text.EndsWith("on"), "friendly fire starts on");
            await Click(this, setup.FriendlyFireButton);
            Check(!setup.FriendlyFire && setup.FriendlyFireButton.Text.EndsWith("off"), "the Friendly fire button turns it off");
            await Click(this, setup.FriendlyFireButton);
            Check(setup.FriendlyFire, "and back on");
            Check(setup.ThemeId == "emerald_keep" && setup.DungeonLabel.Text.EndsWith("The Emerald Keep"), "and back to the list for the Emerald Keep");

            await Click(this, setup.StartButton);
            Check(setup.BeginRaid.Visible, "Start asks Begin Raid?");
            await Click(this, setup.BeginRaid.ChillButton);
            Check(!setup.BeginRaid.Visible && _screen == setup && setup.PartySize == 3, "Chill closes the question and keeps the party");
            await Click(this, setup.StartButton);
            await Click(this, setup.BeginRaid.RaidButton);
            GameScreen game = await Screen<GameScreen>();
            await game.WaitUntilIdle();
            string[] names = [.. game.Session.State.Heroes.Select(hero => hero.Name)];
            Check(names.SequenceEqual(["Wren", "Pip", "Morgan"]), "seat order follows the order characters were picked");
            var wren = game.Session.State.Heroes[0];
            var pip = game.Session.State.Heroes[1];
            var morgan = game.Session.State.Heroes[2];
            Check(pip.OwnerId == wren.Id, "a companion belongs to the player seated before it");
            Check(game.Session.State.FriendlyFire, "the run has friendly fire on");

            Check(game.OrdersButton.Visible, "a player with a companion gets the Orders button");
            await Click(this, game.OrdersButton);
            await Click(this, game.OrderButton(0, CompanionOrder.Hold));
            await game.WaitUntilIdle();
            Check(pip.Order == CompanionOrder.Hold, "the Hold button orders the companion to hold");
            var post = pip.Position;
            await Click(this, game.EndTurnButton);
            await WaitFor(this, () => game.Session.State.ActiveHero == morgan && !game.IsBusy, "the companion's turn to play out");
            Check(pip.Position == post, "a holding companion stays put on its own turn");
            Check(!game.OrdersButton.Visible, "a player without companions has no Orders button");
            Check(morgan.MaxHearts == Woad.Core.Rules.GameRules.HeroBaseStats.Hearts + 2 && morgan.EffectiveStats.AttackDice == 2 && morgan.EffectiveStats.Weapon == 1, "the hero has the character's stats plus gear");
            Check(morgan.Portrait == "moss", "the hero has the chosen portrait");
            Check(morgan.Appearance is { Look: "mage", Main: "ember", Hair: "black" }, "the hero has the chosen look");
            Check(Music.Current == "dungeon", "a run plays the dungeon music");

            string where = string.Join(" ", game.Session.State.Heroes.Select(hero => $"{hero.Name}@{hero.Position}:{hero.Hearts}"));
            (int round, int active) = (game.Session.State.Round, game.Session.State.ActiveHero!.Id);
            await Click(this, game.MenuButton);
            await Click(this, game.MenuButton);
            dungeons = await Screen<DungeonSelectScreen>();
            Check(true, "the Menu button leaves the run after confirming");
            Check(Music.Current == "title", "leaving the run brings back the title music");
            Check(!dungeons.ContinueButton.Disabled && dungeons.ContinueButton.TooltipText.StartsWith("The Emerald Keep: Wren, Pip, Morgan"), "the run was saved, and Continue offers it");

            await Click(this, dungeons.ContinueButton);
            game = await Screen<GameScreen>();
            await game.WaitUntilIdle();
            Check(string.Join(" ", game.Session.State.Heroes.Select(hero => $"{hero.Name}@{hero.Position}:{hero.Hearts}")) == where
                && game.Session.State.Round == round && game.Session.State.ActiveHero!.Id == active, "Continue picks the run up exactly where it was left");
            Check(game.Session.State.Heroes[1].Order == CompanionOrder.Hold && game.Session.State.FriendlyFire, "orders and friendly fire carry over");
            await Click(this, game.MenuButton);
            await Click(this, game.MenuButton);
            dungeons = await Screen<DungeonSelectScreen>();

            await Click(this, dungeons.DungeonButton(0));
            setup = await Screen<RunSetupScreen>();
            await WaitFor(this, () => setup.IsLoaded, "the roster to load");
            await NextFrame(this);
            await Click(this, setup.RosterRow(0));
            await Click(this, setup.StartButton);
            Check(setup.BeginRaid.DetailText.Contains("replaces your saved run"), "starting another run warns that it replaces the saved one");
            await Click(this, setup.BeginRaid.ChillButton);
            await Click(this, setup.BackButton);
            dungeons = await Screen<DungeonSelectScreen>();

            await RunWoarldMapTest(dungeons);
            dungeons = await Screen<DungeonSelectScreen>();
            await Click(this, dungeons.BackButton);
            modes = await Screen<ModeSelectScreen>();
            await RunTavernTest(modes);
            modes = await Screen<ModeSelectScreen>();
            await Click(this, modes.BackButton);
            menu = await Screen<MainMenu>();
            await Click(this, menu.CharactersButton);
            list = await LoadedList();
            Check(list.CharacterCount == 4, "the hired recruit is in the roster");
            await Click(this, list.Row(1));
            await Click(this, list.DeleteButton);
            Check(list.CharacterCount == 4, "the first Delete click only asks for confirmation");
            await Click(this, list.DeleteButton);
            await WaitFor(this, () => list.IsLoaded && list.CharacterCount == 3, "the list to reload after deleting");
            Check(Directory.GetFiles(saveFolder, "*.json").Length == 3, "deleting removes the save file");

            GD.Print("Menu test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Menu test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
        finally
        {
            if (Directory.Exists(saveFolder))
            {
                Directory.Delete(saveFolder, recursive: true);
            }
        }
    }

    /// <summary>
    /// Sets out on the Woarld Map with Wren, Pip and Morgan: walks by click, changes leader, rests,
    /// follows the road to the Emerald Keep, raids it, leaves the run and comes back to the map.
    /// </summary>
    private async Task RunWoarldMapTest(DungeonSelectScreen dungeons)
    {
        await Click(this, dungeons.WoarldMapButton);
        RunSetupScreen setup = await Screen<RunSetupScreen>();
        await WaitFor(this, () => setup.IsLoaded, "the roster to load");
        await NextFrame(this);
        Check(setup.DungeonLabel.Text == "Off to: The Woarld Map", "the party screen says the party sets out on the Woarld Map");
        await Click(this, setup.RosterRow(1));
        await Click(this, setup.RosterRow(2));
        await Click(this, setup.RosterRow(0));
        await Click(this, setup.StartButton);
        Check(!setup.BeginRaid.DetailText.Contains("replaces"), "setting out on the map doesn't replace the saved run");
        await Click(this, setup.BeginRaid.RaidButton);

        WoarldMapScreen map = await Screen<WoarldMapScreen>();
        WoarldJourney journey = map.Journey;
        GridPosition inn = journey.Map.Start;
        Check(journey.Leader == inn && journey.PlaceHere?.Id == "waylay_inn", "the party sets out from The Waylay Inn");
        Check(map.LeaderButton.Text == "Leader: Wren" && journey.Trail.Count == 2, "Wren leads, with two following");
        Check(map.EnterButton.Visible && map.EnterButton.Text == "Enter The Waylay Inn", "at the inn, the Enter button offers it");
        await Click(this, map.EnterButton);
        Check(map.Message.Contains("Raiding Party"), "in a Dungeon Run, the inn says it opens in Raiding Party");

        // A click two tiles east walks there; the others fall in behind.
        await ClickAt(this, map.View.TileCenter(inn with { X = inn.X + 2 }));
        await WaitFor(this, () => !map.IsWalking, "the party to walk");
        Check(journey.Leader == inn with { X = inn.X + 2 } && journey.Trail[0] == inn with { X = inn.X + 1 } && journey.Trail[1] == inn, "clicking walks the party there in a line");
        int twoTiles = journey.StepCost(inn with { X = inn.X + 1 }) + journey.StepCost(inn with { X = inn.X + 2 });
        Check(journey.TotalSteps == twoTiles && !map.EnterButton.Visible, "two tiles walked (by their ground's time), and nothing to enter here");

        await Click(this, map.LeaderButton);
        Check(map.LeaderButton.Text == "Leader: Morgan", "the Leader button passes the lead to the next player, skipping the companion");
        await Click(this, map.RestButton);
        Check(journey.TotalSteps == twoTiles + WoarldJourney.RestSteps && map.Message.StartsWith("Rested"), "resting rolls for everyone and takes 50 steps");

        // Back along the road east, over the bridge and north to the Keep.
        Direction[] road = [.. Enumerable.Repeat(Direction.East, 14), .. Enumerable.Repeat(Direction.North, 5), .. Enumerable.Repeat(Direction.East, 6), Direction.North];
        map.Walk(road);
        await WaitFor(this, () => !map.IsWalking, "the walk to the Keep");
        Check(journey.PlaceHere?.Id == "emerald_keep" && map.EnterButton.Text == "Raid The Emerald Keep", "the road leads to the Emerald Keep");
        await Click(this, map.EnterButton);
        Check(map.BeginRaid.Visible && map.BeginRaid.DetailText.Contains("replaces your saved run"), "raiding asks Begin Raid?, warning of the saved run");
        await Click(this, map.BeginRaid.RaidButton);

        GameScreen game = await Screen<GameScreen>();
        await game.WaitUntilIdle();
        Check(game.Session.State.ThemeId == "emerald_keep" && game.Session.State.Heroes.Count == 3, "the party raids the Keep");
        Check(journey.Day == 2, "a raid takes the rest of the day");
        await Click(this, game.MenuButton);
        await Click(this, game.MenuButton);
        map = await Screen<WoarldMapScreen>();
        Check(map.Journey.PlaceHere?.Id == "emerald_keep" && map.LeaderButton.Text == "Leader: Morgan", "leaving the raid goes back to the map, at the Keep, Morgan still leading");
        await Click(this, map.BackButton);
        Check(File.Exists(_journeyPath) && File.ReadAllText(_journeyPath).Contains("\"day\":2"), "the journey is saved");
    }

    /// <summary>
    /// A Raiding Party of Wren, Pip and Morgan in The Waylay Inn: talks to everyone there (drinks,
    /// a meal To-Go, a round for the bar, a rumor, a purchase, a game of Bones, a hire, a swap),
    /// then goes out to the Woarld Map, rests on what they ate, and comes back in.
    /// </summary>
    private async Task RunTavernTest(ModeSelectScreen modes)
    {
        await Click(this, modes.RaidingPartyButton);
        RaidListScreen raids = await Screen<RaidListScreen>();
        Check(raids.RaidCount == 0, "Raiding Party opens the raids list, empty at first");
        await Click(this, raids.NewRaidButton);
        RunSetupScreen setup = await Screen<RunSetupScreen>();
        await WaitFor(this, () => setup.IsLoaded, "the roster to load");
        await NextFrame(this);
        Check(setup.DungeonLabel.Text == "Off to: The Waylay Inn", "a Raiding Party sets out for The Waylay Inn");
        await Click(this, setup.RosterRow(1));
        await Click(this, setup.RosterRow(2));
        await Click(this, setup.RosterRow(0));
        await Click(this, setup.StartButton);
        await Click(this, setup.BeginRaid.RaidButton);

        TavernScreen inn = await Screen<TavernScreen>();
        Character wren = inn.Party[0];
        Check(wren.Name == "Wren" && inn.LeaderButton.Text == "Leader: Wren" && inn.Visit.Leader == inn.Tavern.Start, "the party comes in at the inn's door, Wren leading");
        Check(_raid?.Name == "Wren's Raid" && _raidStore.LoadAll().Raids.Count == 1, "the raid is saved as Wren's Raid");
        wren.EarnGold(60);
        int full = wren.MaxHearts(_content);
        _raid.SetHearts(wren.Id, 1, full);

        TavernNpc Npc(NpcRole role) => inn.Tavern.Npcs.First(npc => npc.Role == role);
        async Task TalkTo(TavernNpc npc)
        {
            inn.Clicked(npc.Position);
            await WaitFor(this, () => inn.TalkingTo == npc, $"the walk to {npc.Name}");
        }

        await ClickAt(this, inn.View.TileCenter(new GridPosition(5, 5)));
        await WaitFor(this, () => !inn.IsWalking, "the walk into the room");
        TavernNpc bartender = Npc(NpcRole.Bartender);
        await ClickAt(this, inn.View.TileCenter(bartender.Position));
        await WaitFor(this, () => inn.TalkingTo == bartender, "the walk to the bar");
        Check(inn.Visit.NpcInReach == bartender, "clicking the bartender walks the party up to the bar to talk");
        await Click(this, inn.ListButton("Drink", 0)!);
        Check(wren.Gold == 59 && inn.World.Fed(wren.Id).Bonus == 2, "a Woad Ale costs 1 gold and boosts Wren's next Rest");
        await Click(this, inn.ListButton("To-Go", 2)!);
        Check(inn.World.Provisions(wren.Id).SequenceEqual(["hearth_stew"]), "a Hearth Stew is packed To-Go");
        await Click(this, inn.ListButton("Buy", 6)!);
        Check(inn.World.Reputation(inn.Tavern.Id) == 1 && wren.Gold == 46, "a round for the whole bar raises the inn's reputation");
        await Click(this, inn.RumorButton);
        Check(inn.World.Journey.IsRevealed(new GridPosition(33, 26)), "the bartender's rumor marks the Sunken Temple on the map");
        await Click(this, inn.ByeButton);

        await TalkTo(Npc(NpcRole.Merchant));
        await Click(this, inn.BuyButton("Bomb")!);
        Check(wren.Gold == 44 && wren.Belt.Contains("bomb"), "the merchant sells Wren a bomb, his cheapest ware");
        await Click(this, inn.ByeButton);

        await TalkTo(Npc(NpcRole.Gambler));
        Check(inn.ListRows.First().StartsWith("Bones: two dice each") && !inn.ListButton("20 gold")!.Disabled, "the Bones rules get a line of their own, and Wren can bet 20");
        await Click(this, inn.PayerButton);
        Check(inn.Payer.Name == "Pip" && inn.ListButton("1 gold")!.Disabled && inn.ListButton("1 gold")!.TooltipText == "Pip has 0 gold.", "Pip has no gold, so the bets are greyed out, saying why");
        await Click(this, inn.PayerButton);
        await Click(this, inn.PayerButton);
        await Click(this, inn.ListButton("1 gold")!);
        Check(inn.IsRolling && inn.BonesDice.Visible && inn.ListButton("20 gold")!.Disabled, "the Bones dice tumble as in a fight, bets held back meanwhile");
        await WaitFor(this, () => !inn.IsRolling, "the Bones dice to land");
        Check(wren.Gold is 43 or 44 or 45 && (inn.DialogText.Contains("rolls") || inn.DialogText.Contains("tie")), "a game of Bones for 1 gold");
        int gold = wren.Gold;
        await Click(this, inn.ByeButton);

        await TalkTo(Npc(NpcRole.Recruiter));
        await Click(this, inn.ListButton("Hire")!);
        Check(inn.Party.Count == 4 && inn.Party[2].IsCompanion && inn.Party[3].Name == "Morgan" && wren.Gold == gold - 10,
            "hiring a recruit for 10 gold seats them as Wren's companion, after Pip");
        await Click(this, inn.ByeButton);

        await TalkTo(Npc(NpcRole.Innkeeper));
        gold = wren.Gold;
        await Click(this, inn.ListButton("Sleep the night")!);
        Check(inn.World.Journey.Day == 2 && _raid.HeartsOf(wren.Id, full) == 2 && wren.Gold == gold - 8,
            "sleeping the night costs a room each, passes a day, and heals Wren a heart");
        await Click(this, inn.ListButton("Room", 5)!);
        Check(inn.Party.Count == 3 && inn.Party.All(member => member.Name != "Morgan") && _raid.Rooms.Count == 1 && wren.Gold == gold - 10,
            "a room upstairs for Morgan costs 2 gold");
        await Click(this, inn.ListButton("Join", 6)!);
        Check(inn.Party.Count == 4 && inn.Party[3].Name == "Morgan", "and Morgan comes back down to join");
        await Click(this, inn.ByeButton);

        await Click(this, inn.DoorButton);
        WoarldMapScreen map = await Screen<WoarldMapScreen>();
        Check(map.Journey.PlaceHere?.Id == "waylay_inn" && map.Party.Count == 4, "the door leads out onto the Woarld Map, at the inn");
        await Click(this, map.RestButton);
        Check(map.Message.StartsWith("Rested: Wren") && map.Message.Contains("+2") && _raidWorld!.Provisions(wren.Id).Count == 0 && _raidWorld.Fed(wren.Id).IsNone,
            "resting eats Wren's stew and drinks the ale's +2");
        await Click(this, map.EnterButton);
        inn = await Screen<TavernScreen>();
        Check(inn.Party.Count == 4, "the inn on the map leads back inside");
        await Click(this, inn.DoorButton);
        map = await Screen<WoarldMapScreen>();
        int hearts = _raid.HeartsOf(wren.Id, full);
        Check(map.Message.Length > 0 && hearts >= 2, "resting healed Wren by her roll");
        await Click(this, map.BackButton);
        Check(File.ReadAllText(_raidWorldPath).Contains("\"waylay_inn\":1"), "the shared Woarld is saved, with the inn's reputation");

        raids = await Screen<RaidListScreen>();
        Check(raids.RaidCount == 1, "the raids list shows Wren's Raid");
        await Click(this, raids.ContinueButton(0));
        map = await Screen<WoarldMapScreen>();
        Check(map.Party.Count == 4 && map.Journey.PlaceHere?.Id == "waylay_inn" && map.Journey.Day == 2 && _raid.HeartsOf(wren.Id, full) == hearts,
            "Continue picks the raid up on the map, at the inn on day 2, hearts and all");

        // Off down the road to raid the Keep (a quiet road, today), and back out mid-dungeon.
        LoadRaidWorld().Dangers(_content).Calm = true;
        map.Walk([.. Enumerable.Repeat(Direction.East, 16), .. Enumerable.Repeat(Direction.North, 5), .. Enumerable.Repeat(Direction.East, 6), Direction.North]);
        await WaitFor(this, () => !map.IsWalking, "the walk to the Keep");
        await Click(this, map.EnterButton);
        Check(!map.BeginRaid.DetailText.Contains("replaces"), "a raid's dungeon doesn't touch the Dungeon Run's saved run");
        await Click(this, map.BeginRaid.RaidButton);
        GameScreen game = await Screen<GameScreen>();
        await game.WaitUntilIdle();
        Check(game.Session.State.Heroes[0].Name == "Wren" && game.Session.State.Heroes[0].Hearts == hearts, "Wren goes into the Keep wounded, with the hearts she had");
        await Click(this, game.MenuButton);
        await Click(this, game.MenuButton);
        raids = await Screen<RaidListScreen>();
        Check(_raidStore.RunFile(_raid).Exists, "leaving the dungeon keeps it, saved with the raid");
        await Click(this, raids.ContinueButton(0));
        game = await Screen<GameScreen>();
        await game.WaitUntilIdle();
        Check(game.Session.State.ThemeId == "emerald_keep" && game.Session.State.Heroes[0].Hearts == hearts, "Continue goes back into the Keep");
        await Click(this, game.MenuButton);
        await Click(this, game.MenuButton);
        raids = await Screen<RaidListScreen>();
        await Click(this, raids.BackButton);
    }

    private async Task<T> Screen<T>() where T : Control
    {
        await WaitFor(this, () => _screen is T && _screen.IsNodeReady(), typeof(T).Name);
        await NextFrame(this);
        return (T)_screen!;
    }

    /// <summary>Clicks a slider's track part of the way along: 0 is the far left, 1 the far right.</summary>
    private Task ClickSliderAt(HSlider slider, float fraction)
    {
        Rect2 rect = slider.GetGlobalRect();
        return ClickAt(this, new Vector2(rect.Position.X + 1 + fraction * (rect.Size.X - 2), rect.GetCenter().Y));
    }

    private async Task<CharacterListScreen> LoadedList()
    {
        CharacterListScreen list = await Screen<CharacterListScreen>();
        await WaitFor(this, () => list.IsLoaded, "the character list to load");
        return list;
    }
}
