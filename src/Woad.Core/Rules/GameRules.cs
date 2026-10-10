using Woad.Core.Content;

namespace Woad.Core.Rules;

/// <summary>Tunable rule numbers. See docs/MVP-Design.md.</summary>
public static class GameRules
{
    public const int MaxHeroes = 4;

    /// <summary>An attack die showing this or higher is a hit.</summary>
    public const int HitFace = 5;

    /// <summary>A hero's defense die showing this or higher blocks one hit.</summary>
    public const int HeroBlockFace = 5;

    /// <summary>A monster's defense die showing this or higher blocks one hit.</summary>
    public const int MonsterBlockFace = 6;

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
