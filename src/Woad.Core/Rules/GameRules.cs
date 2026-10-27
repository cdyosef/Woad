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

    /// <summary>A grand chest's lock: harder than any other on its floor.</summary>
    public static int GrandChestLockDifficulty(int floor) => ChestLockDifficulty(floor) + 2;

    /// <summary>Gold in a grand chest, besides its cards.</summary>
    public const int GrandChestGold = 10;

    /// <summary>Search total to find a secret door.</summary>
    public static int SecretDifficulty(int floor) => 4 + floor;

    /// <summary>Search total to spot a trap, and Od Hand total to disarm it.</summary>
    public static int TrapDifficulty(int floor) => 3 + floor;

    /// <summary>Search total to find a hidden stash of keys or loot.</summary>
    public static int StashDifficulty(int floor) => 5 + floor;

    /// <summary>Rounds a patch of burning floor (from a Flame Scroll) keeps burning.</summary>
    public const int FlameRounds = 3;

    /// <summary>Rumble it takes to dig off one token of earth when buried.</summary>
    public const int DigPerToken = 3;

    /// <summary>An Od Hand Tend clears blind eyes at this, plus one for each Blind token.</summary>
    public const int TendDifficulty = 10;

    /// <summary>A die showing this earns a Power Die.</summary>
    public const int PowerDieFace = 6;

    /// <summary>Power Dice a hero or monster can bank. A 6 rolled with a full bank is added to its roll instead.</summary>
    public const int MaxBankedPowerDice = 3;

    /// <summary>The farthest a leap can be aimed (a 6, halved).</summary>
    public const int MaxLeap = 3;

    /// <summary>Hearts lost drowning: ending a turn in deep water with no Rumble die to spare.</summary>
    public const int DrownDamage = 1;

    /// <summary>Hearts lost falling into a pit (or through a sprung trap door, like any trap).</summary>
    public const int FallDamage = 1;

    /// <summary>Marks of life that stand a hero Keeling Over back up, and marks of death that kill them.</summary>
    public const int MarksToRise = 3;

    public const int MarksToDie = 3;

    /// <summary>A Keel Die (or Doom Die) showing this earns a mark of death.</summary>
    public const int DeathFace = 1;

    /// <summary>Hearts a hero stands back up with after Keeling Over.</summary>
    public const int HeartsOnRising = 1;

    /// <summary>Every this much of an ally's Od Hand total rousing a hero Keeling Over earns them a mark of life.</summary>
    public const int RousePerMark = 6;

    /// <summary>Prone: steps to lie down, steps to get up, and how much each step costs while down.</summary>
    public const int ProneCost = 1;

    public const int GetUpCost = 2;

    public const int CrawlFactor = 2;

    /// <summary>Bombs: dice in a blast, the Od Hand to defuse a lit one, and the chance a Rumble die (or a point of weapon bonus) sets one off.</summary>
    public const int BlastDice = 4;

    public const int DefuseDifficulty = 10;

    public const int BombChancePerDie = 12;

    public const int BombChancePerBonus = 2;

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
    public static IReadOnlyList<string> StarterKit { get; } = ["wooden_sword", "buckler", "health_potion", "torch"];

    /// <summary>Stats every new character starts with, before spending creation points.</summary>
    public static Stats HeroBaseStats { get; } = new()
    {
        Hearts = 3,
        MoveDice = 1,
        Speed = 2,
        AttackDice = 1,
        DefenseDice = 1,
        OdHandDice = 1,
        Luck = 0,
    };
}
