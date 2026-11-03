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

    /// <summary>The rolls each round a Bad Luck Curse's Cursed dice go on: a hero's first three.</summary>
    public const int CursedRolls = 3;

    /// <summary>How far a Hexer's unused Luck scatters, as Lucky Clovers, when it dies.</summary>
    public const int CloverScatter = 13;

    /// <summary>The most Clover Luck points a hero can hold (see <c>Hero.CloverLuck</c>).</summary>
    public const int MaxCloverLuck = 7;

    /// <summary>Seconds to decide whether to try your Luck on a roll about to fail.</summary>
    public const double LuckSeconds = 5;

    /// <summary>Seconds to decide whether to try your Luck on any other roll (a Wend roll, a search, one asked on with the clover glowing).</summary>
    public const double WendLuckSeconds = 3;

    /// <summary>How far round the tile it's aimed at a Burst spell (a Blast) reaches: 1, a 3x3 area.</summary>
    public const int BurstRadius = 1;

    /// <summary>The most Od Hand dice a magic attack can add to its roll (a spell, or Hold Fire hurled).</summary>
    public const int MagicOdHandDice = 1;

    /// <summary>Percent of holes (pits, trap doors) with a room two floors straight down that go down that far.</summary>
    public const int DeepHoleChance = 25;

    /// <summary>What a torch in hand needs, a die and the Focus Bonus, to stay lit in a fall: this much for each floor fallen.</summary>
    public const int TorchFallSave = 7;

    /// <summary>Dice a sprung trap attacks with.</summary>
    public const int TrapAttackDice = 1;

    /// <summary>What a sprung trap adds to its attack: the floor, and its own difficulty (3 + floor for most).</summary>
    public static int TrapAttack(int floor, int difficulty) => floor + difficulty;

    /// <summary>Defense against a trap that had been found before it went off.</summary>
    public const int FoundTrapDefense = 1;

    /// <summary>Search total to find a Rock or Stick hidden on a tile.</summary>
    public static int FindDifficulty(int floor) => 2 + floor;

    /// <summary>A first Search in a room reaching this picks up everything lying about (a Rock and a Stick), not just one.</summary>
    public const int AllFindsAt = 4;

    /// <summary>XP for the first Search in a room: the Explorer bonus.</summary>
    public const int ExplorerXp = 1;

    /// <summary>XP for the Explorer Award: the most Explorer bonuses on a dungeon run that's won.</summary>
    public const int ExplorerAwardXp = 5;

    /// <summary>Search total to find a hidden stash of keys or loot.</summary>
    public static int StashDifficulty(int floor) => 5 + floor;

    /// <summary>Rounds a patch of burning floor (from a Flame Scroll) keeps burning.</summary>
    public const int FlameRounds = 3;

    /// <summary>Rumble it takes to dig off one token of earth when buried.</summary>
    public const int DigPerToken = 3;

    /// <summary>Aid cures an ailment at this, plus one for each of its stacks.</summary>
    public const int AidDifficulty = 10;

    /// <summary>Curing Nekrot with Aid (and a Medicine Bag) is harder.</summary>
    public const int NekrotAidDifficulty = 14;

    /// <summary>With Thieves' Oil, a disease is resisted on this or more.</summary>
    public const int OiledResist = 3;

    /// <summary>With Thieves' Oil, Necro attacks hit this much less.</summary>
    public const int OilNecroDefense = 2;

    /// <summary>The most stacks of ailments a Sineater Doll can hold.</summary>
    public const int DollCapacity = 10;

    /// <summary>A die showing this earns a Power Die.</summary>
    public const int PowerDieFace = 6;

    /// <summary>Power Dice a hero or monster can bank (a hero one more per rank of Magic). A 6 rolled with a full bank is added to its roll instead.</summary>
    public const int MaxBankedPowerDice = 3;

    /// <summary>How far a light reaches in a dark room (diagonals count as 1).</summary>
    public const int LightRadius = 2;

    /// <summary>What a roll to learn a spell from a scroll needs, before the spell's level.</summary>
    public const int LearnBase = 6;

    /// <summary>The highest rank of Magic: one for each basic spell.</summary>
    public const int MaxMagic = 13;

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

    /// <summary>The most cards any belt holds (the Best Belt's).</summary>
    public const int MaxBeltSlots = 10;

    /// <summary>Carrying slots: two cards (or stacks) in your arms, or one that fills both. While carrying, no Rumble and no Od Hand.</summary>
    public const int CarrySlots = 2;

    public const int BackpackCapacity = 8;

    /// <summary>Cards every new character starts with.</summary>
    public static IReadOnlyList<string> StarterKit { get; } = ["basic_belt", "basic_buckle", "club", "torch", "health_potion", "lockpicks"];

    /// <summary>Stats every new character starts with, before spending creation points.</summary>
    public static Stats HeroBaseStats { get; } = new()
    {
        Hearts = 3,
        MoveDice = 1,
        Speed = 2,
        AttackDice = 1,
        DefenseDice = 1,
        OdHandDice = 0,
        Luck = 0,
    };
}
