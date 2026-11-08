using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --smashtest</c>, with <see cref="DemoRun.SmashDemo"/>): a double-click on the crate beside
/// Aria aims Rumble at it, and one more click swings: a break mark, or a blow that falls short.
/// </summary>
public partial class GameScreen
{
    private async void RunSmashTest()
    {
        try
        {
            await WaitUntilIdle();
            var crate = new GridPosition(4, 4);
            Room room = _session.State.Rooms["yard"];
            Check(room.Layout[crate] == TileKind.Crate && _session.GetSmashTargets().Contains(crate), "a crate stands beside Aria, to smash");

            await DoubleClickAt(this, TileCenter(crate));
            Check(_rumbling, "double-clicking the crate aims Rumble at it");
            await ClickTile(crate);
            await WaitUntilIdle();
            Check(_messages.Any(message => message.Contains("smashes at the crate")), $"one more click swings at it ({string.Join(" / ", _messages.TakeLast(2))})");
            Check(room.BreaksAt(crate) == 1 || room.Layout[crate] == TileKind.Floor || _messages.Any(message => message.Contains("it holds")), "a break mark, or it holds");
            await SaveShot("smash");

            GD.Print("Smash test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Smash test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
