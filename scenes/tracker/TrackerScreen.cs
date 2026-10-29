using Godot;
using Woad.Core.Chiptune;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Tracker;

/// <summary>
/// The Music Tracker: a chiptune editor for the game's songs, open from the main menu when the game runs
/// from the Godot editor. Notes go in from the keyboard like a piano (Z to M, Q to U an octave up);
/// patterns play in the order list down the left. Save keeps the song in songs/ to edit again; Export
/// also renders it to assets/audio/music/, where <c>Music.Play("name")</c> finds it, looping end to end.
/// </summary>
public partial class TrackerScreen : MenuScreen
{
    private const int MaxOrderShown = 11;
    private const string Help = "Z-M, Q-U: notes  1: note off  Del: clear  Arrows, Tab: move  [ ]: octave  Space: play  Ctrl+S: save";

    /// <summary>Piano keys: the letter to a semitone above the octave's C, two octaves across the keyboard.</summary>
    private static readonly Dictionary<char, int> PianoKeys = new()
    {
        ['z'] = 0, ['s'] = 1, ['x'] = 2, ['d'] = 3, ['c'] = 4, ['v'] = 5, ['g'] = 6, ['b'] = 7, ['h'] = 8, ['n'] = 9, ['j'] = 10, ['m'] = 11,
        [','] = 12, ['l'] = 13, ['.'] = 14, [';'] = 15, ['/'] = 16,
        ['q'] = 12, ['2'] = 13, ['w'] = 14, ['3'] = 15, ['e'] = 16, ['r'] = 17, ['5'] = 18, ['t'] = 19, ['6'] = 20, ['y'] = 21, ['7'] = 22, ['u'] = 23,
        ['i'] = 24, ['9'] = 25, ['o'] = 26, ['0'] = 27, ['p'] = 28,
    };

    /// <summary>On the drum channel the first four piano keys are the kit.</summary>
    private static readonly Dictionary<char, int> DrumKeys = new()
    {
        ['z'] = ChipDrum.Kick, ['x'] = ChipDrum.Snare, ['c'] = ChipDrum.HiHat, ['v'] = ChipDrum.Crash,
    };

    private static readonly ChipWave[] PulseWaves = [ChipWave.Pulse12, ChipWave.Pulse25, ChipWave.Pulse50];

    public event Action? BackRequested;

    /// <summary>Where songs are saved to be edited again (a folder Godot leaves alone).</summary>
    public string SongsDir { get; init; } = ProjectSettings.GlobalizePath("res://songs");

    /// <summary>Where Export puts the rendered songs for the game to play.</summary>
    public string MusicDir { get; init; } = ProjectSettings.GlobalizePath("res://assets/audio/music");

    internal ChipSong Song { get; private set; } = ChipSong.EightBit();

    internal PatternGrid Grid { get; private set; } = null!;

    internal LineEdit NameEdit { get; private set; } = null!;

    internal Button ExportButton { get; private set; } = null!;

    internal Button SaveButton { get; private set; } = null!;

    internal Button PlayButton { get; private set; } = null!;

    internal Button StopButton { get; private set; } = null!;

    internal Label Status { get; private set; } = null!;

    /// <summary>The slot of the order list being edited.</summary>
    internal int Slot { get; private set; }

    internal int Octave { get; private set; } = 4;

    /// <summary>Rows the cursor moves down after each note.</summary>
    internal int Step { get; private set; } = 1;

    private readonly List<Button> _slotButtons = [];
    private readonly List<(Button Wave, Button Vibrato)> _channelButtons = [];
    private Label _bpmLabel = null!, _octaveLabel = null!, _stepLabel = null!;
    private Panel _openPanel = null!;
    private AudioStreamPlayer _notePlayer = null!;
    private int _orderScroll;

    /// <summary>Edits not yet saved: New, Open and Back ask twice before throwing them away.</summary>
    private bool _dirty;
    private string? _confirming;

    /// <summary>What's playing: the song as it was when Play was pressed, and the order slots it went through.</summary>
    private ChipSong? _playing;
    private List<int> _playingSlots = [];

    public override void _Ready()
    {
        BuildFrame("Music Tracker");

        float x = 300;
        foreach ((string text, Action action) in new (string, Action)[] { ("New", NewSong), ("Open", ShowOpen), ("Save", () => Save()), ("Export", Export), ("Back", Back) })
        {
            Button button = Add(UiTheme.Button(new Vector2(x, 14), new Vector2(62, 20), text));
            button.Pressed += action;
            SaveButton = text == "Save" ? button : SaveButton;
            ExportButton = text == "Export" ? button : ExportButton;
            x += 64;
        }

        Add(UiTheme.Label(new Vector2(20, 46), new Vector2(32, 14), "Name", color: Palette.Trim));
        NameEdit = Add(new LineEdit { Position = new Vector2(48, 39), Size = new Vector2(96, 20), MaxLength = 32, PlaceholderText = "song name" });
        NameEdit.TextChanged += name =>
        {
            Song.Name = ChipSong.CleanName(name);
            _dirty = true;
        };
        NameEdit.TextSubmitted += _ => Grid.GrabFocus();

        _bpmLabel = Stepper(152, "BPM", () => SetBpm(Song.Bpm - 2), () => SetBpm(Song.Bpm + 2));
        _octaveLabel = Stepper(244, "Octave", () => SetOctave(Octave - 1), () => SetOctave(Octave + 1));
        _stepLabel = Stepper(336, "Step", () => Step = Math.Max(0, Step - 1), () => Step = Math.Min(16, Step + 1));

        PlayButton = Add(UiTheme.Button(new Vector2(432, 44), new Vector2(62, 18), "Play"));
        PlayButton.TooltipText = "Play the song from the cursor (Space)";
        PlayButton.Pressed += () => PlayFrom(Slot, Grid.CursorRow);
        Button pattern = Add(UiTheme.Button(new Vector2(496, 44), new Vector2(62, 18), "Pattern"));
        pattern.TooltipText = "Play this pattern over and over";
        pattern.Pressed += PlayPattern;
        StopButton = Add(UiTheme.Button(new Vector2(560, 44), new Vector2(62, 18), "Stop"));
        StopButton.Pressed += StopPlaying;

        Caption(new Vector2(20, 76), "Order", 70);
        for (int i = 0; i < MaxOrderShown; i++)
        {
            int shown = i;
            Button button = Add(UiTheme.CompactButton(new Vector2(20, 90 + i * 16), new Vector2(72, 15), ""));
            button.Pressed += () => SelectSlot(_orderScroll + shown);
            _slotButtons.Add(button);
        }

        OrderButton(20, 272, 34, "<", "Play the pattern before it here", () => ChangeSlotPattern(-1));
        OrderButton(58, 272, 34, ">", "Play the pattern after it here (a new one past the last)", () => ChangeSlotPattern(1));
        OrderButton(20, 290, 34, "New", "A new, empty pattern after this one", () => InsertSlot(new ChipPattern(Song.Channels.Count, ChipSong.DefaultRows)));
        OrderButton(58, 290, 34, "Copy", "A copy of this pattern after it, to change", () => InsertSlot(Song.Patterns[Song.Order[Slot]].Copy()));
        OrderButton(20, 308, 34, "Again", "Play this same pattern again after it", () => InsertSlot(null));
        OrderButton(58, 308, 34, "Del", "Take this slot out of the order", DeleteSlot);

        Grid = Add(new PatternGrid { Position = new Vector2(100, 76), Size = new Vector2(524, 250), KeyPressed = Key });
        Grid.CellClicked += (channel, row, field) => MoveCursor(channel, row, field);
        for (int channel = 0; channel < 4; channel++)
        {
            _channelButtons.Add(ChannelButtons(channel));
        }

        Status = Add(UiTheme.Label(new Vector2(20, 330), new Vector2(604, 14), Help, 10, Palette.TextDim));
        Status.AutowrapMode = TextServer.AutowrapMode.Off;
        Status.ClipText = true;

        _notePlayer = new AudioStreamPlayer { Bus = AudioSettings.MusicBus, VolumeDb = -10 };
        AddChild(_notePlayer);
        BuildOpenPanel();

        Music.Stop();
        Load(ChipSong.EightBit());
        Grid.GrabFocus();
    }

    public override void _ExitTree()
    {
        if (Music.Current == Music.PreviewName)
        {
            Music.Cut();
        }
    }

    public override void _Process(double delta)
    {
        if (_playing is null || Music.Current != Music.PreviewName)
        {
            if (Grid.PlayRow >= 0)
            {
                Grid.PlayRow = -1;
                Grid.QueueRedraw();
            }

            _playing = null;
            return;
        }

        // Follow the song: show the pattern playing, and light its row.
        int row = (int)(Music.Position / _playing.RowSeconds) % Math.Max(1, _playing.TotalRows);
        for (int i = 0; i < _playingSlots.Count; i++)
        {
            int rows = _playing.Patterns[_playing.Order[i]].Rows;
            if (row < rows)
            {
                if (_playingSlots[i] != Slot)
                {
                    SelectSlot(_playingSlots[i], keepCursor: true);
                }

                if (Grid.PlayRow != row)
                {
                    Grid.PlayRow = row;
                    Grid.Follow(row);
                }

                return;
            }

            row -= rows;
        }
    }

    /// <summary>Opens a song file into the tracker.</summary>
    internal void Load(ChipSong song)
    {
        StopPlaying();
        Song = song;
        Grid.Song = song;
        NameEdit.Text = song.Name;
        _dirty = false;
        _orderScroll = 0;
        SelectSlot(0);
        MoveCursor(0, 0, 0);
        Refresh();
    }

    // ---- The keyboard ------------------------------------------------------------------------

    /// <summary>What a key does in the pattern; false for keys it leaves alone.</summary>
    internal bool Key(InputEventKey key)
    {
        if (key.CtrlPressed && key.Keycode == Godot.Key.S)
        {
            Save();
            return true;
        }

        switch (key.Keycode)
        {
            case Godot.Key.Up:
                MoveCursor(Grid.CursorChannel, Grid.CursorRow - 1, Grid.CursorField);
                return true;
            case Godot.Key.Down:
                MoveCursor(Grid.CursorChannel, Grid.CursorRow + 1, Grid.CursorField);
                return true;
            case Godot.Key.Pageup:
                MoveCursor(Grid.CursorChannel, Grid.CursorRow - Song.RowsPerBeat * 4, Grid.CursorField);
                return true;
            case Godot.Key.Pagedown:
                MoveCursor(Grid.CursorChannel, Grid.CursorRow + Song.RowsPerBeat * 4, Grid.CursorField);
                return true;
            case Godot.Key.Home:
                MoveCursor(Grid.CursorChannel, 0, Grid.CursorField);
                return true;
            case Godot.Key.End:
                MoveCursor(Grid.CursorChannel, Grid.Pattern.Rows - 1, Grid.CursorField);
                return true;
            case Godot.Key.Left:
                Sideways(-1);
                return true;
            case Godot.Key.Right:
                Sideways(1);
                return true;
            case Godot.Key.Tab:
                MoveCursor((Grid.CursorChannel + (key.ShiftPressed ? Song.Channels.Count - 1 : 1)) % Song.Channels.Count, Grid.CursorRow, 0);
                return true;
            case Godot.Key.Delete or Godot.Key.Backspace:
                Write(Grid.CursorField == 0 ? ChipCell.Empty : Cell with { Volume = -1 });
                return true;
            case Godot.Key.Space:
                if (_playing is null)
                {
                    PlayFrom(Slot, Grid.CursorRow);
                }
                else
                {
                    StopPlaying();
                }

                return true;
        }

        char letter = char.ToLowerInvariant((char)key.Unicode);
        if (letter == '[' || letter == ']')
        {
            SetOctave(Octave + (letter == '[' ? -1 : 1));
            return true;
        }

        return Grid.CursorField == 0 ? NoteKey(letter) : VolumeKey(letter);
    }

    private bool NoteKey(char letter)
    {
        ChipChannel channel = Song.Channels[Grid.CursorChannel];
        if (letter == '1')
        {
            Write(Cell with { Note = ChipCell.Off });
            return true;
        }

        int note;
        if (channel.Wave == ChipWave.Noise)
        {
            if (!DrumKeys.TryGetValue(letter, out note))
            {
                return false;
            }
        }
        else if (PianoKeys.TryGetValue(letter, out int semitone))
        {
            note = Math.Clamp(12 * (Octave + 1) + semitone, ChipCell.LowestNote, ChipCell.HighestNote);
        }
        else
        {
            return false;
        }

        ChipCell cell = Cell with { Note = note };
        Audition(channel, cell);
        Write(cell);
        return true;
    }

    private bool VolumeKey(char letter)
    {
        int volume = Uri.IsHexDigit(letter) ? Convert.ToInt32(letter.ToString(), 16) : letter == '.' ? -1 : -2;
        if (volume == -2)
        {
            return false;
        }

        Write(Cell with { Volume = volume });
        return true;
    }

    private ChipCell Cell => Grid.Pattern.Cells[Grid.CursorChannel][Grid.CursorRow];

    /// <summary>Puts a cell under the cursor and steps down.</summary>
    private void Write(ChipCell cell)
    {
        Grid.Pattern.Cells[Grid.CursorChannel][Grid.CursorRow] = cell;
        _dirty = true;
        MoveCursor(Grid.CursorChannel, Math.Min(Grid.Pattern.Rows - 1, Grid.CursorRow + Step), Grid.CursorField);
    }

    /// <summary>Plays a note on its own as it goes in.</summary>
    private void Audition(ChipChannel channel, ChipCell cell)
    {
        _notePlayer.Stream = Stream(ChipSynth.RenderNote(channel, cell), loop: false);
        _notePlayer.Play();
    }

    private void Sideways(int direction)
    {
        int field = Grid.CursorField + direction;
        int channel = Grid.CursorChannel;
        if (field is < 0 or > 1)
        {
            channel = (channel + direction + Song.Channels.Count) % Song.Channels.Count;
            field = direction > 0 ? 0 : 1;
        }

        MoveCursor(channel, Grid.CursorRow, field);
    }

    internal void MoveCursor(int channel, int row, int field)
    {
        Grid.CursorChannel = Math.Clamp(channel, 0, Song.Channels.Count - 1);
        Grid.CursorRow = Math.Clamp(row, 0, Grid.Pattern.Rows - 1);
        Grid.CursorField = field;
        Grid.Follow(Grid.CursorRow);
    }

    // ---- The song's settings -----------------------------------------------------------------------

    private Label Stepper(float x, string name, Action down, Action up)
    {
        Add(UiTheme.Label(new Vector2(x, 46), new Vector2(40, 14), name, color: Palette.Trim));
        float left = x + (name.Length > 4 ? 34 : 24);
        Button minus = Add(UiTheme.CompactButton(new Vector2(left, 44), new Vector2(14, 18), "-"));
        Label value = Add(UiTheme.Label(new Vector2(left + 15, 46), new Vector2(26, 14), ""));
        value.HorizontalAlignment = HorizontalAlignment.Center;
        Button plus = Add(UiTheme.CompactButton(new Vector2(left + 42, 44), new Vector2(14, 18), "+"));
        minus.Pressed += () =>
        {
            down();
            Refresh();
        };
        plus.Pressed += () =>
        {
            up();
            Refresh();
        };
        return value;
    }

    private void SetBpm(double bpm)
    {
        Song.Bpm = Math.Clamp(bpm, 40, 300);
        _dirty = true;
    }

    private void SetOctave(int octave)
    {
        Octave = Math.Clamp(octave, 0, 8);
        Refresh();
    }

    private (Button Wave, Button Vibrato) ChannelButtons(int channel)
    {
        float left = Grid.Position.X + PatternGrid.NumberWidth + channel * Grid.ChannelWidth + 3;
        float y = Grid.Position.Y + 13;
        Button wave = Add(UiTheme.CompactButton(new Vector2(left, y), new Vector2(46, 15), ""));
        wave.TooltipText = "The pulse's width: thin and buzzy to round and hollow";
        wave.Pressed += () =>
        {
            ChipChannel settings = Song.Channels[channel];
            int at = Array.IndexOf(PulseWaves, settings.Wave);
            if (at >= 0)
            {
                settings.Wave = PulseWaves[(at + 1) % PulseWaves.Length];
                _dirty = true;
                Audition(settings, new ChipCell(12 * (Octave + 1) + 9));
                Refresh();
            }
        };

        Button quieter = Add(UiTheme.CompactButton(new Vector2(left + 48, y), new Vector2(14, 15), "-"));
        quieter.TooltipText = "Quieter";
        quieter.Pressed += () => ChangeVolume(channel, 1 / 1.25);
        Button louder = Add(UiTheme.CompactButton(new Vector2(left + 63, y), new Vector2(14, 15), "+"));
        louder.TooltipText = "Louder";
        louder.Pressed += () => ChangeVolume(channel, 1.25);

        Button vibrato = Add(UiTheme.CompactButton(new Vector2(left + 79, y), new Vector2(38, 15), "Vib"));
        vibrato.TooltipText = "Vibrato: a wobble on held notes";
        vibrato.ToggleMode = true;
        vibrato.Toggled += on =>
        {
            if (Song.Channels[channel].Vibrato != on)
            {
                Song.Channels[channel].Vibrato = on;
                _dirty = true;
            }
        };
        return (wave, vibrato);
    }

    private void ChangeVolume(int channel, double by)
    {
        ChipChannel settings = Song.Channels[channel];
        settings.Volume = Math.Clamp(Math.Round(settings.Volume * by, 3), 0.005, 0.5);
        _dirty = true;
        Refresh();
    }

    // ---- The order list ------------------------------------------------------------------------

    private void OrderButton(float x, float y, float width, string text, string tooltip, Action action)
    {
        Button button = Add(UiTheme.CompactButton(new Vector2(x, y), new Vector2(width, 16), text));
        button.TooltipText = tooltip;
        button.Pressed += () =>
        {
            action();
            _dirty = true;
            Refresh();
        };
    }

    internal void SelectSlot(int slot, bool keepCursor = false)
    {
        Slot = Math.Clamp(slot, 0, Song.Order.Count - 1);
        Grid.PatternIndex = Song.Order[Slot];
        _orderScroll = Math.Clamp(_orderScroll, Math.Max(0, Slot - MaxOrderShown + 1), Slot);
        if (!keepCursor)
        {
            Grid.PlayRow = -1;
        }

        MoveCursor(Grid.CursorChannel, Grid.CursorRow, Grid.CursorField);
        Refresh();
    }

    /// <summary>A slot after this one: a pattern of its own, or (null) this same pattern again.</summary>
    internal void InsertSlot(ChipPattern? pattern)
    {
        int index = Song.Order[Slot];
        if (pattern is not null)
        {
            Song.Patterns.Add(pattern);
            index = Song.Patterns.Count - 1;
        }

        Song.Order.Insert(Slot + 1, index);
        SelectSlot(Slot + 1);
    }

    private void DeleteSlot()
    {
        if (Song.Order.Count > 1)
        {
            Song.Order.RemoveAt(Slot);
            SelectSlot(Math.Min(Slot, Song.Order.Count - 1));
        }
    }

    private void ChangeSlotPattern(int by)
    {
        int index = Song.Order[Slot] + by;
        if (index < 0)
        {
            return;
        }

        if (index >= Song.Patterns.Count)
        {
            Song.Patterns.Add(new ChipPattern(Song.Channels.Count, ChipSong.DefaultRows));
            index = Song.Patterns.Count - 1;
        }

        Song.Order[Slot] = index;
        SelectSlot(Slot);
    }

    private void Refresh()
    {
        _bpmLabel.Text = $"{Song.Bpm:0}";
        _octaveLabel.Text = $"{Octave}";
        _stepLabel.Text = $"{Step}";
        for (int i = 0; i < _slotButtons.Count; i++)
        {
            int slot = _orderScroll + i;
            Button button = _slotButtons[i];
            button.Visible = slot < Song.Order.Count;
            if (button.Visible)
            {
                button.Text = $"{slot:D2}:  Pattern {Song.Order[slot]:D2}";
                button.AddThemeColorOverride("font_color", slot == Slot ? Palette.TrimBright : Palette.Text);
                button.Modulate = slot == Slot ? Colors.White : new Color(1, 1, 1, 0.75f);
            }
        }

        for (int channel = 0; channel < _channelButtons.Count; channel++)
        {
            bool shown = channel < Song.Channels.Count;
            (Button wave, Button vibrato) = _channelButtons[channel];
            wave.Visible = shown;
            vibrato.Visible = shown && Song.Channels[channel].Wave != ChipWave.Noise;
            if (shown)
            {
                ChipChannel settings = Song.Channels[channel];
                wave.Text = settings.Wave switch
                {
                    ChipWave.Pulse12 => "Pulse 12%",
                    ChipWave.Pulse25 => "Pulse 25%",
                    ChipWave.Pulse50 => "Square",
                    ChipWave.Triangle => "Triangle",
                    _ => "Drums",
                };
                wave.Disabled = Array.IndexOf(PulseWaves, settings.Wave) < 0;
                vibrato.SetPressedNoSignal(settings.Vibrato);
            }
        }

        Grid.QueueRedraw();
    }

    // ---- Playing -------------------------------------------------------------------------------

    /// <summary>Plays the whole song from a row of a slot of the order list, looping.</summary>
    internal void PlayFrom(int slot, int row)
    {
        Play(Song, [.. Enumerable.Range(0, Song.Order.Count)], (Song.RowAt(slot) + row) * Song.RowSeconds);
    }

    private void PlayPattern()
    {
        ChipSong single = ChipSong.FromJson(Song.ToJson());
        single.Order = [Song.Order[Slot]];
        Play(single, [Slot], 0);
    }

    private void Play(ChipSong song, List<int> slots, double fromSeconds)
    {
        _playing = ChipSong.FromJson(song.ToJson());
        _playingSlots = slots;
        Music.Preview(Stream(ChipSynth.Render(_playing), loop: true), fromSeconds);
        Grid.GrabFocus();
    }

    internal void StopPlaying()
    {
        _playing = null;
        if (Music.Current == Music.PreviewName)
        {
            Music.Cut();
        }
    }

    private static AudioStreamWav Stream(float[] samples, bool loop) => new()
    {
        Format = AudioStreamWav.FormatEnum.Format16Bits,
        MixRate = ChipSynth.Rate,
        Stereo = false,
        Data = ChipSynth.Pcm16(samples),
        LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
        LoopBegin = 0,
        LoopEnd = loop ? samples.Length : 0,
    };

    // ---- Files ---------------------------------------------------------------------------------

    private string SongPath => Path.Combine(SongsDir, $"{Song.Name}.json");

    private string WavPath => Path.Combine(MusicDir, $"{Song.Name}.wav");

    /// <summary>Saves the song file; false (and why, on the status line) when it can't be.</summary>
    internal bool Save()
    {
        if (Song.Name.Length == 0)
        {
            Say("Give the song a name first.", Palette.Danger);
            NameEdit.GrabFocus();
            return false;
        }

        Directory.CreateDirectory(SongsDir);
        string ignore = Path.Combine(SongsDir, ".gdignore");
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, "");
        }

        File.WriteAllText(SongPath, Song.ToJson());
        _dirty = false;
        Say($"Saved songs/{Song.Name}.json.", Palette.Heal);
        return true;
    }

    /// <summary>Saves the song, then renders it into the game's music: asks first before replacing a song that wasn't made here.</summary>
    internal void Export()
    {
        if (Song.Name.Length == 0)
        {
            Say("Give the song a name first.", Palette.Danger);
            NameEdit.GrabFocus();
            return;
        }

        if (File.Exists(WavPath) && !File.Exists(SongPath) && !Confirm($"export {Song.Name}"))
        {
            Say($"The game already has music called {Song.Name}, not made here. Export again to replace it.", Palette.Danger);
            return;
        }

        if (!Save())
        {
            return;
        }

        Directory.CreateDirectory(MusicDir);
        File.WriteAllBytes(WavPath, ChipSynth.Wav(ChipSynth.Render(Song)));
        Say($"Exported {Song.Name}.wav ({Song.Seconds:0.0}s). The Godot editor imports it next time it has focus; then Music.Play(\"{Song.Name}\").", Palette.Heal);
    }

    private void NewSong()
    {
        if (!_dirty || Confirm("new"))
        {
            Load(ChipSong.EightBit());
            Say(Help, Palette.TextDim);
        }
        else
        {
            Say("This song has changes that aren't saved. Press New again to start over anyway.", Palette.Danger);
        }
    }

    private void Back()
    {
        if (!_dirty || Confirm("back"))
        {
            StopPlaying();
            BackRequested?.Invoke();
        }
        else
        {
            Say("This song has changes that aren't saved. Press Back again to leave anyway.", Palette.Danger);
        }
    }

    /// <summary>True the second time in a row the same thing is asked for.</summary>
    private bool Confirm(string what)
    {
        bool sure = _confirming == what;
        _confirming = sure ? null : what;
        return sure;
    }

    private void Say(string text, Color color)
    {
        if (!text.Contains("again"))
        {
            _confirming = null;
        }

        Status.Text = text;
        Status.AddThemeColorOverride("font_color", color);
    }

    private void BuildOpenPanel()
    {
        _openPanel = Add(new Panel { Position = new Vector2(180, 70), Size = new Vector2(280, 230), Visible = false });
    }

    private void ShowOpen()
    {
        if (_dirty && !Confirm("open"))
        {
            Say("This song has changes that aren't saved. Press Open again to open another anyway.", Palette.Danger);
            return;
        }

        foreach (Node child in _openPanel.GetChildren())
        {
            child.QueueFree();
        }

        _openPanel.AddChild(UiTheme.Heading(UiTheme.Label(new Vector2(12, 8), new Vector2(200, 16), "Open a song", 9, Palette.TrimBright), 1));
        string[] files = Directory.Exists(SongsDir) ? [.. Directory.GetFiles(SongsDir, "*.json").Order()] : [];
        if (files.Length == 0)
        {
            _openPanel.AddChild(UiTheme.Label(new Vector2(12, 30), new Vector2(256, 30), "No songs saved yet: they go in the songs folder.", color: Palette.TextDim));
        }

        for (int i = 0; i < Math.Min(files.Length, 9); i++)
        {
            string file = files[i];
            Button button = UiTheme.Button(new Vector2(12, 28 + i * 20), new Vector2(256, 18), Path.GetFileNameWithoutExtension(file));
            button.Pressed += () => Open(file);
            _openPanel.AddChild(button);
        }

        Button cancel = UiTheme.Button(new Vector2(196, 204), new Vector2(72, 18), "Cancel");
        cancel.Pressed += () =>
        {
            _openPanel.Visible = false;
            Grid.GrabFocus();
        };
        _openPanel.AddChild(cancel);
        _openPanel.Visible = true;
        MoveChild(_openPanel, -1);
    }

    internal void Open(string file)
    {
        _openPanel.Visible = false;
        try
        {
            ChipSong song = ChipSong.FromJson(File.ReadAllText(file));
            song.Name = ChipSong.CleanName(song.Name.Length > 0 ? song.Name : Path.GetFileNameWithoutExtension(file));
            Load(song);
            Say($"Opened {song.Name}.", Palette.Heal);
        }
        catch (Exception problem) when (problem is IOException or FormatException or System.Text.Json.JsonException)
        {
            Say($"Couldn't open {Path.GetFileName(file)}: {problem.Message}", Palette.Danger);
        }

        Grid.GrabFocus();
    }
}
