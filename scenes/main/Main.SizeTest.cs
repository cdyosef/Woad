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
            ShowGame(DemoRun.Leaver(seed), []);
            var game = (GameScreen)_screen!;
            await game.WaitUntilIdle();

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
