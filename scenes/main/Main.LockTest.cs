using Godot;
using Woad.Core.Art;
using Woad.Scenes.Menu;
using Woad.Scenes.Paint;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --locktest</c>): figures that ship locked. The game's own are locked (their code isn't in the
/// source). A test figure, Lockbox, teased: in character creation it shows dark, as "???", and can't be saved; a wrong
/// code unlocks nothing; its own code unlocks it. A secret one is hidden altogether until its code is entered. And
/// Pixel Paint (from the editor) locks a figure with a code.
/// </summary>
public partial class Main
{
    private async void RunLockTest()
    {
        string folder = Path.Combine(Path.GetTempPath(), "woad-locktest", Guid.NewGuid().ToString("N"));
        try
        {
            Looks.UnlocksPath = Path.Combine(folder, "unlocks.json");
            Check(Looks.All.Where(look => Looks.IsShipped(look.Id)).All(look => Looks.IsLocked(look.Id)), "the game's own painted figures ship locked");

            Looks.PaintedDir = Path.Combine(folder, "painted");
            Directory.CreateDirectory(Looks.PaintedDir);
            PaintedLook lockbox = PaintedLook.Blank("Lockbox") with { Rows = [.. Enumerable.Repeat("....RRRRRRRR....", 16)], Lock = FigureLock.For("brass key", secret: false) };
            File.WriteAllText(Looks.PaintedPath("lockbox"), lockbox.ToJson());
            Looks.Reload();

            ShowEditor(null, ShowMainMenu);
            var editor = await Screen<CharacterEditor>();
            for (int step = 0; step < Looks.All.Count && editor.Appearance.Look != "lockbox"; step++)
            {
                await Click(this, editor.StyleButton(0, next: true));
            }

            Check(editor.LookShown == "??? (locked)", $"stepping through the looks, a locked one shows as ??? ({editor.LookShown})");
            editor.NameEdit.Text = "Locksmith";
            await Click(this, editor.StyleButton(1, next: true));
            Check(editor.SaveButton.Disabled && editor.ErrorText.Contains("locked"), $"and it can't be saved ({editor.ErrorText})");

            editor.CodeEdit.Text = "pinecone";
            await Click(this, editor.UnlockButton);
            Check(editor.ErrorText.Contains("doesn't unlock"), $"a wrong code unlocks nothing ({editor.ErrorText})");
            editor.CodeEdit.Text = "Brass Key";
            await Click(this, editor.UnlockButton);
            Check(editor.ErrorText.Contains("Lockbox"), $"its code unlocks it ({editor.ErrorText})");
            Check(editor.LookShown == "Lockbox" && !editor.SaveButton.Disabled, $"the look shows, and can be saved ({editor.LookShown})");
            Check(!Looks.IsLocked("lockbox") && File.Exists(Looks.UnlocksPath), "unlocked for good on this computer");

            // A secret figure: hidden altogether until its code.
            PaintedLook hush = PaintedLook.Blank("Hush") with { Rows = [.. Enumerable.Repeat("....TTTTTTTT....", 16)], Lock = FigureLock.For("whisper", secret: true) };
            File.WriteAllText(Looks.PaintedPath("hush"), hush.ToJson());
            Looks.Reload();
            Check(Looks.All.Any(look => look.Id == "hush") && Looks.Choosable.All(look => look.Id != "hush"), "a secret figure isn't among the looks to pick");
            Check(Looks.TryUnlock("WHISPER").SequenceEqual(["Hush"]) && Looks.Choosable.Any(look => look.Id == "hush"), "its code brings it out");

            // Pixel Paint, from the editor, locks a figure with a code.
            ShowPaint(test: false);
            var paint = await Screen<PaintScreen>();
            Check(PaintScreen.CanLock && paint.LockButton.Visible, "Pixel Paint (from the editor) can lock a figure");
            paint.NameEdit.Text = "Vault Guard";
            paint.Rename("Vault Guard");
            paint.Canvas.Load(PaintCanvasFill());
            await Click(this, paint.LockButton);
            await Click(this, paint.SaveButton);
            Check(!File.Exists(Looks.PaintedPath("vault_guard")) && paint.Status.Contains("Type a code"), $"locking asks for a code ({paint.Status})");
            paint.LockCode.Text = "Iron Gate";
            await Click(this, paint.SaveButton);
            Check(File.Exists(Looks.PaintedPath("vault_guard")) && PaintedLook.FromJson(File.ReadAllText(Looks.PaintedPath("vault_guard"))).Lock == FigureLock.For("irongate", secret: false),
                $"saved teased, with its code's hash ({paint.Status})");

            GD.Print("Lock test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Lock test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static string[,] PaintCanvasFill()
    {
        var pixels = PaintCanvas.Blank();
        for (int y = 2; y < 14; y++)
        {
            for (int x = 4; x < 12; x++)
            {
                pixels[x, y] = "T";
            }
        }

        pixels[6, 5] = pixels[9, 5] = "e";
        return pixels;
    }
}
