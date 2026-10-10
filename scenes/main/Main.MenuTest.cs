using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --menutest</c>): creates two characters, seats them, starts a run, leaves it
/// and deletes a character, all with synthetic clicks and typing against a throwaway save folder.
/// </summary>
public partial class Main
{
    private async void RunMenuTest(string saveFolder)
    {
        try
        {
            MainMenu menu = await Screen<MainMenu>();
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
            await Click(this, editor.PlusButton(StatKind.Hearts));
            await Click(this, editor.PlusButton(StatKind.Hearts));
            await Click(this, editor.PlusButton(StatKind.AttackDice));
            Check(editor.PointsLeft == 1, "point-buy spends 1 + 1 + 3 of 6 points");
            Check(editor.PlusButton(StatKind.AttackDice).Disabled, "level 1 allows only one extra Attack die");
            Check(editor.PlusButton(StatKind.DefenseDice).Disabled, "you can't spend points you don't have");
            await Click(this, editor.SaveButton);

            list = await LoadedList();
            Check(list.CharacterCount == 1, "the new character is listed");
            Check(Directory.GetFiles(saveFolder, "*.json").Length == 1, "the character is saved to disk");

            await Click(this, list.NewButton);
            editor = await Screen<CharacterEditor>();
            await Click(this, editor.NameEdit);
            await Type(this, "Wren");
            await Click(this, editor.SaveButton);
            list = await LoadedList();
            Check(list.CharacterCount == 2, "a second character is listed");

            await Click(this, list.BackButton);
            menu = await Screen<MainMenu>();
            await Click(this, menu.PlayButton);
            RunSetupScreen setup = await Screen<RunSetupScreen>();
            await WaitFor(this, () => setup.IsLoaded, "the roster to load");
            Check(setup.StartButton.Disabled, "Start needs at least one player");
            await Click(this, setup.RosterRow(1));
            await Click(this, setup.RosterRow(0));
            Check(setup.PartySize == 2, "clicking characters seats them");

            await Click(this, setup.StartButton);
            GameScreen game = await Screen<GameScreen>();
            await game.WaitUntilIdle();
            string[] names = [.. game.Session.State.Heroes.Select(hero => hero.Name)];
            Check(names.SequenceEqual(["Wren", "Morgan"]), "seat order follows the order characters were picked");
            var morgan = game.Session.State.Heroes[1];
            Check(morgan.MaxHearts == 6 && morgan.EffectiveStats.AttackDice == 3, "the hero has the character's stats plus gear");
            Check(morgan.Portrait == "moss", "the hero has the chosen portrait");

            await Click(this, game.MenuButton);
            await Click(this, game.MenuButton);
            setup = await Screen<RunSetupScreen>();
            Check(true, "the Menu button leaves the run after confirming");

            await WaitFor(this, () => setup.IsLoaded, "the roster to load");
            Button back = setup.GetChildren().OfType<Button>().First(button => button.Text == "Back");
            await Click(this, back);
            menu = await Screen<MainMenu>();
            await Click(this, menu.CharactersButton);
            list = await LoadedList();
            await Click(this, list.Row(1));
            await Click(this, list.DeleteButton);
            Check(list.CharacterCount == 2, "the first Delete click only asks for confirmation");
            await Click(this, list.DeleteButton);
            await WaitFor(this, () => list.IsLoaded && list.CharacterCount == 1, "the list to reload after deleting");
            Check(Directory.GetFiles(saveFolder, "*.json").Length == 1, "deleting removes the save file");

            GD.Print("Menu test passed.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"Menu test failed: {exception.Message}");
            GetTree().Quit(1);
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

    private async Task<CharacterListScreen> LoadedList()
    {
        CharacterListScreen list = await Screen<CharacterListScreen>();
        await WaitFor(this, () => list.IsLoaded, "the character list to load");
        return list;
    }
}
