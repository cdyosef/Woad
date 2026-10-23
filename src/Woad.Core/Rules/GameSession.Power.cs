using Woad.Core.Commands;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Power Dice: every 6 rolled earns one. On their own attacks and moves heroes choose to add each to
/// that roll or bank it; a full bank adds it at once. Defense banks its sixes and spends banked dice
/// to block. Monsters add the sixes on their attacks straight away and defend like heroes.
/// </summary>
public sealed partial class GameSession
{
    private string? Handle(AddPowerDie command, List<GameEvent> events)
    {
        if (Turn.UnsettledPowerDice < 1)
        {
            return "There's no Power Die to add.";
        }

        AddEarnedPowerDie(ActiveHero, events);
        SettlePowerDice(ActiveHero, events);
        return null;
    }

    private string? Handle(BankPowerDie command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (Turn.UnsettledPowerDice < 1)
        {
            return "There's no Power Die to bank.";
        }

        if (hero.Power.IsFull)
        {
            return "Your Power Dice bank is full.";
        }

        Turn.UnsettledPowerDice--;
        hero.Power.Banked++;
        events.Add(new PowerDiceBanked(hero.Id, 1, hero.Power.Banked));
        SettlePowerDice(hero, events);
        return null;
    }

    private static int Sixes(IEnumerable<int> faces) => faces.Count(face => face == GameRules.PowerDieFace);

    private int RollOneDie() => _dice.Roll(1).Faces[0];

    private static string? CheckBankedPowerDice(Hero hero, int count) =>
        count < 0 || count > hero.Power.Banked
            ? $"You have {hero.Power.Banked} banked Power {(hero.Power.Banked == 1 ? "Die" : "Dice")}."
            : null;

    /// <summary>Takes banked dice out to roll into an attack or move.</summary>
    private static void SpendBankedPowerDice(Hero hero, int count, List<GameEvent> events)
    {
        if (count > 0)
        {
            hero.Power.Banked -= count;
            events.Add(new PowerDiceSpent(hero.Id, count, hero.Power.Banked));
        }
    }

    /// <summary>Sixes on the hero's pending attack or move: each waits to be added or banked.</summary>
    private void EarnPowerDice(Hero hero, int sixes, List<GameEvent> events)
    {
        Turn.UnsettledPowerDice += sixes;
        SettlePowerDice(hero, events);
    }

    /// <summary>
    /// After Power Dice change: a full bank rolls the rest in at once; any still waiting for the
    /// hero's choice are announced; and a pending attack with nothing left to decide resolves.
    /// </summary>
    private void SettlePowerDice(Hero hero, List<GameEvent> events)
    {
        while (Turn.UnsettledPowerDice > 0 && hero.Power.IsFull)
        {
            AddEarnedPowerDie(hero, events);
        }

        if (Turn.UnsettledPowerDice > 0)
        {
            events.Add(new PowerDiceEarned(hero.Id, Turn.UnsettledPowerDice));
        }
        else if (Turn.PendingRoll is { } pending && hero.LuckRemaining < 1)
        {
            Turn.PendingRoll = null;
            ResolvePendingRoll(pending, events);
        }
    }

    /// <summary>Rolls one earned Power Die into the pending attack, or else the move in progress. Its own 6 earns another.</summary>
    private void AddEarnedPowerDie(Hero hero, List<GameEvent> events)
    {
        Turn.UnsettledPowerDice--;
        int face = RollOneDie();
        if (Turn.PendingRoll is { } attack)
        {
            attack.Roll = attack.Roll.With(face);
            events.Add(new PowerDieAdded(hero.Id, face, attack.Roll, attack.Total));
        }
        else if (Turn.MovementRoll is { } roll)
        {
            Turn.MovementRoll = roll.With(face);
            Turn.CountSteps();
            events.Add(new PowerDieAdded(hero.Id, face, Turn.MovementRoll, Turn.MovementPoints));
        }

        if (face == GameRules.PowerDieFace)
        {
            Turn.UnsettledPowerDice++;
        }
    }

    /// <summary>A monster's attack roll: every 6 rolls another die in at once, and so on.</summary>
    private (DiceRoll Roll, int PowerDice) RollMonsterDice(int dice)
    {
        DiceRoll roll = _dice.Roll(dice);
        int power = 0;
        for (int earned = Sixes(roll.Faces); earned > 0; earned--)
        {
            int face = RollOneDie();
            roll = roll.With(face);
            power++;
            if (face == GameRules.PowerDieFace)
            {
                earned++;
            }
        }

        return (roll, power);
    }

    /// <summary>
    /// A defense roll for a hero or monster. Each 6 is banked if there's room, or else rolled in.
    /// A hero's <paramref name="guard"/> backs it up while it's losing: their Block, then their extra
    /// Defense dice one at a time. Then, while it's still losing and the banked dice could still make up
    /// the difference, they're rolled in one at a time; and last a lucky die. Emits the roll, then what
    /// was spent and banked.
    /// </summary>
    private DiceRoll RollDefense(int ownerId, PowerBank bank, int dice, int armor, int attackTotal, bool canSpend, List<GameEvent> events, Guard? guard = null)
    {
        DiceRoll roll = _dice.Roll(dice);
        int power = 0, banked = 0, spent = 0;
        int unsettled = Sixes(roll.Faces);

        void Settle()
        {
            for (; unsettled > 0; unsettled--)
            {
                if (!bank.IsFull)
                {
                    bank.Banked++;
                    banked++;
                    continue;
                }

                int face = RollOneDie();
                roll = roll.With(face);
                power++;
                if (face == GameRules.PowerDieFace)
                {
                    unsettled++;
                }
            }
        }

        Settle();
        int bonus = armor;
        if (guard is not null)
        {
            // Allies' Guard Dice first; a 6 on one rolls another straight in.
            while (attackTotal > roll.Total + bonus && guard.GuardUsed < guard.GuardDice)
            {
                guard.GuardUsed++;
                foreach (int face in RollBonusDice(1).Faces)
                {
                    roll = roll.With(face);
                }
            }

            if (attackTotal > roll.Total + bonus && guard.Block > 0)
            {
                guard.BlockUsed = guard.Block;
                bonus += guard.Block;
            }

            while (attackTotal > roll.Total + bonus && guard.DiceUsed < guard.Dice)
            {
                guard.DiceUsed++;
                int face = RollOneDie();
                roll = roll.With(face);
                unsettled += face == GameRules.PowerDieFace ? 1 : 0;
                Settle();
            }
        }

        while (canSpend && bank.Banked > 0 && attackTotal > roll.Total + bonus
               && attackTotal - (roll.Total + bonus) <= 6 * bank.Banked)
        {
            bank.Banked--;
            spent++;
            int face = RollOneDie();
            roll = roll.With(face);
            power++;
            unsettled += face == GameRules.PowerDieFace ? 1 : 0;
            Settle();
        }

        if (guard is { Lucky: true } && attackTotal > roll.Total + bonus)
        {
            guard.LuckyUsed = true;
            roll = roll.With(RollOneDie());
        }

        events.Add(new DefenseRolled(ownerId, roll, bonus, roll.Total + bonus, power)
        {
            Block = guard?.BlockUsed ?? 0,
            ExtraDice = guard?.DiceUsed ?? 0,
            GuardDice = guard?.GuardUsed ?? 0,
            LastChance = guard?.LuckyUsed ?? false,
        });
        if (spent > 0)
        {
            events.Add(new PowerDiceSpent(ownerId, spent, bank.Banked));
        }

        if (banked > 0)
        {
            events.Add(new PowerDiceBanked(ownerId, banked, bank.Banked));
        }

        return roll;
    }
}
