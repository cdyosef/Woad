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

    public required int Xp { get; init; }

    /// <summary>Treasure turned into gold; 0 for heroes who didn't escape.</summary>
    public required int Gold { get; init; }

    public required int DeepestFloor { get; init; }

    /// <summary>The hearts the hero ended the run with (0 if they fell).</summary>
    public int Hearts { get; init; }

    /// <summary>The points bought by the end of the run (counting any spent in it); null for a debug hero.</summary>
    public StatAllocation? Allocation { get; init; }

    /// <summary>The spells they know by the end of the run (counting any learned in it); null for a debug hero.</summary>
    public IReadOnlyList<string>? Spells { get; init; }

    /// <summary>The companion policy it ended the run with.</summary>
    public CompanionPolicy? Policy { get; init; }

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

    /// <summary>What the run did to the character's name with the Merchant Guild.</summary>
    public int GuildStandingChange { get; init; }

    /// <summary>What the run did to the character's standing with the Law.</summary>
    public int LawStandingChange { get; init; }

    /// <summary>Merchants the party rescued this run (by name), and whether this hero freed each.</summary>
    public IReadOnlyList<(string Name, bool FreedByThisHero)> MerchantsRescued { get; init; } = [];
}

public static class RunResults
{
    /// <exception cref="InvalidOperationException">The run hasn't ended yet.</exception>
    public static IReadOnlyList<HeroRunResult> For(GameState state)
    {
        if (state.Phase != RunPhase.Ended)
        {
            throw new InvalidOperationException("The run hasn't ended yet.");
        }

        static List<string> Gear(IEnumerable<CardInstance> cards) =>
            cards.Where(card => card.Definition.Type != CardType.Treasure).SelectMany(card => Enumerable.Repeat(card.Key, card.Count)).ToList();

        return state.Heroes.Where(hero => !hero.Summoned).Select(hero => new HeroRunResult
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
            Xp = hero.Xp,
            Gold = hero.Status == HeroStatus.Escaped ? hero.TreasureValue : 0,
            DeepestFloor = hero.DeepestFloor,
            Hearts = hero.Hearts,
            PowerDice = hero.Power.Banked,
            Retreated = state.RetreatedIds.Contains(hero.Id),
            Allocation = hero.Allocation,
            Spells = hero.Allocation is null ? null : [.. hero.Spells.Select(spell => spell.Id)],
            Policy = hero.Policy,
            Epitaph = hero.Status == HeroStatus.Dead ? hero.Bane : null,
            GuildStandingChange = hero.GuildStandingChange,
            LawStandingChange = hero.LawStandingChange,
            MerchantsRescued = state.RescuedMerchants.Select(rescued => (rescued.Name, rescued.FreedByHeroId == hero.Id)).ToList(),
            Equipped = Gear(hero.EquippedCards),
            Belt = Gear(hero.Belt),
            Backpack = Gear(hero.Backpack),
        }).ToList();
    }
}
