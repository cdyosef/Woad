using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --clodtest</c>, with <see cref="DemoRun.Clod"/>): Aria and Clod Hopper, her frog-hunting companion,
/// in his hat and waders with his gig, at a bog pool. Aria ends her turns; Clod plays his own, wading in, and says his
/// piece (in a speech bubble) now and then.
/// </summary>
public partial class GameScreen
{
    private async void RunClodTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Hero clod = _session.State.Heroes.Single(hero => hero.Name == "Clod Hopper");
            Check(clod.IsCompanion && clod.Appearance.Persona == GameSession.ClodPersona, "Clod Hopper is Aria's companion, with his persona");
            Check(clod.EquippedCards.Any(card => card.Definition.Id == "frog_gig") && clod.Swims, "he holds his Frog Gig and wears his waders");
            Check(clod.Bags.Any(bag => bag.Definition.Id == "sack") && clod.AllCards.Any(card => card.Definition.Id == "bog_lantern"), "a Sack under his belt, his lantern on it");
            await SaveShot("clod_start");

            bool spoke = false;
            for (int turns = 0; turns < 12 && !spoke && aria.IsInPlay; turns++)
            {
                await Click(this, _endTurn);
                await WaitUntilIdle();
                spoke = _messages.Any(message => message.Contains("Clod Hopper: \"", StringComparison.Ordinal));
            }

            Check(spoke, $"Clod says his piece ({string.Join(" / ", _messages.TakeLast(4))})");
            await SaveShot("clod_speaks");
            GD.Print("Clod test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Clod test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
