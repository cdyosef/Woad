using Woad.Core.Content;

namespace Woad.Core.Rules;

/// <summary>Tunable rule numbers. See docs/MVP-Design.md.</summary>
public static class GameRules
{
    public const int MaxHeroes = 8;

    /// <summary>Rooms are built for up to this many heroes; a bigger party faces a tougher boss.</summary>
    public const int FullParty = 4;

    /// <summary>
    /// How many heroes a party counts as when sizing its monsters: each player counts 1, each
    /// companion half (rounded up), as companions don't loot or unlock and can't carry on alone.
    /// </summary>
    public static int PartyStrength(int players, int companions) => players + (companions + 1) / 2;

    /// <inheritdoc cref="PartyStrength(int, int)"/>
    public static int PartyStrength(IReadOnlyList<HeroSetup> heroes) =>
        PartyStrength(heroes.Count(hero => hero.OwnerSeat is null), heroes.Count(hero => hero.OwnerSeat is not null));

    /// <summary>Extra hearts a boss gets in a party bigger than <see cref="FullParty"/>: 3 for each hero beyond it.</summary>
    public static int ExtraMonsterHearts(int partySize, bool boss) => boss ? 3 * Math.Max(0, partySize - FullParty) : 0;

    /// <summary>Hearts an attack takes when its total beats the defense total (ties go to the defender).</summary>
    public const int DamagePerHit = 1;

    /// <summary>Od Hand total to pick a locked door. Kept low enough that one die plus a Power Die can always do it.</summary>
    public static int DoorLockDifficulty(int floor) => 4 + Math.Min(floor, 2);

    /// <summary>Od Hand total to pick a locked chest (which holds better loot).</summary>
    public static int ChestLockDifficulty(int floor) => 5 + floor;

    /// <summary>Search total to find a secret door.</summary>
    public static int SecretDifficulty(int floor) => 4 + floor;

    /// <summary>Search total to spot a trap, and Od Hand total to disarm it.</summary>
    public static int TrapDifficulty(int floor) => 3 + floor;

    /// <summary>Search total to find a hidden stash of keys or loot.</summary>
    public static int StashDifficulty(int floor) => 5 + floor;

    /// <summary>A die showing this earns a Power Die.</summary>
    public const int PowerDieFace = 6;

    /// <summary>Power Dice a hero or monster can bank. A 6 rolled with a full bank is added to its roll instead.</summary>
    public const int MaxBankedPowerDice = 3;

    /// <summary>A pit jump needs this on one die.</summary>
    public const int JumpDifficulty = 4;

    /// <summary>Hearts lost falling into a pit (or through a sprung trap door, like any trap).</summary>
    public const int FallDamage = 1;

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
        OdHandDice = 1,
        Luck = 0,
    };
}
