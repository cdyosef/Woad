using Woad.Core.Chiptune;
using Xunit;

namespace Woad.Core.Tests.Chiptune;

public class ChipSongTests
{
    [Theory]
    [InlineData("C-4.", 60, -1)]
    [InlineData("F#5A", 78, 10)]
    [InlineData("A-4F", 69, 15)]
    [InlineData("===.", ChipCell.Off, -1)]
    [InlineData("....", ChipCell.None, -1)]
    [InlineData("...8", ChipCell.None, 8)]
    [InlineData("KCK.", ChipDrum.Kick, -1)]
    [InlineData("CRS3", ChipDrum.Crash, 3)]
    public void ACellReadsAndWritesAsFourLetters(string text, int note, int volume)
    {
        ChipCell cell = ChipCell.Parse(text);

        Assert.Equal(new ChipCell(note, volume), cell);
        Assert.Equal(text, cell.Text(drums: text is "KCK." or "CRS3"));
    }

    [Fact]
    public void ASongSurvivesBeingSavedAndOpened()
    {
        ChipSong song = ChipSong.EightBit("tune");
        song.Bpm = 168;
        song.Patterns[0].Cells[0][0] = new ChipCell(69);
        song.Patterns[0].Cells[0][8] = new ChipCell(ChipCell.Off);
        song.Patterns[0].Cells[3][4] = new ChipCell(ChipDrum.Snare, 12);
        song.Patterns.Add(song.Patterns[0].Copy());
        song.Order = [0, 1, 0];

        ChipSong opened = ChipSong.FromJson(song.ToJson());

        Assert.Equal("tune", opened.Name);
        Assert.Equal(168, opened.Bpm);
        Assert.Equal([0, 1, 0], opened.Order);
        Assert.Equal(ChipWave.Noise, opened.Channels[3].Wave);
        Assert.Equal(new ChipCell(69), opened.Patterns[1].Cells[0][0]);
        Assert.Equal(new ChipCell(ChipCell.Off), opened.Patterns[1].Cells[0][8]);
        Assert.Equal(new ChipCell(ChipDrum.Snare, 12), opened.Patterns[0].Cells[3][4]);
        Assert.Equal(song.ToJson(), opened.ToJson());
    }

    [Fact]
    public void ASongFileNamingAMissingPatternIsRefused()
    {
        ChipSong song = ChipSong.EightBit("tune");
        song.Order = [3];

        Assert.Throws<FormatException>(() => ChipSong.FromJson(song.ToJson()));
    }

    [Theory]
    [InlineData("Bridge Troll!", "bridge_troll_")]
    [InlineData("  icarus ", "icarus")]
    public void ASongsNameIsCleanedForTheGame(string name, string clean) => Assert.Equal(clean, ChipSong.CleanName(name));

    [Fact]
    public void ASongIsExactlyItsRowsLongSoItLoopsEndToEnd()
    {
        // 120 bpm, 4 rows a beat: an eighth of a second a row; 64 rows twice over is 16 seconds.
        ChipSong song = ChipSong.EightBit("tune");
        song.Bpm = 120;
        song.Order = [0, 0];

        float[] samples = ChipSynth.Render(song);

        Assert.Equal(16 * ChipSynth.Rate, samples.Length);
        Assert.All(samples, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void ANoteSoundsAtItsPitchUntilItsNoteOff()
    {
        // A4 (440 Hz) on a square wave from row 0, cut off at row 8: one second at 120 bpm.
        ChipSong song = ChipSong.EightBit("tune");
        song.Bpm = 120;
        song.Channels[0].Wave = ChipWave.Pulse50;
        song.Channels[0].Vibrato = false;
        song.Patterns[0].Cells[0][0] = new ChipCell(69);
        song.Patterns[0].Cells[0][8] = new ChipCell(ChipCell.Off);

        float[] samples = ChipSynth.Render(song);

        // Each cycle of a square wave crosses zero upward once: 440 a second, give or take one at the edges.
        int rises = Enumerable.Range(1, ChipSynth.Rate / 2).Count(i => samples[i - 1] <= 0 && samples[i] > 0);
        Assert.InRange(rises, 219, 221);
        Assert.Equal(0.1f, samples[ChipSynth.Rate / 4], 3);
        Assert.All(samples[ChipSynth.Rate..], sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void ACellsVolumeScalesItsChannels()
    {
        ChipChannel channel = new() { Wave = ChipWave.Pulse50, Volume = 0.3 };

        float loud = ChipSynth.RenderNote(channel, new ChipCell(69)).Max();
        float quiet = ChipSynth.RenderNote(channel, new ChipCell(69, 5)).Max();

        Assert.Equal(0.3f, loud, 3);
        Assert.Equal(0.1f, quiet, 3);
    }

    [Fact]
    public void DrumsRingOutAndTheSameDrumSoundsTheSameEachTime()
    {
        ChipSong song = ChipSong.EightBit("tune");
        song.Bpm = 120;
        song.Patterns[0].Cells[3][0] = new ChipCell(ChipDrum.Snare);
        song.Patterns[0].Cells[3][16] = new ChipCell(ChipDrum.Snare);

        float[] samples = ChipSynth.Render(song);

        int beat = 16 * ChipSynth.Rate / 8;
        Assert.Contains(samples[..beat], sample => sample != 0);
        Assert.Equal(samples[..2000], samples[beat..(beat + 2000)]);
    }

    [Fact]
    public void AWavFileHoldsTheSamplesAs16BitMono()
    {
        byte[] wav = ChipSynth.Wav([0f, 1f, -1f, 0.5f]);

        Assert.Equal(44 + 8, wav.Length);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(ChipSynth.Rate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(32000, BitConverter.ToInt16(wav, 46));
        Assert.Equal(-32000, BitConverter.ToInt16(wav, 48));
    }
}
