using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// Plays the generated sound effects (res://assets/audio/*.wav) from anywhere: <c>Sfx.Play("hit")</c>.
/// One instance lives under Main; with none, or a missing file, playing is silently skipped.
/// </summary>
public partial class Sfx : Node
{
    private const int Voices = 8;
    private const float VolumeDb = -8;

    private static Sfx? _instance;

    private readonly List<AudioStreamPlayer> _players = [];
    private readonly Dictionary<string, AudioStream?> _streams = [];
    private int _next;

    public override void _EnterTree()
    {
        _instance = this;
        for (int i = 0; i < Voices; i++)
        {
            var player = new AudioStreamPlayer { VolumeDb = VolumeDb, Bus = AudioSettings.SfxBus };
            AddChild(player);
            _players.Add(player);
        }
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }

        // Let go of the sounds now rather than whenever the garbage collector runs, so Godot
        // doesn't report them as still in use when the app quits.
        foreach (AudioStreamPlayer player in _players)
        {
            player.Stop();
            player.Stream = null;
        }

        foreach (AudioStream? stream in _streams.Values)
        {
            stream?.Dispose();
        }

        _streams.Clear();
    }

    /// <param name="pitch">1 is normal; small random variations keep repeated sounds from droning.</param>
    public static void Play(string name, float pitch = 1)
    {
        if (_instance is null || _instance.Stream(name) is not { } stream)
        {
            return;
        }

        AudioStreamPlayer player = _instance._players.FirstOrDefault(voice => !voice.Playing)
            ?? _instance._players[_instance._next++ % Voices];
        player.Stream = stream;
        player.PitchScale = pitch;
        player.Play();
    }

    /// <summary>
    /// Stops all sound and music, gives the audio thread a moment to let go of it, then quits.
    /// Quitting while sounds play makes Godot report them as resources still in use.
    /// </summary>
    public static async void QuitGame(Node context, int exitCode = 0)
    {
        Music.Silence();
        if (_instance is not null)
        {
            foreach (AudioStreamPlayer player in _instance._players)
            {
                player.Stop();
                player.Stream = null;
            }
        }

        SceneTree tree = context.GetTree();
        await context.ToSignal(tree.CreateTimer(0.15, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
        tree.Quit(exitCode);
    }

    /// <summary>Plays with a slightly random pitch, for sounds heard over and over (steps, dice).</summary>
    public static void PlayVaried(string name) => Play(name, (float)GD.RandRange(0.9, 1.1));

    private AudioStream? Stream(string name)
    {
        if (!_streams.TryGetValue(name, out AudioStream? stream))
        {
            string path = $"res://assets/audio/{name}.wav";
            stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
            _streams[name] = stream;
        }

        return stream;
    }
}
