using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// Placeholder colors until the art pass (M6): a green-stone dungeon framed by gothic,
/// gold-trimmed panels.
/// </summary>
public static class Palette
{
    public static readonly Color Background = new("0b0a14");
    public static readonly Color Panel = new("1a1626");
    public static readonly Color PanelDark = new("120f1c");
    public static readonly Color PanelHover = new("2a2338");
    public static readonly Color PanelPressed = new("3a2f4d");
    public static readonly Color Trim = new("b08d4a");
    public static readonly Color TrimBright = new("e2c47a");
    public static readonly Color TrimDim = new("4a4030");
    public static readonly Color Text = new("e8e0c8");
    public static readonly Color TextDim = new("8f8778");
    public static readonly Color Danger = new("e0605a");
    public static readonly Color Heal = new("6fcf7a");
    public static readonly Color Heart = new("d8434a");
    public static readonly Color HeartEmpty = new("3a2a30");
    public static readonly Color Ink = new("15121d");

    /// <summary>Power Dice, earned by rolling a 6.</summary>
    public static readonly Color Power = new("b58cf0");

    public static readonly Color FloorLight = new("3f6b58");
    public static readonly Color FloorDark = new("375f4e");
    public static readonly Color Grout = new("2c4d40");
    public static readonly Color Wall = new("1f2b27");
    public static readonly Color Rock = new("141a18");
    public static readonly Color WallTop = new("4d6158");
    public static readonly Color Pit = new("05070a");
    public static readonly Color ShallowWater = new("4a8ab8", 0.7f);
    public static readonly Color DeepWater = new("1d3a66");
    public static readonly Color Chest = new("8a5a2b");
    public static readonly Color ChestOpen = new("4a3018");
    public static readonly Color Loot = new("f0c94a");
    public static readonly Color Pot = new("a8643c");
    public static readonly Color Fire = new("ff6a1f");
    public static readonly Color FireCore = new("ffd84a");
    public static readonly Color Stone = new("9aa0a6");
    public static readonly Color StoneShade = new("6e747c", 0.6f);
    public static readonly Color Grave = new("3b2c22", 0.85f);
    public static readonly Color PotRim = new("7a4528");

    public static readonly Color Reachable = new("6fb7ff", 0.30f);

    /// <summary>Reachable tiles that hurt: pits and found traps.</summary>
    public static readonly Color Hazard = new("ff4a3d", 0.38f);

    /// <summary>Where a pit jump would land.</summary>
    public static readonly Color Jump = new("b58cf0", 0.42f);

    /// <summary>Tints the floor seen far down a pit that leads to the room below.</summary>
    public static readonly Color FloorBelow = new(0.30f, 0.38f, 0.36f);
    public static readonly Color Target = new("ffa94d", 0.40f);
    public static readonly Color Blast = new("ff7a3d", 0.45f);
    public static readonly Color Interact = new("f0c94a", 0.9f);

    private static readonly Color[] Seats =
    [
        new("e05d5d"), new("5d8de0"), new("6fcf7a"), new("e0c95d"),
        new("e0955d"), new("b07de0"), new("5dd0d0"), new("e07db8"),
    ];

    public static Color Seat(int seat) => Seats[seat % Seats.Length];

    /// <summary>Stand-in token colors per monster until there are sprites.</summary>
    public static Color Monster(string monsterId) => monsterId switch
    {
        "slime" or "slimeling" => new Color("7bd389"),
        "bat" => new Color("a77fe0"),
        "spear_guard" => new Color("8fa3b8"),
        "archer_guard" => new Color("c8a46a"),
        "skeleton" => new Color("e8e2d0"),
        "iron_warden" => new Color("a83a46"),
        "cinder_imp" => new Color("ff7a2a"),
        "fire_beetle" => new Color("b8442a"),
        _ => new Color("b0b0b0"),
    };

    public static string Hex(Color color) => "#" + color.ToHtml(includeAlpha: false);
}
