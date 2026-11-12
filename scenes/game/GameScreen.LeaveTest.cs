using Godot;
using Woad.Core.Commands;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --leavetest</c>, with <see cref="DemoRun.Leaver"/>): Clod Hopper starts the run unpaid and
/// leaving. Clicking him asks: talk him round, or let him go; Aria tries. Then, turn by turn, he either has a change
/// of heart and stays, or walks out.
/// </summary>
public partial class GameScreen
{
    private async void RunLeaveTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            Hero clod = _session.State.Heroes.Single(hero => hero.Name == "Clod Hopper");
            Check(clod.Leaving && clod.Wage > 0, "Clod starts the run leaving, unpaid");

            await ClickTile(clod.Position);
            Check(_inventory.IsChoosingHand && _inventory.HandButton(0).Text.StartsWith("Convince", StringComparison.Ordinal),
                $"clicking him asks: talk him round, or let him go ({_inventory.HandButton(0).Text})");
            await SaveShot("leaver_asked");
            await Click(this, _inventory.HandButton(0));
            await WaitUntilIdle();
            Check(_messages.Any(message => message.Contains("talk") && message.Contains("Clod Hopper")), $"Aria tries to talk him round ({string.Join(" / ", _messages.TakeLast(2))})");

            for (int turns = 0; turns < 14 && clod.Leaving; turns++)
            {
                await Click(this, _endTurn);
                await WaitUntilIdle();
            }

            Check(!clod.Leaving && (clod.Departed || clod.IsInPlay), $"in the end he walks out, or stays after all (departed {clod.Departed}: {string.Join(" / ", _messages.TakeLast(3))})");
            Check(!clod.Departed || !_figures.ContainsKey(clod.Id), "gone, his figure leaves the board");
            await SaveShot("leaver_end");
            GD.Print("Leave test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Leave test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
