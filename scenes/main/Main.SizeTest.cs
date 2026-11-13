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

            // At 720 by 450: the sheet a quarter bigger, the board wider than a standard room; the bag rolls out of its
            // drawer under the sheet, and a card picked from it slides out beside it, not over it.
            GetWindow().Size = new Vector2I(1440, 900);
            for (int frame = 0; frame < 10; frame++)
            {
                await NextFrame(this);
            }

            Check(game.SheetScale == 1.25f && game.BoardRect.Size.X > BoardView.Pixels && game.BoardRect.Size.Y >= BoardView.Pixels, $"at 720x450 the sheet's bigger and the board shows more ({game.SheetScale}, {game.BoardRect})");
            game.ShowForTest(null, null, null);
            await Click(this, game.Sheet.BagDeck);
            for (int frame = 0; frame < 20; frame++)
            {
                await NextFrame(this);
            }
            Rect2 bag = game.Sheet.BagView.GetGlobalRect();
            Check(game.Sheet.IsBagOpen && bag.Position.X >= game.Sheet.GetGlobalRect().End.X - 1, $"the bag rolls out from under the sheet ({bag})");
            game.ShowForTest(aria.AllCards.First(card => card.Definition.Id == "torch").Id, null, null);
            for (int frame = 0; frame < 20; frame++)
            {
                await NextFrame(this);
            }
            Rect2 slide = game.Slideout.GetGlobalRect();
            Check(slide.Position.X >= bag.End.X - 1, $"a picked card's slide-out comes out beside the open bag, not over it ({slide} by {bag})");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } bagShots)
            {
                GetViewport().GetTexture().GetImage().SavePng($"{bagShots}/size_bag.png");
            }

            await Click(this, game.Sheet.BagDeck);
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
