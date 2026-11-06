using Godot;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Game;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --gatetest</c>): the Emerald Keep from the start. The party comes in at the courtyard's
/// gateway, across the moat from the Emerald Knight and his guards, and the gate won't open without his key.
/// </summary>
public partial class Main
{
    private async void RunGateTest()
    {
        try
        {
            RunSetup setup = DungeonGenerator.CreateRun(_content, "emerald_keep", [.. DemoRun.DebugHeroes.Take(3)], 11);
            var game = new GameScreen { Setup = setup, Content = _content };
            Show(game);
            await game.WaitUntilIdle();

            Room courtyard = game.Session.State.Rooms[setup.StartRoomId];
            Check(courtyard.Monsters.Count(monster => monster.Definition.Id == "emerald_knight") == 1
                && courtyard.Monsters.Count(monster => monster.Definition.Id == "emerald_guard") == 2,
                "the Emerald Knight and two spear guards (two for every three heroes, rounded up) hold the courtyard");
            Check(courtyard.TilesOfKind(TileKind.Exit).Count() == 4 && courtyard.TilesOfKind(TileKind.DeepWater).Any(),
                "the party comes in at a gateway four wide, a moat between them and the gate");
            Check(courtyard.Passages.Values.Any(passage => passage is { NeedsGateKey: true, IsLocked: true }), "the way into the keep is a locked gate");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/keep_courtyard.png");
            }

            GD.Print("Gate test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Gate test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
