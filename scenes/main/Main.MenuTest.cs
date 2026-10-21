using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;
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
            string[] headings = ["HEARTS", "WEND", "RUMBLE", "DEFENSE", "OD HAND", "LUCK"];
            bool explained = true;
            for (int row = 0; row < headings.Length; row++)
            {
                Vector2 statName = editor.GetGlobalRect().Position + new Vector2(30, 164 + row * 21 + 8);
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
            Check(new[] { modes.RaidingPartyButton, modes.FrontierRaidButton, modes.BalladButton, modes.MapEditorButton, modes.WoarldTreeButton }.All(button => button.Disabled),
                "the modes still to come are greyed out");
            await Click(this, modes.BackButton);
            menu = await Screen<MainMenu>();
            await Click(this, menu.PlayButton);
            modes = await Screen<ModeSelectScreen>();
            await Click(this, modes.DungeonRunButton);
            DungeonSelectScreen dungeons = await Screen<DungeonSelectScreen>();
            Check(dungeons.ContinueButton.Disabled && dungeons.WoarldMapButton.Disabled, "with no saved run, Continue and the Woarld Map are greyed out");
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
            await Click(this, setup.FriendlyFireButton);
            Check(setup.FriendlyFire && setup.FriendlyFireButton.Text.EndsWith("on"), "the Friendly fire button turns it on");
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

            await Click(this, game.MenuButton);
            await Click(this, game.MenuButton);
            setup = await Screen<RunSetupScreen>();
            Check(true, "the Menu button leaves the run after confirming");
            Check(Music.Current == "title", "leaving the run brings back the title music");

            await WaitFor(this, () => setup.IsLoaded, "the roster to load");
            await Click(this, setup.BackButton);
            dungeons = await Screen<DungeonSelectScreen>();
            await Click(this, dungeons.BackButton);
            modes = await Screen<ModeSelectScreen>();
            await Click(this, modes.BackButton);
            menu = await Screen<MainMenu>();
            await Click(this, menu.CharactersButton);
            list = await LoadedList();
            await Click(this, list.Row(1));
            await Click(this, list.DeleteButton);
            Check(list.CharacterCount == 3, "the first Delete click only asks for confirmation");
            await Click(this, list.DeleteButton);
            await WaitFor(this, () => list.IsLoaded && list.CharacterCount == 2, "the list to reload after deleting");
            Check(Directory.GetFiles(saveFolder, "*.json").Length == 2, "deleting removes the save file");

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
