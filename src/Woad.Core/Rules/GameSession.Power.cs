using Woad.Core.Commands;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Power Dice. A 6 on a Star die (any plain die) on an attack, move or Od Hand roll surges: one Power
/// Die rolls straight into the roll, and another goes into the bank, or with the bank full rolls in
/// too; a Power Die's own 6 surges again. Monsters' attacks surge the same way. Defense banks its
/// sixes, rolls them in when the bank is full, and spends banked dice to block. Typed dice (fire,
/// ice...) earn none: their 6s roll more of their own type instead.
/// </summary>
public sealed partial class GameSession
{
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

    /// <summary>
    /// Sixes on the hero's pending roll or move surge (see <see cref="GameSession"/>). Then a pending roll
    /// with no Luck left to reroll it resolves.
    /// </summary>
    private void EarnPowerDice(Hero hero, int sixes, List<GameEvent> events)
    {
        int banked = 0;
        for (int left = sixes; left > 0; left--)
        {
            left += AddPowerDie(hero, events);
            if (!hero.Power.IsFull)
            {
                hero.Power.Banked++;
                banked++;
            }
            else
            {
                left += AddPowerDie(hero, events);
            }
        }

        if (banked > 0)
        {
            events.Add(new PowerDiceBanked(hero.Id, banked, hero.Power.Banked));
        }

        if (Turn.PendingRoll is { } pending && hero.LuckRemaining < 1)
        {
            Turn.PendingRoll = null;
            ResolvePendingRoll(pending, events);
        }
    }

    /// <summary>Rolls a Power Die into the pending roll, or else the move in progress. Returns 1 when it came up 6 (to surge again).</summary>
    private int AddPowerDie(Hero hero, List<GameEvent> events)
    {
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

        return face == GameRules.PowerDieFace ? 1 : 0;
    }

    /// <summary>
    /// A monster's attack dice. Star dice surge: each 6 rolls a Power Die straight in and banks another
    /// (or with its bank full, rolls that in too), and so on. Typed dice (its <see cref="MonsterDefinition.DieType"/>)
    /// roll another of their type in for each 6 instead. Returns the faces' types, the Power Dice
    /// rolled in (the last faces) and the dice banked.
    /// </summary>
    /// <param name="star">Plain dice whatever the monster's type (its banked Power Dice, its chain).</param>
    private (DiceRoll Roll, IReadOnlyList<DieType> Types, int PowerDice, int Banked) RollMonsterDice(Monster monster, int dice, bool star = false)
    {
        if (monster.Definition.DieType is { } type && !star)
        {
            (int[] faces, _) = RollBonusDice(dice);
            return (new DiceRoll(faces), [.. faces.Select(_ => type)], 0, 0);
        }

        DiceRoll roll = _dice.Roll(dice);
        int power = 0, banked = 0;
        for (int left = Sixes(roll.Faces); left > 0; left--)
        {
            for (int surge = monster.Power.IsFull ? 2 : 1; surge > 0; surge--)
            {
                int face = RollOneDie();
                roll = roll.With(face);
                power++;
                left += face == GameRules.PowerDieFace ? 1 : 0;
            }

            if (!monster.Power.IsFull)
            {
                monster.Power.Banked++;
                banked++;
            }
        }

        return (roll, [], power, banked);
    }

    /// <summary>Announces Power Dice a roll put in the bank.</summary>
    private static void NoteBanked(int ownerId, PowerBank bank, int banked, List<GameEvent> events)
    {
        if (banked > 0)
        {
            events.Add(new PowerDiceBanked(ownerId, banked, bank.Banked));
        }
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
