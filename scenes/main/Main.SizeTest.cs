using Godot;
using Woad.Scenes.Game;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --sizetest</c>, with a window): the game in windows of other shapes. 1440 by 900 (16:10, two to
/// a pixel) makes the canvas 720 by 450, and the game fills it to every edge; 1920 by 1080 makes it 640 by 360 again.
/// A menu sits in the middle. Screenshots of each, with WOAD_SHOTS.
/// </summary>
public partial class Main
{
    private async void RunSizeTest(ulong seed)
    {
        try
        {
            ShowGame(DemoRun.Torch(seed), []);
            var game = (GameScreen)_screen!;
            await game.WaitUntilIdle();

            // A card picked up (its slide-out), a monster targeted (its sheet), a monster under the mouse (the hover box).
            Woad.Core.Model.Hero aria = game.Session.State.Heroes[0];
            Woad.Core.Model.Monster slime = game.Session.State.RoomOf(aria).Monsters[0];
            game.ShowForTest(aria.AllCards.First(card => card.Definition.Id == "torch").Id, slime.Id, slime.Position);
            await game.WaitUntilIdle();
            Check(game.ItemPanel.Body.Contains("Ways to use it") && game.ItemPanel.Body.Contains("Base price"), $"the slide-out tells all about the picked-up card ({game.ItemPanel.Body.Length} letters)");
            Check(game.HoverPanel.Shown.Contains("Slime") || game.HoverPanel.Shown.Length > 20, $"the hover box tells what's under the mouse ({game.HoverPanel.Shown})");

            foreach ((int width, int height, Vector2 canvas) in new[] { (1440, 900, new Vector2(720, 450)), (1920, 1080, new Vector2(640, 360)), (1280, 800, new Vector2(640, 400)) })
            {
                GetWindow().Size = new Vector2I(width, height);
                for (int frame = 0; frame < 10; frame++)
                {
                    await NextFrame(this);
                }

                GD.Print($"window {GetWindow().Size}, content {GetWindow().ContentScaleSize}, mode {GetWindow().Mode}");
                Vector2 size = GetViewportRect().Size;
                Check(size == canvas, $"a {width}x{height} window makes a {canvas.X}x{canvas.Y} canvas ({size})");
                Check(game.Size == size && game.RightPanelEnd == size, $"the game fills it to every edge ({game.Size}, right panel to {game.RightPanelEnd})");
                Check(game.LogHeight >= 24, $"the log has room ({game.LogHeight})");
                if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
                {
                    GetViewport().GetTexture().GetImage().SavePng($"{shots}/size_{width}x{height}.png");
                }
            }

            GetWindow().Size = new Vector2I(1440, 900);
            ShowMainMenu();
            for (int frame = 0; frame < 10; frame++)
            {
                await NextFrame(this);
            }

            Check(_screen!.Position == new Vector2(40, 45), $"a menu sits in the middle ({_screen.Position})");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } menuShots)
            {
                GetViewport().GetTexture().GetImage().SavePng($"{menuShots}/size_menu.png");
            }

            GD.Print("Size test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Size test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
