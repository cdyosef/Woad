using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// A hired companion who went unpaid, whom nobody took on, and who has no bond with anyone in the party, starts the
/// next run leaving. How they go about it is their own affair, settled on their first turn (see <see cref="LeavingPlan"/>):
/// some hang about the entrance a few turns asking for any last offers; some see the first room through, fighting and
/// picking over it, and go when the party moves on; some come a few rooms along and go when the party heads deeper or
/// finds the boss. Any of them goes at once when it gets too hot (half their hearts gone) or when they've picked up
/// treasure worth what they were owed. On each of their turns they may have a change of heart (a d6 and their best
/// bond in the party, 6 or more: the way back looks long, and perhaps worse than the way on). Any player in the room
/// can pay them what they're owed there and then (less by their bond, a gold a point), or try to talk them round, once
/// a turn: a d6 and their bond with them, 6 or more. Paid or persuaded, they stay, and follow whoever did it.
/// Otherwise, when their time comes, they spend their turn walking out, back to the inn.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The turns a leaving companion waits about for a last offer before going.</summary>
    public const int LeavingWait = 3;

    /// <summary>What a convince roll, or a change of heart, has to come to.</summary>
    public const int ConvinceDifficulty = 6;

    /// <summary>The rooms a companion leaving after "a few rooms" comes along for.</summary>
    public const int LeavingRooms = 3;

    private static readonly string[] LeavingLines =
    [
        "No coin, no company. Last chance, anyone?",
        "I'm owed, and I'm off. Unless somebody's paying?",
        "Long road back to the inn... any offers before I walk it?",
        "Going once... going twice...",
    ];

    /// <summary>The bond between two heroes in this run (0 with none).</summary>
    public int BondBetween(Hero one, Hero other) => one.Bonds.GetValueOrDefault(other.Id);

    /// <summary>What a leaving companion will take from <paramref name="payer"/> to stay, in copper: what they're owed, less a gold for each point of bond between them (never less than 1 gold).</summary>
    public int StayPrice(Hero companion, Hero payer) =>
        Math.Max(Money.Gold(1), companion.Wage - Money.Gold(BondBetween(companion, payer)));

    /// <summary>The companions in the active hero's room who are leaving, to be paid or talked round.</summary>
    public IReadOnlyList<Hero> GetLeavingCompanions() =>
        State.Turn is null ? [] : [.. State.ActiveHeroesIn(ActiveRoom).Where(other => other.Leaving && other != ActiveHero)];

    private string? CheckLeaver(int companionId, out Hero? companion)
    {
        companion = State.Heroes.FirstOrDefault(hero => hero.Id == companionId);
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish what you're doing first.";
        }

        if (ActiveHero.IsCompanion)
        {
            return "That's for a player to do.";
        }

        if (companion is not { Leaving: true, IsActive: true })
        {
            return "They aren't going anywhere.";
        }

        return State.RoomOf(companion) != ActiveRoom ? $"{companion.Name} is too far off to hear you." : null;
    }

    /// <summary>Why the active hero can't pay the leaving companion <paramref name="copper"/> to stay, or null.</summary>
    public string? CheckOfferWage(int companionId, int copper)
    {
        if (CheckLeaver(companionId, out Hero? companion) is { } error)
        {
            return error;
        }

        if (copper > ActiveHero.Coins)
        {
            return $"You only have {Money.Long(ActiveHero.Coins)}.";
        }

        int price = StayPrice(companion!, ActiveHero);
        return copper < price ? $"{companion!.Name} wants {Money.Long(price)} to stay." : null;
    }

    /// <summary>Why the active hero can't try to talk the leaving companion round now, or null.</summary>
    public string? CheckConvince(int companionId) =>
        CheckLeaver(companionId, out _) ?? (Turn.Convinced ? "You've said your piece this turn: try again next turn." : null);

    private string? Handle(OfferWage command, List<GameEvent> events)
    {
        if (CheckOfferWage(command.CompanionId, command.Copper) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Hero companion = State.GetHero(command.CompanionId);
        hero.Coins -= command.Copper;
        companion.Coins += command.Copper;
        // A better offer than they were on is their wage from now on.
        companion.Wage = Math.Max(companion.Wage, command.Copper);
        Stay(companion, hero, StayReason.Paid, events, command.Copper);
        return null;
    }

    private string? Handle(Convince command, List<GameEvent> events)
    {
        if (CheckConvince(command.CompanionId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Hero companion = State.GetHero(command.CompanionId);
        Turn.Convinced = true;
        int face = RollOneDie();
        int bond = BondBetween(companion, hero);
        bool won = face + bond >= ConvinceDifficulty;
        events.Add(new ConvinceRolled(hero.Id, companion.Id, face, bond, won));
        if (won)
        {
            Stay(companion, hero, StayReason.Convinced, events);
        }

        return null;
    }

    private string? Handle(Depart command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (!hero.Leaving || hero.LeavingWhy is null)
        {
            return "They aren't leaving yet.";
        }

        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        int seat = hero.Seat;
        ReleaseCarry(hero);
        events.Add(new HeroSpoke(hero.Id, Languages.CommonId, hero.LeavingWhy switch
        {
            LeavingWhy.Hurt => "Too hot for an unpaid hand. I'm gone!",
            LeavingWhy.Satisfied => "That squares what I'm owed. Good luck to you.",
            LeavingWhy.TooDeep => "This is as far as I go for nothing.",
            _ => "No offers? Then it's the inn for me.",
        }));
        hero.Leaving = false;
        hero.Departed = true;
        hero.Status = HeroStatus.Escaped;
        events.Add(new CompanionDeparted(hero.Id));
        events.Add(new HeroEscaped(hero.Id, hero.TreasureValue));
        EndRunIfOver(events);
        if (State.Phase == RunPhase.InProgress)
        {
            events.Add(new TurnEnded(hero.Id));
            AdvanceTurn(seat, events);
        }

        return null;
    }

    private void Stay(Hero companion, Hero? leader, StayReason reason, List<GameEvent> events, int paid = 0)
    {
        companion.Leaving = false;
        companion.Lingering = false;
        companion.LeavingWhy = null;
        if (leader is not null)
        {
            companion.OwnerId = leader.Id;
        }

        events.Add(new CompanionStayed(companion.Id, leader?.Id, reason, paid));
    }

    /// <summary>
    /// A leaving companion's turn begins: perhaps a change of heart; else, while they still wait, they ask for offers
    /// (and their turn is spent waiting); their waiting done, this turn they go.
    /// </summary>
    private void LeavingTurn(Hero hero, List<GameEvent> events)
    {
        if (!hero.Leaving || !hero.IsActive)
        {
            return;
        }

        int face = RollOneDie();
        int bond = State.Heroes.Where(other => other != hero && other.IsInPlay).Select(other => BondBetween(hero, other)).DefaultIfEmpty(0).Max();
        if (face + bond >= ConvinceDifficulty)
        {
            events.Add(new HeroSpoke(hero.Id, Languages.CommonId, "...It's a long road back. Longer alone. Fine. I'm in."));
            Stay(hero, null, StayReason.ChangeOfHeart, events);
            return;
        }

        // How they mean to go, settled on their first turn: a third each way.
        if (hero.LeavingPlan is null)
        {
            hero.LeavingPlan = RollOneDie() switch { <= 2 => LeavingPlan.Entrance, <= 4 => LeavingPlan.FirstRoom, _ => LeavingPlan.FewRooms };
            hero.LeavingTreasure = hero.TreasureValue;
        }

        int explored = State.Rooms.Values.Count(room => room.Explored);
        bool deeper = State.Heroes.Any(other => other.DeepestFloor > 1) || State.Rooms.Values.Any(room => room.Explored && room.Monsters.Any(monster => monster.Definition.Boss));
        hero.LeavingWhy =
            hero.Hearts * 2 <= hero.MaxHearts ? LeavingWhy.Hurt
            : hero.Wage > 0 && hero.TreasureValue - hero.LeavingTreasure >= hero.Wage ? LeavingWhy.Satisfied
            : hero.LeavingPlan == LeavingPlan.Entrance && hero.LeavingTurns <= 0 ? LeavingWhy.NoOffers
            : hero.LeavingPlan == LeavingPlan.FirstRoom && explored > 1 && !ActiveRoomHasFight(hero) ? LeavingWhy.TooDeep
            : hero.LeavingPlan == LeavingPlan.FewRooms && (explored > LeavingRooms || deeper) && !ActiveRoomHasFight(hero) ? LeavingWhy.TooDeep
            : null;

        // At the entrance they only wait; the others play their turn. All ask for offers their first few turns.
        hero.Lingering = hero.LeavingWhy is null && hero.LeavingPlan == LeavingPlan.Entrance;
        if (hero.LeavingWhy is null && hero.LeavingTurns > 0)
        {
            events.Add(new HeroSpoke(hero.Id, Languages.CommonId, LeavingLines[(face + hero.LeavingTurns) % LeavingLines.Length]));
        }

        hero.LeavingTurns = Math.Max(0, hero.LeavingTurns - 1);
    }

    /// <summary>Whether there's a fight on where the hero stands: they see it out before going (unless it's too hot).</summary>
    private bool ActiveRoomHasFight(Hero hero) => State.RoomOf(hero).Monsters.Any(monster => !monster.Asleep);
}
