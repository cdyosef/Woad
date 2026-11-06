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

            // The roof, from the start: open stone, the crenellated edge, the stairs down in the middle.
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } roofShots)
            {
                var roof = new GameScreen { Setup = setup with { StartRoomId = DungeonGenerator.RoofId }, Content = _content };
                Show(roof);
                await roof.WaitUntilIdle();
                Room top = roof.Session.State.Rooms[DungeonGenerator.RoofId];
                Check(top.PlaceName == "the Roof" && top.TilesOfKind(TileKind.Pit).Count() > 40, "the roof's edge is open air between pillars");
                Check(top.Monsters.Any(monster => monster.Definition.Id == "baron_herkullos") && top.Monsters.Count(monster => monster.Definition.Id == "grasshopper_knight") == 3,
                    "Baron Herkullos holds the roof, with his Grasshopper Knights");
                roof.Board.ZoomBy(0.5f);
                for (int frame = 0; frame < 30; frame++)
                {
                    await NextFrame(this);
                }

                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{roofShots}/keep_roof.png");
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
