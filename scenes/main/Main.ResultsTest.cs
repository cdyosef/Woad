using Godot;
using Woad.Core.Characters;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Game;
using Woad.Scenes.Menu;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --resultstest</c>): two saved characters escape a one-room run by clicking, then
/// the test checks that their results were written to disk and shown, against a throwaway save folder.
/// </summary>
public partial class Main
{
    private async void RunResultsTest(string saveFolder)
    {
        try
        {
            Character morgan = Character.Create("Morgan", "moss", new StatAllocation { Hearts = 1 }, _content, DateTimeOffset.UtcNow);
            Character wren = Character.Create("Wren", "frost", StatAllocation.None, _content, DateTimeOffset.UtcNow.AddSeconds(1));
            await _characters.SaveAsync(morgan);
            await _characters.SaveAsync(wren);
            Character[] party = [morgan, wren];

            ShowGame(DemoRun.ExitRoom([.. party.Select(character => character.ToHeroSetup())], seed: 1), party);
            GameScreen game = await Screen<GameScreen>();
            await game.WaitUntilIdle();

            var exit = new GridPosition(4, 3);
            for (int seat = 0; seat < party.Length; seat++)
            {
                Hero hero = game.Session.State.Heroes[seat];
                await Click(this, game.RollButton);
                await game.WaitUntilIdle();
                await ClickAt(this, game.TileCenter(exit));
                await game.WaitUntilIdle();
                Check(hero.Position == exit, $"{hero.Name} walks onto the exit");
                await ClickAt(this, game.TileCenter(exit));
                await game.WaitUntilIdle();
                Check(hero.Status == HeroStatus.Escaped, $"clicking the exit lets {hero.Name} escape");
            }

            ResultsScreen results = await Screen<ResultsScreen>();
            Check(true, "the results screen follows the end of the run");
            Check(Music.Current == "victory", "the results screen plays the victory tune");

            IReadOnlyList<Character> saved = (await _characters.LoadAllAsync()).Characters;
            Check(saved.Count == 2 && saved.All(character => character.RunsPlayed == 1 && character.Xp == GameRules.XpForEscaping),
                "each character's run and XP are saved");
            Check(saved.All(character => character.Wins == 1), "tied winners each get the win");
            Check(saved.Single(character => character.Name == "Morgan").Equipped.SequenceEqual(["wooden_sword", "buckler"]),
                "escaped heroes keep their gear");

            await Click(this, results.DoneButton);
            await Screen<RunSetupScreen>();
            Check(true, "Done returns to party setup");

            GD.Print("Results test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Results test failed: {exception.Message}");
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
}
