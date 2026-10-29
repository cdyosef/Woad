using Godot;
using Woad.Core.Chiptune;
using Woad.Scenes.Tracker;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes;

/// <summary>
/// The Music Tracker (<c>-- --tracker</c>), and its test (<c>-- --trackertest</c>): names a song, plays
/// notes in from the keyboard, drums and a volume on the noise channel, copies the pattern, exports it
/// to a throwaway folder and checks the song file and WAV, plays it, and opens it again.
/// </summary>
public partial class Main
{
    private void ShowTracker(bool test = false)
    {
        string folder = Path.Combine(Path.GetTempPath(), "woad-trackertest", Guid.NewGuid().ToString("N"));
        var tracker = test ? new TrackerScreen { SongsDir = Path.Combine(folder, "songs"), MusicDir = Path.Combine(folder, "music") } : new TrackerScreen();
        tracker.BackRequested += ShowMainMenu;
        Show(tracker);
        if (test)
        {
            RunTrackerTest(tracker);
        }
    }

    private async void RunTrackerTest(TrackerScreen tracker)
    {
        try
        {
            await NextFrame(this);
            Button Find(string text) => tracker.GetChildren().OfType<Button>().First(button => button.Text == text);

            Check(tracker.NameEdit.GetRect().End.Y <= 74, $"the name box fits above the order list ({tracker.NameEdit.Size})");
            await Click(this, tracker.NameEdit);
            await Type(this, "Test Tune");
            Check(tracker.Song.Name == "test_tune", "the song's name is made fit for the game: test_tune");

            await ClickAt(this, tracker.Grid.GlobalPosition + tracker.Grid.CellPosition(0, 0) + new Vector2(4, 4));
            Check(tracker.Grid.HasFocus() && tracker.Grid.CursorChannel == 0 && tracker.Grid.CursorRow == 0, "clicking a cell puts the cursor on it");

            await Type(this, "zxc1");
            ChipCell[] lead = tracker.Song.Patterns[0].Cells[0];
            Check(lead[0] == new ChipCell(60) && lead[1] == new ChipCell(62) && lead[2] == new ChipCell(64), "Z, X and C play C, D and E in octave 4, a row each");
            Check(lead[3].Note == ChipCell.Off && tracker.Grid.CursorRow == 4, "1 is a note-off, and the cursor steps down");

            foreach (Key key in new[] { Key.Tab, Key.Tab, Key.Tab, Key.Home })
            {
                await Press(key);
            }

            await Type(this, "zx");
            await Press(Key.Right);
            await Press(Key.Up);
            await Type(this, "8");
            ChipCell[] drums = tracker.Song.Patterns[0].Cells[3];
            Check(drums[0] == new ChipCell(ChipDrum.Kick) && drums[1] == new ChipCell(ChipDrum.Snare, 8), "on the noise channel Z is a kick and X a snare, and 8 in the volume column sets it");

            await Click(this, Find("Copy"));
            Check(tracker.Song.Order.Count == 2 && tracker.Slot == 1 && tracker.Song.Patterns[1].Cells[0][0] == new ChipCell(60), "Copy puts a copy of the pattern next in the order");

            await Click(this, tracker.ExportButton);
            string songFile = Path.Combine(tracker.SongsDir, "test_tune.json");
            string wavFile = Path.Combine(tracker.MusicDir, "test_tune.wav");
            Check(File.Exists(songFile) && File.Exists(wavFile), "Export saves the song file and renders the WAV");
            ChipSong saved = ChipSong.FromJson(File.ReadAllText(songFile));
            Check(saved.ToJson() == tracker.Song.ToJson(), "the song file holds the song as written");
            int samples = (int)Math.Round(saved.Seconds * ChipSynth.Rate);
            Check(new FileInfo(wavFile).Length == 44 + 2 * samples, $"the WAV is exactly the song's {saved.TotalRows} rows long, to loop end to end");

            await Click(this, tracker.PlayButton);
            Check(Music.Current == Music.PreviewName, "Play plays the song through the game's music");
            if (System.Environment.GetEnvironmentVariable("WOAD_SHOTS") is { } shots)
            {
                await NextFrame(this);
                await NextFrame(this);
                GetViewport().GetTexture().GetImage().SavePng($"{shots}/tracker.png");
            }

            await Click(this, tracker.StopButton);
            Check(Music.Current is null, "Stop stops it");

            tracker.Load(ChipSong.EightBit());
            await Click(this, Find("Open"));
            await Click(this, tracker.GetChildren().OfType<Panel>().SelectMany(panel => panel.GetChildren().OfType<Button>()).First(button => button.Text == "test_tune"));
            Check(tracker.Song.Name == "test_tune" && tracker.Song.Order.Count == 2 && tracker.Song.Patterns[0].Cells[3][1] == new ChipCell(ChipDrum.Snare, 8), "Open opens it again");

            GD.Print("Tracker test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Tracker test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }

    /// <summary>A key with no letter to it (Tab, the arrows) pressed and let go.</summary>
    private async Task Press(Key key)
    {
        foreach (bool pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = pressed }, inLocalCoords: true);
        }

        await NextFrame(this);
    }
}
