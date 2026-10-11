using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>Music and sound-effect volumes. Changes are heard as the sliders move and saved on leaving.</summary>
public partial class SettingsScreen : MenuScreen
{
    private const ulong PreviewGapMsec = 120;

    private ulong _lastPreview;

    public event Action? BackRequested;

    internal HSlider MusicSlider { get; private set; } = null!;

    internal HSlider SoundSlider { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

    public override void _Ready()
    {
        BuildFrame("SETTINGS");

        Caption(new Vector2(170, 100), "MUSIC");
        MusicSlider = VolumeSlider(116, AudioSettings.MusicVolume, AudioSettings.SetMusicVolume);

        Caption(new Vector2(170, 154), "SOUND EFFECTS");
        SoundSlider = VolumeSlider(170, AudioSettings.SfxVolume, volume =>
        {
            AudioSettings.SetSfxVolume(volume);
            PreviewSound();
        });

        BackButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();
    }

    public override void _ExitTree() => AudioSettings.Save();

    private HSlider VolumeSlider(float top, float volume, Action<float> changed)
    {
        Label percent = Add(UiTheme.Label(new Vector2(430, top + 1), new Vector2(40, 14), Percent(volume), 10));
        HSlider slider = Add(new HSlider
        {
            Position = new Vector2(170, top),
            Size = new Vector2(250, 16),
            MinValue = 0,
            MaxValue = 1,
            Step = 0.05,
            Value = volume,
            FocusMode = FocusModeEnum.None,
        });
        slider.ValueChanged += value =>
        {
            changed((float)value);
            percent.Text = Percent((float)value);
        };
        return slider;
    }

    /// <summary>A click at the new volume, at most every few frames while dragging.</summary>
    private void PreviewSound()
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastPreview >= PreviewGapMsec)
        {
            _lastPreview = now;
            Sfx.Play("click");
        }
    }

    private static string Percent(float volume) => volume <= 0 ? "Off" : $"{Mathf.RoundToInt(volume * 100)}%";
}
