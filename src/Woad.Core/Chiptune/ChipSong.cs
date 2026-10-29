using System.Text.Json;
using System.Text.Json.Serialization;

namespace Woad.Core.Chiptune;

/// <summary>The sound a tracker channel makes: a pulse wave of some width, a triangle, or drums.</summary>
public enum ChipWave
{
    /// <summary>A thin, buzzy pulse: 12.5% of each cycle up.</summary>
    Pulse12,

    /// <summary>A reedy pulse, a quarter of each cycle up: the classic chiptune lead.</summary>
    Pulse25,

    /// <summary>A square wave: hollow and round.</summary>
    Pulse50,

    /// <summary>A soft triangle, for the bass.</summary>
    Triangle,

    /// <summary>Drums: each note is a kick, snare, hi-hat or crash.</summary>
    Noise,
}

/// <summary>The drums a Noise channel plays, numbered as in General MIDI.</summary>
public static class ChipDrum
{
    public const int Kick = 36;
    public const int Snare = 38;
    public const int HiHat = 42;
    public const int Crash = 49;

    public static readonly IReadOnlyList<int> All = [Kick, Snare, HiHat, Crash];

    /// <summary>The three letters a drum shows in the pattern.</summary>
    public static string Name(int drum) => drum switch
    {
        Kick => "KCK",
        Snare => "SNR",
        HiHat => "HAT",
        Crash => "CRS",
        _ => "???",
    };
}

/// <summary>
/// One cell of a pattern: nothing, a note (a MIDI number, or a drum on a Noise channel), or a note-off
/// that silences the channel; with a volume from 0 to 15, or -1 to play at the channel's own volume.
/// </summary>
public readonly record struct ChipCell(int Note, int Volume = -1)
{
    /// <summary>Nothing new: whatever is sounding carries on.</summary>
    public const int None = 0;

    /// <summary>Note-off: the channel falls silent.</summary>
    public const int Off = 1;

    public const int MaxVolume = 15;

    /// <summary>The lowest and highest notes a cell can hold: C0 to B8.</summary>
    public const int LowestNote = 12;
    public const int HighestNote = 119;

    public static ChipCell Empty => new(None);

    public bool IsEmpty => Note == None && Volume < 0;

    public bool IsNote => Note >= LowestNote;

    private static readonly string[] NoteNames = ["C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-"];

    /// <summary>The three letters a note shows: "C-4", "F#5", "===" for note-off, a drum's name, "..." for none.</summary>
    public string NoteText(bool drums) => Note switch
    {
        None => "...",
        Off => "===",
        _ when drums => ChipDrum.Name(Note),
        _ => $"{NoteNames[Note % 12]}{Note / 12 - 1}",
    };

    /// <summary>The one hex digit its volume shows, or "." for the channel's own.</summary>
    public string VolumeText => Volume < 0 ? "." : Volume.ToString("X");

    /// <summary>Four letters: the note and its volume, as kept in a song file ("C-4F", "KCK.", "....").</summary>
    public string Text(bool drums) => NoteText(drums) + VolumeText;

    /// <summary>Reads <see cref="Text"/> back.</summary>
    public static ChipCell Parse(string text)
    {
        if (text.Length != 4)
        {
            throw new FormatException($"A cell is four letters, not '{text}'.");
        }

        string note = text[..3];
        int volume = text[3] == '.' ? -1 : Convert.ToInt32(text[3].ToString(), 16);
        int number = note switch
        {
            "..." => None,
            "===" => Off,
            "KCK" => ChipDrum.Kick,
            "SNR" => ChipDrum.Snare,
            "HAT" => ChipDrum.HiHat,
            "CRS" => ChipDrum.Crash,
            _ => ParseNote(note),
        };
        return new ChipCell(number, volume);
    }

    private static int ParseNote(string note)
    {
        int step = Array.IndexOf(NoteNames, note[..2]);
        if (step < 0 || !int.TryParse(note[2..], out int octave))
        {
            throw new FormatException($"'{note}' isn't a note.");
        }

        return step + 12 * (octave + 1);
    }
}

/// <summary>How a channel sounds: its wave, its loudness, and how each note is shaped.</summary>
public sealed class ChipChannel
{
    public string Name { get; set; } = "";

    public ChipWave Wave { get; set; }

    /// <summary>Loudness at full volume, as a sample's height: the game's songs keep leads near 0.1 and the bass near 0.2.</summary>
    public double Volume { get; set; } = 0.1;

    /// <summary>How much of the time until the next note each note is held for, so notes don't smear together.</summary>
    public double Gate { get; set; } = 0.9;

    /// <summary>How fast a held note fades (0 holds steady): 6 or so plucks it.</summary>
    public double Decay { get; set; }

    /// <summary>A gentle wobble in pitch on notes held longer than a quarter second.</summary>
    public bool Vibrato { get; set; }
}

/// <summary>A block of rows, a column of cells for each channel. Songs play patterns in their order list.</summary>
public sealed class ChipPattern
{
    public ChipPattern(int channels, int rows)
    {
        Cells = [.. Enumerable.Range(0, channels).Select(_ => Enumerable.Repeat(ChipCell.Empty, rows).ToArray())];
    }

    private ChipPattern(ChipCell[][] cells)
    {
        Cells = cells;
    }

    /// <summary>[channel][row].</summary>
    public ChipCell[][] Cells { get; }

    public int Rows => Cells.Length == 0 ? 0 : Cells[0].Length;

    public ChipPattern Copy() => new([.. Cells.Select(column => column.ToArray())]);

    internal static ChipPattern FromColumns(ChipCell[][] cells) => new(cells);
}

/// <summary>
/// A chiptune song for the game, as the tracker edits it: channels, patterns, and the order they play
/// in. It renders to a WAV that loops end to end (see <see cref="ChipSynth"/>), and saves as JSON with
/// each channel's column of a pattern as one line of four-letter cells, easy to read and diff.
/// </summary>
public sealed class ChipSong
{
    public const int DefaultRows = 64;

    /// <summary>Its file name, and the name the game plays it by (<c>Music.Play("name")</c>).</summary>
    public string Name { get; set; } = "";

    public double Bpm { get; set; } = 140;

    /// <summary>Rows to a beat: 4 makes each row a sixteenth note.</summary>
    public int RowsPerBeat { get; set; } = 4;

    public List<ChipChannel> Channels { get; set; } = [];

    public List<ChipPattern> Patterns { get; set; } = [];

    /// <summary>The patterns in the order they play, by index; a pattern can play any number of times.</summary>
    public List<int> Order { get; set; } = [];

    /// <summary>
    /// A new 8-bit song, laid out like the old consoles: two pulse leads, a triangle bass and a noise
    /// drum kit, at the loudness the game's own songs use; one empty pattern.
    /// </summary>
    public static ChipSong EightBit(string name = "")
    {
        var song = new ChipSong
        {
            Name = name,
            Channels =
            [
                new ChipChannel { Name = "Pulse 1", Wave = ChipWave.Pulse25, Volume = 0.1, Vibrato = true },
                new ChipChannel { Name = "Pulse 2", Wave = ChipWave.Pulse12, Volume = 0.06 },
                new ChipChannel { Name = "Triangle", Wave = ChipWave.Triangle, Volume = 0.21, Gate = 0.85 },
                new ChipChannel { Name = "Noise", Wave = ChipWave.Noise, Volume = 0.13 },
            ],
        };
        song.Patterns.Add(new ChipPattern(song.Channels.Count, DefaultRows));
        song.Order.Add(0);
        return song;
    }

    public double RowSeconds => 60.0 / Bpm / RowsPerBeat;

    /// <summary>Every row of the song from start to end, through the order list.</summary>
    public int TotalRows => Order.Sum(index => Patterns[index].Rows);

    public double Seconds => TotalRows * RowSeconds;

    /// <summary>The row of the whole song that a slot in the order list starts at.</summary>
    public int RowAt(int orderSlot) => Order.Take(orderSlot).Sum(index => Patterns[index].Rows);

    /// <summary>A name the game can play and the file system can hold: lowercase letters, digits and underscores.</summary>
    public static string CleanName(string name) =>
        new([.. name.Trim().ToLowerInvariant().Select(letter => char.IsAsciiLetterOrDigit(letter) ? letter : '_')]);

    public string ToJson() => JsonSerializer.Serialize(SongFile.From(this), Options);

    public static ChipSong FromJson(string json) =>
        (JsonSerializer.Deserialize<SongFile>(json, Options) ?? throw new FormatException("The song file is empty.")).ToSong();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The song as saved: patterns as lines of cells.</summary>
    private sealed class SongFile
    {
        public int Version { get; set; } = 1;

        public string Name { get; set; } = "";

        public double Bpm { get; set; }

        public int RowsPerBeat { get; set; }

        public List<ChipChannel> Channels { get; set; } = [];

        /// <summary>[pattern][channel]: the channel's cells down the pattern, separated by spaces.</summary>
        public List<List<string>> Patterns { get; set; } = [];

        public List<int> Order { get; set; } = [];

        public static SongFile From(ChipSong song) => new()
        {
            Name = song.Name,
            Bpm = song.Bpm,
            RowsPerBeat = song.RowsPerBeat,
            Channels = song.Channels,
            Patterns =
            [
                .. song.Patterns.Select(pattern => pattern.Cells
                    .Select((column, channel) => string.Join(' ', column.Select(cell => cell.Text(song.Channels[channel].Wave == ChipWave.Noise))))
                    .ToList()),
            ],
            Order = song.Order,
        };

        public ChipSong ToSong()
        {
            if (Channels.Count == 0 || Patterns.Count == 0 || Order.Count == 0)
            {
                throw new FormatException("A song needs channels, patterns and an order to play them in.");
            }

            var patterns = new List<ChipPattern>();
            foreach (List<string> columns in Patterns)
            {
                if (columns.Count != Channels.Count)
                {
                    throw new FormatException($"Each pattern needs a column for each of the {Channels.Count} channels.");
                }

                ChipCell[][] cells = [.. columns.Select(column => column.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ChipCell.Parse).ToArray())];
                if (cells.Any(column => column.Length != cells[0].Length || column.Length == 0))
                {
                    throw new FormatException("Every channel of a pattern needs the same number of rows.");
                }

                patterns.Add(ChipPattern.FromColumns(cells));
            }

            if (Order.Any(index => index < 0 || index >= patterns.Count))
            {
                throw new FormatException("The order list names a pattern that isn't there.");
            }

            return new ChipSong
            {
                Name = Name,
                Bpm = Bpm > 0 ? Bpm : 140,
                RowsPerBeat = RowsPerBeat > 0 ? RowsPerBeat : 4,
                Channels = Channels,
                Patterns = patterns,
                Order = Order,
            };
        }
    }
}
