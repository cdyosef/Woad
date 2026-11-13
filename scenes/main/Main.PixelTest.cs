using Godot;
using Woad.Core.Art;
using Woad.Scenes.Paint;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Pixel Paint (<c>-- --paint</c>), and its test (<c>-- --pixeltest</c>): paints a figure with the pencil,
/// the mirror, the fill, the eraser and the pick, undoes and redoes, saves it to a throwaway folder,
/// finds it among the figures, opens it again, repaints a built-in figure and puts it back.
/// </summary>
public partial class Main
{
    private void ShowPaint(bool test = false)
    {
        if (test)
        {
            Looks.PaintedDir = Path.Combine(Path.GetTempPath(), "woad-pixeltest", Guid.NewGuid().ToString("N"));
        }

        var paint = new PaintScreen();
        paint.BackRequested += ShowMainMenu;
        Show(paint);
        if (test)
        {
            RunPixelTest(paint);
        }
    }

    private async void RunPixelTest(PaintScreen paint)
    {
        try
        {
            await NextFrame(this);
            PaintCanvas canvas = paint.Canvas;
            Vector2 Cell(int x, int y) => canvas.GlobalPosition + canvas.CellCenter(x, y);
            int built = Looks.All.Count;

            await Click(this, paint.SaveButton);
            Check(Looks.All.Count == built && paint.Status.Contains("Nothing"), $"an empty figure isn't saved ({paint.Status})");

            await Click(this, paint.NameEdit);
            paint.NameEdit.SelectAll();
            await Type(this, "Tin Man");
            Check(paint.LookId == "tin_man", $"the figure's name makes its id ({paint.LookId})");
            await ClickAt(this, new Vector2(120, 20));

            await ClickAt(this, Cell(3, 2));
            Check(canvas[3, 2] == "T" && canvas[12, 2] == "", "the pencil paints a pixel in the main color");

            await Click(this, paint.MirrorButton);
            await Click(this, paint.Swatch("#181420"));
            await ClickAt(this, Cell(5, 4));
            Check(canvas[5, 4] == "#181420" && canvas[10, 4] == "#181420", "with the mirror on, both halves are painted");

            await DragAt(this, Cell(4, 6), Cell(4, 10));
            Check(Enumerable.Range(6, 5).All(y => canvas[4, y] == "#181420" && canvas[11, y] == "#181420"), "dragging draws a line, mirrored");

            await Click(this, paint.Swatch("e"));
            await ClickAt(this, Cell(6, 5));
            Check(canvas[6, 5] == "e" && canvas[9, 5] == "e", "the eye paint marks the eyes");

            await Click(this, paint.UndoButton);
            Check(canvas[6, 5] == "" && canvas[9, 5] == "", "Undo takes the last stroke back");
            await Click(this, paint.RedoButton);
            Check(canvas[6, 5] == "e", "Redo puts it back");

            // A closed box to fill: its sides are the lines down x 4 and x 11, with a lid and a floor.
            await Click(this, paint.Swatch("#181420"));
            await DragAt(this, Cell(4, 6), Cell(7, 6));
            await DragAt(this, Cell(4, 10), Cell(7, 10));
            await Click(this, paint.Swatch("S"));
            await Click(this, paint.ToolButton(PaintTool.Fill));
            await ClickAt(this, Cell(6, 8));
            Check(canvas[5, 7] == "S" && canvas[10, 9] == "S" && canvas[4, 8] == "#181420" && canvas[3, 8] == "", "Fill fills the inside of an outline and stops at it");

            await Click(this, paint.ToolButton(PaintTool.Eraser));
            await ClickAt(this, Cell(3, 2));
            Check(canvas[3, 2] == "", "the eraser rubs a pixel out");

            await Click(this, paint.ToolButton(PaintTool.Pick));
            await ClickAt(this, Cell(5, 4));
            Check(canvas.Paint == "#181420" && canvas.Tool == PaintTool.Pencil, "Pick takes the paint under the mouse and goes back to the pencil");

            await Click(this, paint.SaveButton);
            Check(File.Exists(Looks.PaintedPath("tin_man")), $"Save writes the figure's file ({paint.Status})");
            Look? tinMan = Looks.All.FirstOrDefault(look => look.Id == "tin_man");
            Check(Looks.All.Count == built + 1 && tinMan is { Name: "Tin Man", SkinSet: "human", HairSet: "hair" }, "the painted figure joins the figures to pick from");
            Check(tinMan!.Eyes.SequenceEqual([new Vector2I(6, 5), new Vector2I(9, 5)]), "its eyes are where they were marked");
            Check(Looks.Strip(tinMan) is { } strip && strip.GetWidth() == 80 && strip.GetHeight() == 16, "it has art to draw: five frames, as the built-in figures have");
            PaintedLook saved = PaintedLook.FromJson(File.ReadAllText(Looks.PaintedPath("tin_man")));
            Check(saved.Rows[8][6] == 'S' && saved.Palette[saved.Rows[4][5]] == "#181420", "the file holds the pixels and their colors");

            await Click(this, paint.NewButton);
            Check(canvas[5, 4] == "" && paint.LookId == "new_figure", "New starts a blank figure");
            Check(paint.Open(tinMan) && canvas[5, 4] == "#181420" && canvas[6, 5] == "e" && canvas[5, 7] == "S" && paint.LookId == "tin_man", "the saved figure opens again as it was painted");
            await NextFrame(this);
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/paint.png");
            }

            string drawing = Path.Combine(Looks.PaintedDir, "drawing.png");
            using (Image sketch = Image.CreateEmpty(40, 60, false, Image.Format.Rgba8))
            {
                sketch.Fill(new Color("e6deb4"));
                sketch.FillRect(new Rect2I(10, 8, 20, 44), new Color("50424a"));
                sketch.SavePng(drawing);
            }

            Check(paint.LoadTrace(drawing) && canvas.Trace is not null, "a drawing opens under the grid to trace");
            await Wheel(this, Cell(8, 8), MouseButton.WheelUp, 3);
            Check(canvas.TraceZoom > 1.3f, $"the wheel zooms the drawing ({canvas.TraceZoom:0.##})");
            Check(!paint.LoadTrace(Looks.PaintedPath("tin_man")) && canvas.Trace is not null, "a file that isn't a picture is turned away");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } traced)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{traced}/paint-trace.png");
            }

            await Click(this, paint.DeleteButton);
            Check(paint.IsAskingToDelete && File.Exists(Looks.PaintedPath("tin_man")), "Delete asks first");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } asked)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{asked}/paint-delete.png");
            }
            await Click(this, paint.KeepButton);
            Check(!paint.IsAskingToDelete && File.Exists(Looks.PaintedPath("tin_man")), "Keep it keeps it");
            await Click(this, paint.DeleteButton);
            await Click(this, paint.ConfirmDeleteButton);
            Check(!File.Exists(Looks.PaintedPath("tin_man")) && Looks.All.Count == built, "Delete, said again, takes a painted figure away");

            Look wanderer = Looks.Get("wanderer");
            if (paint.Open(wanderer))
            {
                Check(canvas[6, 5] == "e" && canvas[9, 5] == "e" && canvas[7, 8] == "T" && canvas[7, 4] == "S" && canvas[7, 2] == "H" && canvas[6, 1] == "#181420",
                    "a built-in figure opens with its player colors, eyes and outline");
                await Click(this, paint.Swatch("R"));
                await ClickAt(this, Cell(7, 8));
                await Click(this, paint.SaveButton);
                Check(paint.LookId == "wanderer_2" && Looks.Painted("wanderer_2") is { } copy && copy.Rows[8][7] == 'R' && copy.Rows[8][8] == 'R',
                    $"saving a built-in figure makes a new one from it ({paint.LookId}: {paint.Status})");
                Check(Looks.Painted("wanderer") is null && Looks.All.Count == built + 1, "and the original stays as it came");
                await Click(this, paint.DeleteButton);
                await Click(this, paint.ConfirmDeleteButton);
                Check(Looks.Painted("wanderer_2") is null && Looks.All.Count == built, "Delete takes the new figure away again");
            }
            else
            {
                GD.Print("(the built-in figures' art can't be read here, so repainting one wasn't tried)");
            }

            GD.Print("Pixel Paint test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Pixel Paint test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
