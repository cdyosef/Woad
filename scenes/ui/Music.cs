using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// Plays the songs in res://assets/audio/music/ from anywhere, crossfading between them:
/// <c>Music.Play("dungeon")</c>. Asking for the song already playing does nothing, so callers can
/// ask whenever the mood might have changed. A song can be .ogg, .mp3 or .wav (generated ones are
/// .wav); with none, or no instance under Main, it stays silent.
/// </summary>
public partial class Music : Node
{
    private const float VolumeDb = -10;
    private const double FadeSeconds = 0.8;
    private static readonly string[] Extensions = ["ogg", "mp3", "wav"];

    private static Music? _instance;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    private readonly Dictionary<string, AudioStream?> _streams = [];
    private int _active;
    private Tween? _fade;

    /// <summary>The song playing or fading in, or null when the music is stopped.</summary>
    public static string? Current { get; private set; }

    public override void _EnterTree()
    {
        _instance = this;
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = AudioSettings.MusicBus, VolumeLinear = 0 };
            AddChild(_players[i]);
        }
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
            Current = null;
        }

        // As with Sfx: let go of the songs now so Godot doesn't report them as leaked on quit.
        StopNow();
        foreach (AudioStream? stream in _streams.Values)
        {
            stream?.Dispose();
        }

        _streams.Clear();
    }

    /// <param name="loop">False for short tunes that play once, like the results fanfare.</param>
    public static void Play(string name, bool loop = true)
    {
        if (_instance is null || Current == name)
        {
            return;
        }

        Current = name;
        _instance.FadeTo(_instance.Stream(name, loop));
    }

    /// <summary>Fades the music out.</summary>
    public static void Stop()
    {
        if (_instance is null || Current is null)
        {
            return;
        }

        Current = null;
        _instance.FadeTo(null);
    }

    /// <summary>Cuts the music dead, mid-note (the Icarus Wings breaking).</summary>
    public static void Cut()
    {
        Current = null;
        _instance?.StopNow();
    }

    /// <summary>Cuts the music off at once and lets go of it (see <see cref="Sfx.QuitGame"/>).</summary>
    internal static void Silence()
    {
        Current = null;
        _instance?.StopNow();
    }

    private void FadeTo(AudioStream? stream)
    {
        _fade?.Kill();
        AudioStreamPlayer outgoing = _players[_active];
        _active = 1 - _active;
        AudioStreamPlayer incoming = _players[_active];

        incoming.Stop();
        incoming.Stream = stream;
        incoming.VolumeLinear = 0;
        if (stream is not null)
        {
            incoming.Play();
        }

        _fade = CreateTween().SetParallel();
        _fade.TweenProperty(outgoing, "volume_linear", 0f, FadeSeconds);
        if (stream is not null)
        {
            _fade.TweenProperty(incoming, "volume_linear", Mathf.DbToLinear(VolumeDb), FadeSeconds);
        }

        _fade.Chain().TweenCallback(Callable.From(outgoing.Stop));
    }

    private void StopNow()
    {
        _fade?.Kill();
        _fade = null;
        foreach (AudioStreamPlayer? player in _players)
        {
            if (player is not null)
            {
                player.Stop();
                player.Stream = null;
            }
        }
    }

    private AudioStream? Stream(string name, bool loop)
    {
        if (!_streams.TryGetValue(name, out AudioStream? stream))
        {
            string? path = Extensions.Select(extension => $"res://assets/audio/music/{name}.{extension}").FirstOrDefault(candidate => ResourceLoader.Exists(candidate));
            stream = path is null ? null : GD.Load<AudioStream>(path);
            _streams[name] = stream;
        }

        switch (stream)
        {
            case AudioStreamWav wav:
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                wav.LoopBegin = 0;
                wav.LoopEnd = loop ? (int)Math.Round(wav.GetLength() * wav.MixRate) : 0;
                break;
            case AudioStreamOggVorbis ogg:
                ogg.Loop = loop;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = loop;
                break;
        }

        return stream;
    }
}
