using Godot;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>How the typed dice and the tokens they leave look and read: their colors, names and what they do.</summary>
public static class DieTypes
{
    public static string Name(DieType type) => type.ToString();

    /// <summary>A typed die's color in the dice bar.</summary>
    public static Color Tint(DieType type) => type switch
    {
        DieType.Fire => Palette.Fire,
        DieType.Poison => new Color("7fd04a"),
        DieType.Ice => new Color("a8ecff"),
        DieType.Acid => new Color("d4ec3a"),
        DieType.Necro => new Color("8f78b0"),
        DieType.Squall => new Color("6fa8dc"),
        DieType.Radiate => new Color("fff2b0"),
        DieType.Life => new Color("3fbf62"),
        DieType.Earth => new Color("b08850"),
        DieType.Elektrik => new Color("ffe03a"),
        DieType.Psycho => new Color("e878e8"),
        DieType.Shadow => new Color("5a5878"),
        _ => Palette.DieRumble,
    };

    /// <summary>What a 6 on one does on a hit, for tooltips.</summary>
    public static string SixOnHit(DieType type) => type switch
    {
        DieType.Fire => "a 6 sets you alight",
        DieType.Poison => "a 6 poisons you",
        DieType.Ice => "a 6 freezes you a stack",
        DieType.Acid => "a 6 eats your armor",
        DieType.Necro => "a 6 blackens a lost heart",
        DieType.Squall => "a 6 blows you back",
        DieType.Radiate => "a 6 makes you shine",
        DieType.Life => "a 6 grows plants under you",
        DieType.Earth => "a 6 knocks you down and buries you",
        DieType.Elektrik => "a 6 stuns you",
        DieType.Psycho => "a 6 confuses you",
        DieType.Shadow => "a 6 blinds you",
        _ => "a 6 surges with Power",
    };

    public static Color Tint(Condition condition) => condition switch
    {
        Condition.Poison => Tint(DieType.Poison),
        Condition.Freeze => Tint(DieType.Ice),
        Condition.Corroded or Condition.Acid => Tint(DieType.Acid),
        Condition.BlackHeart => Tint(DieType.Necro),
        Condition.Shine => Tint(DieType.Radiate),
        Condition.Buried => Tint(DieType.Earth),
        Condition.Confused => Tint(DieType.Psycho),
        Condition.Blind => Tint(DieType.Shadow),
        _ => Palette.Text,
    };

    public static string Label(Condition condition) => condition switch
    {
        Condition.BlackHeart => "Black Heart",
        Condition.Corroded => "Armor eaten",
        _ => condition.ToString(),
    };

    /// <summary>Called out over whoever just got one.</summary>
    public static string Callout(Condition condition) => condition switch
    {
        Condition.Poison => "POISONED!",
        Condition.Freeze => "FREEZE!",
        Condition.Corroded => "ARMOR MELTS!",
        Condition.Acid => "ACID!",
        Condition.BlackHeart => "BLACK HEART!",
        Condition.Shine => "SHINE!",
        Condition.Buried => "BURIED!",
        Condition.Confused => "CONFUSED!",
        Condition.Blind => "BLINDED!",
        _ => "!",
    };

    /// <summary>A line about one of someone's tokens, for tooltips.</summary>
    public static string Describe(Condition condition, int count) => condition switch
    {
        Condition.Poison => $"poisoned ({count}: -{count} to all rolls)",
        Condition.Freeze => count == 1 ? "chilled (half speed)" : $"freezing ({count} stacks)",
        Condition.Corroded => $"armor eaten (-{count})",
        Condition.Acid => $"acid burning ({count})",
        Condition.BlackHeart => count == 1 ? "a Black Heart" : $"{count} Black Hearts",
        Condition.Shine => $"shining (-{count} defense)",
        Condition.Buried => $"buried ({count}: 3 Rumble each to dig out)",
        Condition.Confused => $"confused ({count})",
        Condition.Blind => $"blind ({count}: -{count} to attacks)",
        _ => condition.ToString(),
    };
}
