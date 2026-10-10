using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// The music and sound-effect volumes: one audio bus each, remembered in a small settings file
/// (user://settings.cfg). Volumes run from 0 (off) to 1 (full).
/// </summary>
public static class AudioSettings
{
    public const string MusicBus = "Music";
    public const string SfxBus = "Sfx";

    private const string Section = "audio";
    private const float DefaultVolume = 0.8f;

    private static string _path = "user://settings.cfg";

    public static float MusicVolume { get; private set; } = DefaultVolume;

    public static float SfxVolume { get; private set; } = DefaultVolume;

    /// <summary>Creates the buses and applies the saved volumes. Call once, before anything plays.</summary>
    /// <param name="path">Where settings are read and saved; the debug tests pass a throwaway file.</param>
    public static void Load(string path)
    {
        _path = path;
        var config = new ConfigFile();
        if (config.Load(path) == Error.Ok)
        {
            MusicVolume = Mathf.Clamp(config.GetValue(Section, "music", DefaultVolume).AsSingle(), 0, 1);
            SfxVolume = Mathf.Clamp(config.GetValue(Section, "sfx", DefaultVolume).AsSingle(), 0, 1);
        }

        Apply(MusicBus, MusicVolume);
        Apply(SfxBus, SfxVolume);
    }

    public static void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp(volume, 0, 1);
        Apply(MusicBus, MusicVolume);
    }

    public static void SetSfxVolume(float volume)
    {
        SfxVolume = Mathf.Clamp(volume, 0, 1);
        Apply(SfxBus, SfxVolume);
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.SetValue(Section, "music", MusicVolume);
        config.SetValue(Section, "sfx", SfxVolume);
        Error error = config.Save(_path);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Couldn't save settings to {_path}: {error}");
        }
    }

    /// <summary>The bus's current loudness in decibels, or null when it's muted.</summary>
    public static float? BusVolumeDb(string bus)
    {
        int index = AudioServer.GetBusIndex(bus);
        return index < 0 || AudioServer.IsBusMute(index) ? null : AudioServer.GetBusVolumeDb(index);
    }

    private static void Apply(string bus, float volume)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, bus);
            AudioServer.SetBusSend(index, "Master");
        }

        // Squared, so the slider's middle sounds about half as loud rather than barely quieter.
        AudioServer.SetBusMute(index, volume <= 0);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(volume * volume));
    }
}
