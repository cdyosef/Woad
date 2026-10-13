using Woad.Core.Content;

namespace Woad.Core.Rules;

/// <summary>Tunable rule numbers. See docs/MVP-Design.md.</summary>
public static class GameRules
{
    public const int MaxHeroes = 4;

    /// <summary>Hearts an attack takes when its total beats the defense total (ties go to the defender).</summary>
    public const int DamagePerHit = 1;

    /// <summary>A die showing this earns a Power Die.</summary>
    public const int PowerDieFace = 6;

    /// <summary>Power Dice a hero or monster can bank. A 6 rolled with a full bank is added to its roll instead.</summary>
    public const int MaxBankedPowerDice = 3;

    /// <summary>Hearts restored to each hero in a room when its last monster falls.</summary>
    public const int HeartsOnRoomClear = 1;

    /// <summary>Hearts a heart found in a pot restores.</summary>
    public const int HeartsFromPot = 1;

    /// <summary>XP for reaching a floor for the first time (on top of each monster's own XP).</summary>
    public const int XpPerNewFloor = 5;

    public const int XpForEscaping = 10;

    public const int BeltCapacity = 2;

    public const int BackpackCapacity = 6;

    /// <summary>Cards every new character starts with.</summary>
    public static IReadOnlyList<string> StarterKit { get; } = ["wooden_sword", "buckler", "health_potion"];

    /// <summary>Stats every new character starts with, before spending creation points.</summary>
    public static Stats HeroBaseStats { get; } = new()
    {
        Hearts = 4,
        MoveDice = 1,
        Speed = 2,
        AttackDice = 1,
        DefenseDice = 1,
        Luck = 0,
    };
}
