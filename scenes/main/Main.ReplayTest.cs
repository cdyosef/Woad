using Godot;
using Woad.Core.Bots;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Scenes.Game;
using Woad.Scenes.Net;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// Debug mode (<c>-- --replaytest</c>): a recorded session (Aria and Bran in the Emerald Keep, the bot playing, Bran
/// shouting a warning a few moves in) is watched from the Replays list: it plays move by move, what was said shows when
/// it was said, the round buttons jump about, Clean hides the panels, and a screenshot is saved.
/// </summary>
public partial class Main
{
    private async void RunReplayTest(string saveFolder)
    {
        try
        {
            Stats stats = GameRules.HeroBaseStats with { Hearts = 14 };
            var run = new SavedRun { ThemeId = "emerald_keep", Seed = 9, Heroes = [new HeroSetup("Aria", stats, ["wooden_sword"]), new HeroSetup("Bran", stats, ["wooden_sword"])] };
            var record = new SessionRecord { GameVersion = NetLink.GameVersion, Run = run.ToJson(), Players = ["Host", "Guest"] };
            GameSession played = run.Restore(_content);
            for (int i = 0; i < 70 && played.State.Phase == RunPhase.InProgress; i++)
            {
                var command = SimpleBot.NextCommand(played);
                played.Execute(command);
                run.Record(command, played.State);
                record.Record(run, i % 2 == 0 ? "Host" : "Guest");
                if (i == 1)
                {
                    record.Note(SessionNoteKind.Chat, "Bran", i + 1, played.State.Heroes[1].Id, "Behind you!", "common");
                }
            }

            Directory.CreateDirectory(ReplayFolder);
            File.WriteAllText(Path.Combine(ReplayFolder, "2026-11-12 2000 The Emerald Keep.woad-replay.json"), record.ToJson());
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } keep)
            {
                // Kept, to try the video recorder on.
                File.WriteAllText(Path.Combine(keep, "test.woad-replay.json"), record.ToJson());
            }

            ShowReplays();
            ReplayListScreen list = await Screen<ReplayListScreen>();
            Check(list.Count == 1 && !list.WatchButton.Disabled, "the Replays list shows the recorded session");
            await Click(this, list.WatchButton);
            GameScreen game = await Screen<GameScreen>();
            await game.WaitUntilIdle();
            Check(game.ReplayMove == 0 && !game.ReplayPlaying, "watching, it waits at the start");

            await Click(this, (Button)game.FindChild("ReplayPlay", recursive: true, owned: false)!);
            await WaitFor(this, () => game.ReplayMove >= 6, "the replay to play on", maxFrames: 6000);
            Check(game.HasBubble(game.Session.State.Heroes[1].Id), "Bran's warning shows over him when he said it");
            await Click(this, (Button)game.FindChild("ReplayPlay", recursive: true, owned: false)!);
            Check(!game.ReplayPlaying, "Pause stops it");

            await Click(this, (Button)game.FindChild("ReplayR>", recursive: true, owned: false)!);
            game = await WaitForNew(game);
            int round = game.Session.State.Round;
            Check(round >= 2, $"R> jumps on to the next round ({round}, move {game.ReplayMove})");
            await Click(this, (Button)game.FindChild("Replay<R", recursive: true, owned: false)!);
            game = await WaitForNew(game);
            Check(game.Session.State.Round < round, $"<R jumps back ({game.Session.State.Round})");

            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } barShots)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{barShots}/replay_bar.png");
            }

            await Click(this, (Button)game.FindChild("ReplayClean", recursive: true, owned: false)!);
            Check(game.IsClean, "Clean hides the panels");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/replay_clean.png");
            }

            string shot = game.TakeScreenshot();
            Check(shot.Length == 0 || File.Exists(shot), "Shot saves a screenshot (with a window to draw in)");
            if (shot.Length > 0)
            {
                File.Delete(shot);
            }

            // H brings the panels (and the bar) back.
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.H, Pressed = true });
            await NextFrame(this);
            Check(!game.IsClean, "H brings the panels back");
            await Click(this, (Button)game.FindChild("ReplayExit", recursive: true, owned: false)!);
            await Screen<ReplayListScreen>();
            Check(Engine.TimeScale == 1, "Exit goes back to the list, at normal speed");

            GD.Print("Replay test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Replay test failed: {exception.Message} {exception.StackTrace}");
            Sfx.QuitGame(this, 1);
        }
        finally
        {
            if (Directory.Exists(saveFolder))
            {
                Directory.Delete(saveFolder, recursive: true);
            }
        }
    }

    private async Task<GameScreen> WaitForNew(GameScreen old)
    {
        await WaitFor(this, () => _screen is GameScreen fresh && fresh != old, "the replay to be built afresh", maxFrames: 3000);
        var game = (GameScreen)_screen!;
        await game.WaitUntilIdle();
        return game;
    }
}
