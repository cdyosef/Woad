using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>
/// What kind of raid to start: a quick Dungeon Run, or (later) a Raiding Party from The Waylay
/// Inn, a Frontier Raid on a random Woarld, a Ballad campaign, the Map Editor or the Woarld Tree.
/// </summary>
public partial class ModeSelectScreen : MenuScreen
{
    private const string ComingSoon = "Coming soon.";

    public event Action? BackRequested;

    public event Action? DungeonRunRequested;

    public event Action? RaidingPartyRequested;

    internal Button DungeonRunButton { get; private set; } = null!;

    internal Button RaidingPartyButton { get; private set; } = null!;

    internal Button FrontierRaidButton { get; private set; } = null!;

    internal Button BalladButton { get; private set; } = null!;

    internal Button MapEditorButton { get; private set; } = null!;

    internal Button WoarldTreeButton { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

    public override void _Ready()
    {
        BuildFrame("SELECT MODE");

        DungeonRunButton = Mode(0, "Dungeon Run", "Pick a dungeon and dive straight in with your party.", ready: true);
        DungeonRunButton.Pressed += () => DungeonRunRequested?.Invoke();
        RaidingPartyButton = Mode(1, "Raiding Party", "Set out from The Waylay Inn into the Woarld shared by everyone on this PC.", ready: true);
        RaidingPartyButton.Pressed += () => RaidingPartyRequested?.Invoke();
        FrontierRaidButton = Mode(2, "Frontier Raid", "Raid a Woarld made fresh at random. Saved as an Expedition.");
        BalladButton = Mode(3, "Ballad of the Woad Raiders", "The long campaign, in a Woarld your party's own. Saved as a Ballad.");
        MapEditorButton = Mode(4, "Map Editor", "Draw your own Woarlds and dungeons.");
        WoarldTreeButton = Mode(5, "Woarld Tree", "Raid together online.");

        BackButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();
    }

    /// <summary>A mode's button and the line describing it. A mode that isn't built yet is greyed out.</summary>
    private Button Mode(int row, string name, string description, bool ready = false)
    {
        float y = 50 + row * 42;
        Button button = Add(UiTheme.Button(new Vector2(40, y), new Vector2(200, 30), name));
        button.Disabled = !ready;
        if (!ready)
        {
            button.TooltipText = $"{name}: {ComingSoon.ToLowerInvariant()}";
        }

        Label line = Add(UiTheme.Label(new Vector2(254, y), new Vector2(350, 30), ready ? description : $"{description} {ComingSoon}", color: ready ? Palette.Text : Palette.TextDim));
        line.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }
}
