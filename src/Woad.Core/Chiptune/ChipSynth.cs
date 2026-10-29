namespace Woad.Core.Chiptune;

/// <summary>
/// Plays a <see cref="ChipSong"/> into samples, the way tools/build-assets.py makes the game's songs:
/// 22050 Hz mono, pulse and triangle waves with a quick attack and release, and the same kick, snare,
/// hi-hat and crash. A note sounds until the next note or note-off in its channel (held for its
/// channel's gate of that time); drums ring out. The song is exactly its rows long, so it loops end to end.
/// </summary>
public static class ChipSynth
{
    public const int Rate = 22050;

    /// <summary>A sample at full height is written as this, a little under 16-bit's limit, as build-assets.py does.</summary>
    private const double PeakValue = 32000;

    /// <summary>The whole song, start to end of the order list.</summary>
    public static float[] Render(ChipSong song)
    {
        var samples = new float[(int)Math.Round(song.Seconds * Rate)];
        for (int channel = 0; channel < song.Channels.Count; channel++)
        {
            RenderChannel(song, channel, samples);
        }

        return samples;
    }

    /// <summary>One note on its own, as the tracker plays it when you type it in.</summary>
    public static float[] RenderNote(ChipChannel channel, ChipCell cell, double seconds = 0.35)
    {
        var samples = new float[(int)(seconds * Rate)];
        Sound(channel, cell, seconds / channel.Gate, samples, 0);
        return samples;
    }

    /// <summary>The samples as a 16-bit mono WAV file.</summary>
    public static byte[] Wav(float[] samples)
    {
        byte[] pcm = Pcm16(samples);
        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>The samples as little-endian 16-bit numbers, clipped at full height.</summary>
    public static byte[] Pcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(Math.Clamp(samples[i], -1f, 1f) * PeakValue);
            bytes[2 * i] = (byte)value;
            bytes[2 * i + 1] = (byte)(value >> 8);
        }

        return bytes;
    }

    private static void RenderChannel(ChipSong song, int channelIndex, float[] samples)
    {
        ChipChannel channel = song.Channels[channelIndex];
        var events = new List<(int Row, ChipCell Cell)>();
        int row = 0;
        foreach (int index in song.Order)
        {
            ChipPattern pattern = song.Patterns[index];
            ChipCell[] column = pattern.Cells[channelIndex];
            for (int i = 0; i < column.Length; i++)
            {
                if (column[i].Note != ChipCell.None)
                {
                    events.Add((row + i, column[i]));
                }
            }

            row += pattern.Rows;
        }

        for (int i = 0; i < events.Count; i++)
        {
            (int start, ChipCell cell) = events[i];
            if (!cell.IsNote)
            {
                continue;
            }

            int end = i + 1 < events.Count ? events[i + 1].Row : row;
            Sound(channel, cell, (end - start) * song.RowSeconds, samples, (int)Math.Round(start * song.RowSeconds * Rate));
        }
    }

    /// <summary>A note lasting <paramref name="seconds"/> until the next one, mixed in at <paramref name="at"/>.</summary>
    private static void Sound(ChipChannel channel, ChipCell cell, double seconds, float[] samples, int at)
    {
        double volume = channel.Volume * (cell.Volume < 0 ? 1 : (double)cell.Volume / ChipCell.MaxVolume);
        if (channel.Wave == ChipWave.Noise)
        {
            Drum(cell.Note, volume, samples, at);
            return;
        }

        // As build-assets.py's Song.note: held for the gate, a 5 ms attack, a release of up to 40 ms.
        double held = seconds * channel.Gate;
        double frequency = 440.0 * Math.Pow(2, (cell.Note - 69) / 12.0);
        double release = Math.Min(0.04, held / 3);
        double phase = 0;
        int count = Math.Min((int)(held * Rate), samples.Length - at);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / Rate;
            double wobble = channel.Vibrato && t > 0.25 ? 1 + 0.005 * Math.Sin(2 * Math.PI * 5.5 * t) : 1;
            phase += frequency * wobble / Rate;
            double envelope = Math.Min(1, t / 0.005) * Math.Min(1, (held - t) / release) * Math.Exp(-channel.Decay * t);
            samples[at + i] += (float)(Wave(channel.Wave, phase) * envelope * volume);
        }
    }

    private static double Wave(ChipWave wave, double phase)
    {
        double cycle = phase % 1.0;
        return wave switch
        {
            ChipWave.Pulse12 => cycle < 0.125 ? 1 : -1,
            ChipWave.Pulse25 => cycle < 0.25 ? 1 : -1,
            ChipWave.Pulse50 => cycle < 0.5 ? 1 : -1,
            ChipWave.Triangle => 4 * Math.Abs(cycle - 0.5) - 1,
            _ => 0,
        };
    }

    /// <summary>build-assets.py's drum kit: a falling sine kick, and noise for the snare, hi-hat and crash.</summary>
    private static void Drum(int drum, double volume, float[] samples, int at)
    {
        switch (drum)
        {
            case ChipDrum.Kick:
                Kick(volume * 1.6, samples, at);
                break;
            case ChipDrum.Snare:
                Noise(0.12, volume, decay: 28, seed: 31, lowpass: 0.35, samples, at);
                break;
            case ChipDrum.HiHat:
                Noise(0.03, volume * 0.45, decay: 120, seed: 37, lowpass: 0.05, samples, at);
                break;
            case ChipDrum.Crash:
                Noise(0.9, volume * 0.8, decay: 5, seed: 41, lowpass: 0.08, samples, at);
                break;
        }
    }

    private static void Kick(double volume, float[] samples, int at)
    {
        const double seconds = 0.09, release = 0.05;
        double phase = 0;
        int count = Math.Min((int)(seconds * Rate), samples.Length - at);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / Rate;
            phase += (150 - 1000 * t) / Rate;
            double envelope = Math.Min(1, t / 0.005) * Math.Min(1, (seconds - t) / release);
            samples[at + i] += (float)(Math.Sin(2 * Math.PI * phase) * envelope * volume);
        }
    }

    private static void Noise(double seconds, double volume, double decay, int seed, double lowpass, float[] samples, int at)
    {
        var random = new Random(seed);
        double previous = 0;
        int count = Math.Min((int)(seconds * Rate), samples.Length - at);
        for (int i = 0; i < count; i++)
        {
            previous = previous * lowpass + (random.NextDouble() * 2 - 1) * (1 - lowpass);
            samples[at + i] += (float)(previous * volume * Math.Exp(-decay * i / Rate));
        }
    }
}
