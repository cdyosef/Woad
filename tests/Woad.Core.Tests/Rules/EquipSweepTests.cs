using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>
/// Every card in the real game that goes in a slot, put through equipping: on, into the other hand if it takes
/// either, stowed, on again, and (in a hand) swapped with a shield and a dagger. Nothing may be lost, doubled, or
/// left half on (a two-handed weapon in one hand), and the hero's stats must still add up.
/// </summary>
public sealed class EquipSweepTests
{
    private static readonly ContentCatalog Real = ContentCatalog.LoadDefault();

    public static TheoryData<string> SlotCards()
    {
        var data = new TheoryData<string>();
        // Not a flame of Hold Fire (it's hurled, dropped or handed on, never stowed), nor a Lit Stick (it burns out
        // in 5 rounds, and each step here takes a round).
        foreach (CardDefinition card in Real.Cards.Values
                     .Where(card => card.Slot is not null && card.Slot is not (EquipSlot.Belt or EquipSlot.Buckle) && card.ModFor is null && !card.HeldFire && card.Id != "lit_stick")
                     .OrderBy(card => card.Id))
        {
            data.Add(card.Id);
        }

        return data;
    }

    private static GameSession Start(params string[] cards)
    {
        var room = RoomTemplate.Parse(".........\n.........\n.........\n.........\n....1....\n.........\n.........\n.........\n.........", Real);
        var setup = new RunSetup
        {
            Seed = 7,
            ThemeId = "emerald_keep",
            Heroes = [new HeroSetup("Sweep", GameRules.HeroBaseStats with { OdHandDice = 1 }, ["basic_belt", "basic_buckle", .. cards], Skills: Skills.All)],
            Rooms = [new RoomSetup("yard", room)],
            StartRoomId = "yard",
        };
        var session = new GameSession(Real, setup, new GameRandom(7));
        session.Begin();
        return session;
    }

    /// <summary>What's wrong with the hero's things, or null: every card once, two-handed gear in both hands, stats that add up.</summary>
    private static string? Check(Hero hero, int expected, string step)
    {
        List<CardInstance> all = [.. hero.EquippedCards, .. hero.Mods, .. hero.Belt, .. hero.Backpack, .. hero.Carried];
        if (all.Count != all.Distinct().Count())
        {
            return $"{step}: a card is in two places";
        }

        if (all.Sum(card => card.Count) != expected)
        {
            return $"{step}: {all.Sum(card => card.Count)} cards, expected {expected}";
        }

        if (hero.Equipment.GetValueOrDefault(EquipSlot.MainHand) is { Definition.TwoHanded: true } main && hero.Equipment.GetValueOrDefault(EquipSlot.OffHand) != main)
        {
            return $"{step}: the two-handed {main.Definition.Name} is in one hand";
        }

        foreach ((EquipSlot slot, CardInstance card) in hero.Equipment)
        {
            bool fits = card.Definition.Slot == slot || card.Definition.AltSlot == slot
                        || (slot == EquipSlot.OffHand && card.Definition.TwoHanded)
                        || (slot is EquipSlot.MainHand or EquipSlot.OffHand && card.Definition.Type is CardType.Tool or CardType.Accessory);
            if (!fits)
            {
                return $"{step}: the {card.Definition.Name} sits in the {slot}";
            }
        }

        _ = hero.EffectiveStats;
        return null;
    }

    [Theory]
    [MemberData(nameof(SlotCards))]
    public void EveryCardWithASlotGoesOnComesOffAndSwapsCleanly(string id)
    {
        GameSession session = Start([id, .. new[] { "buckler", "dagger" }.Where(other => other != id)]);
        Hero hero = session.HeroInSeat();
        int expected = hero.AllCards.Sum(card => card.Count);
        CardInstance card = hero.AllCards.First(each => each.Definition.Id == id);
        List<string> problems = [];

        void Step(string step, GameCommand command, bool mustWork = true)
        {
            CommandResult result = session.Execute(command);
            if (mustWork && !result.IsAccepted)
            {
                problems.Add($"{step}: refused ({result.Error})");
            }

            if (Check(hero, expected, step) is { } problem)
            {
                problems.Add(problem);
            }

            session.EndRound();
        }

        if (Check(hero, expected, "start") is { } start)
        {
            problems.Add(start);
        }

        if (hero.SlotOf(card) is null)
        {
            Step("equip", new EquipCard(hero.Id, card.Id));
        }

        EquipSlot? on = hero.SlotOf(card);
        if (on is null)
        {
            problems.Add("not on after equipping");
        }

        // A card that goes in either hand moves to the other.
        if (card.Definition.AltSlot is { } alt && on is { } first && !card.Definition.TwoHanded)
        {
            EquipSlot other = first == card.Definition.Slot ? alt : card.Definition.Slot!.Value;
            Step($"to the {other}", new EquipCard(hero.Id, card.Id, other));
            if (hero.SlotOf(card) != other)
            {
                problems.Add($"didn't move to the {other} (in the {hero.SlotOf(card)})");
            }
        }

        Step("stow", new StowCard(hero.Id, card.Id));
        if (hero.SlotOf(card) is not null || !hero.Backpack.Contains(card))
        {
            problems.Add($"stowing left it in the {hero.SlotOf(card)}");
        }

        Step("equip again", new EquipCard(hero.Id, card.Id));

        // In a hand: a shield and a dagger go on after it, then it goes back on.
        if (hero.SlotOf(card) is EquipSlot.MainHand or EquipSlot.OffHand)
        {
            foreach (string swap in new[] { "buckler", "dagger" })
            {
                if (hero.AllCards.FirstOrDefault(each => each.Definition.Id == swap) is { } other && hero.SlotOf(other) is null)
                {
                    Step($"then the {swap}", new EquipCard(hero.Id, other.Id), mustWork: false);
                }
            }

            if (hero.SlotOf(card) is null)
            {
                Step("back on", new EquipCard(hero.Id, card.Id));
            }
        }

        Assert.True(problems.Count == 0, $"{id}: {string.Join("; ", problems)}");
    }
}
