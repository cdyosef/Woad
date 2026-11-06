using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// What one hero takes home from a finished run. Escaped heroes keep everything they carried out
/// and turn their treasure into gold; heroes who died or were left behind keep only the gear they brought in. Everyone
/// keeps the XP they earned.
/// </summary>
public sealed record HeroRunResult
{
    public required int HeroId { get; init; }

    public required Guid? CharacterId { get; init; }

    public required string Name { get; init; }

    public required int Seat { get; init; }

    public required string Portrait { get; init; }

    public Appearance Appearance { get; init; } = Appearance.Default;

    public required HeroStatus Status { get; init; }

    public required bool Won { get; init; }

    public required int Kills { get; init; }

    /// <summary>XP earned, the Explorer Award's and the other awards' included.</summary>
    public required int Xp { get; init; }

    /// <summary>Rooms they searched first.</summary>
    public int ExplorerBonus { get; init; }

    /// <summary>The most Explorer bonuses on a dungeon run that's won (shared on a tie): <see cref="GameRules.ExplorerAwardXp"/> more XP.</summary>
    public bool ExplorerAward { get; init; }

    /// <summary>The end-of-run awards won (Keymaster, Slayer...), each with its XP (counted in <see cref="Xp"/>).</summary>
    public IReadOnlyList<RunAward> Awards { get; init; } = [];

    /// <summary>Treasure turned into gold; 0 for heroes who didn't escape.</summary>
    public required int Coins { get; init; }

    public required int DeepestFloor { get; init; }

    /// <summary>The hearts the hero ended the run with (0 if they fell).</summary>
    public int Hearts { get; init; }

    /// <summary>The points bought by the end of the run (counting any spent in it); null for a debug hero.</summary>
    public StatAllocation? Allocation { get; init; }

    /// <summary>The spells they know by the end of the run (counting any learned in it); null for a debug hero.</summary>
    public IReadOnlyList<string>? Spells { get; init; }

    /// <summary>The companion policy it ended the run with.</summary>
    public CompanionPolicy? Policy { get; init; }

    /// <summary>Clover Luck taken home: all of it out of the dungeon, otherwise no more than they brought in.</summary>
    public int CloverLuck { get; init; }

    /// <summary>The rolls the Luck menu asks on, as it was left.</summary>
    public LuckRoll? LuckAsks { get; init; }

    /// <summary>They retreated from a fight on the Woarld Map (off the field, but it isn't won).</summary>
    public bool Retreated { get; init; }

    /// <summary>Power Dice the hero still had banked, carried into their next run.</summary>
    public int PowerDice { get; init; }

    /// <summary>For a hero who died, what killed them ("Slain by a Goblin"); otherwise null.</summary>
    public string? Epitaph { get; init; }

    /// <summary>Card ids of the gear the character goes home with (treasure is already counted as gold).</summary>
    public required IReadOnlyList<string> Equipped { get; init; }

    public required IReadOnlyList<string> Belt { get; init; }

    public required IReadOnlyList<string> Backpack { get; init; }

    /// <summary>What they carry home in their arms.</summary>
    public IReadOnlyList<string> Carried { get; init; } = [];

    /// <summary>What the run did to the character's name with the Merchant Guild.</summary>
    public int GuildStandingChange { get; init; }

    /// <summary>What the run did to the character's standing with the Law.</summary>
    public int LawStandingChange { get; init; }

    /// <summary>The run broke the cult's ritual (the same for everyone in it).</summary>
    public bool RitualStopped { get; init; }

    /// <summary>The run let the cult's ritual open the portal (the same for everyone in it).</summary>
    public bool PortalOpened { get; init; }

    /// <summary>What the hero carries on to the next run until it's cured: Black Hearts and disease.</summary>
    public IReadOnlyDictionary<Condition, int> Lingering { get; init; } = new Dictionary<Condition, int>();

    /// <summary>Merchants the party rescued this run (by name), and whether this hero freed each.</summary>
    public IReadOnlyList<(string Name, bool FreedByThisHero)> MerchantsRescued { get; init; } = [];
}

/// <summary>An end-of-run award: its name and the XP it's worth.</summary>
public sealed record RunAward(string Name, int Xp);

public static class RunResults
{
    /// <summary>
    /// The end-of-run awards, on a completed run (a dungeon whose boss is beaten, someone getting out): for the
    /// most keys used, doors unlocked, traps disarmed, traps set off, kills and damage. Anyone in the party can
    /// win one, the dead too, and a tie shares it. Each is worth <see cref="GameRules.AwardXpPerFloor"/> for every
    /// floor of the dungeon; "Check for what?!" (the most traps set off) half that, rounded up.
    /// </summary>
    public static IReadOnlyList<(string Name, Func<Hero, int> Tally, bool Half)> Awards { get; } =
    [
        ("Keymaster", hero => hero.KeysUsed, false),
        ("Lockbreaker", hero => hero.DoorsUnlocked, false),
        ("Trapsmith", hero => hero.TrapsDisarmed, false),
        ("Check for what?!", hero => hero.TrapsSprung, true),
        ("Slayer", hero => hero.Kills, false),
        ("Heavy Hitter", hero => hero.DamageDealt, false),
    ];

    /// <summary>Whether a finished run counts as completed: a dungeon (not a fight on the Woarld Map) whose boss is beaten, and someone got out.</summary>
    public static bool Completed(GameState state) =>
        !state.Skirmish
        && state.Heroes.Any(hero => !hero.Summoned && hero.Status == HeroStatus.Escaped && !state.RetreatedIds.Contains(hero.Id))
        && !state.Rooms.Values.Any(room => room.Monsters.Any(monster => monster.Definition.Boss));

    /// <summary>The awards each hero in <paramref name="party"/> wins on this run (none if it isn't completed).</summary>
    private static Dictionary<Hero, List<RunAward>> AwardsFor(GameState state, IReadOnlyList<Hero> party)
    {
        var won = party.ToDictionary(hero => hero, _ => new List<RunAward>());
        if (!Completed(state) || party.Count == 0)
        {
            return won;
        }

        int floors = Math.Max(1, state.Rooms.Values.Where(room => room.Level == 0).Max(room => room.Floor));
        foreach ((string name, Func<Hero, int> tally, bool half) in Awards)
        {
            int most = party.Max(tally);
            if (most < 1)
            {
                continue;
            }

            int xp = GameRules.AwardXpPerFloor * floors;
            foreach (Hero hero in party.Where(hero => tally(hero) == most))
            {
                won[hero].Add(new RunAward(name, half ? (xp + 1) / 2 : xp));
            }
        }

        return won;
    }

    /// <exception cref="InvalidOperationException">The run hasn't ended yet.</exception>
    public static IReadOnlyList<HeroRunResult> For(GameState state)
    {
        if (state.Phase != RunPhase.Ended)
        {
            throw new InvalidOperationException("The run hasn't ended yet.");
        }

        static List<string> Gear(IEnumerable<CardInstance> cards) =>
            cards.Where(card => card.Definition.Type != CardType.Treasure && !card.Definition.GateKey).SelectMany(card => Enumerable.Repeat(card.Key, card.Count)).ToList();

        // The Explorer Award: a dungeon run someone got out of, to whoever searched the most rooms first.
        List<Hero> party = [.. state.Heroes.Where(hero => !hero.Summoned)];
        Dictionary<Hero, List<RunAward>> awards = AwardsFor(state, party);
        int mostExplored = !state.Skirmish && party.Any(hero => hero.Status == HeroStatus.Escaped && !state.RetreatedIds.Contains(hero.Id))
            ? party.Max(hero => hero.ExplorerBonus)
            : 0;
        bool Explorer(Hero hero) => mostExplored > 0 && hero.ExplorerBonus == mostExplored;

        return party.Select(hero => new HeroRunResult
        {
            HeroId = hero.Id,
            CharacterId = hero.CharacterId,
            Name = hero.Name,
            Seat = hero.Seat,
            Portrait = hero.Portrait,
            Appearance = hero.Appearance,
            Status = hero.Status,
            Won = state.WinnerIds.Contains(hero.Id),
            Kills = hero.Kills,
            Xp = hero.Xp + (Explorer(hero) ? GameRules.ExplorerAwardXp : 0) + awards[hero].Sum(award => award.Xp),
            ExplorerBonus = hero.ExplorerBonus,
            ExplorerAward = Explorer(hero),
            Awards = awards[hero],
            Coins = hero.Status == HeroStatus.Escaped ? hero.TreasureValue : 0,
            DeepestFloor = hero.DeepestFloor,
            Hearts = hero.Hearts,
            PowerDice = hero.Power.Banked,
            Retreated = state.RetreatedIds.Contains(hero.Id),
            Allocation = hero.Allocation,
            Spells = hero.Allocation is null ? null : [.. hero.Spells.Select(spell => spell.Id)],
            Policy = hero.Policy,
            LuckAsks = hero.LuckAsks,
            CloverLuck = hero.Status == HeroStatus.Escaped ? hero.CloverLuck : Math.Min(hero.CloverLuck, hero.StartCloverLuck),
            Epitaph = hero.Status == HeroStatus.Dead ? hero.Bane : null,
            GuildStandingChange = hero.GuildStandingChange,
            LawStandingChange = hero.LawStandingChange,
            MerchantsRescued = state.RescuedMerchants.Select(rescued => (rescued.Name, rescued.FreedByHeroId == hero.Id)).ToList(),
            RitualStopped = state.RitualStopped,
            PortalOpened = state.PortalOpen,
            Lingering = Conditions.Lingering.Where(hero.Conditions.Has).ToDictionary(condition => condition, condition => hero.Conditions[condition]),
            Equipped = Gear(hero.EquippedCards.Concat(hero.Mods)),
            Belt = Gear(hero.Belt),
            Backpack = Gear(hero.Backpack),
            Carried = Gear(hero.Carried),
        }).ToList();
    }
}
