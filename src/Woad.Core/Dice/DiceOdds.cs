namespace Woad.Core.Dice;

/// <summary>Exact odds for comparing totals of d6 pools, used by monsters to plan their attacks.</summary>
public static class DiceOdds
{
    /// <summary>The chance that <paramref name="attackDice"/>d6 + <paramref name="attackBonus"/> beats
    /// <paramref name="defenseDice"/>d6 + <paramref name="defenseBonus"/> (ties go to the defender).</summary>
    public static double ChanceToBeat(int attackDice, int attackBonus, int defenseDice, int defenseBonus)
    {
        double[] attack = SumOdds(attackDice);
        double[] defense = SumOdds(defenseDice);
        double chance = 0;
        for (int a = 0; a < attack.Length; a++)
        {
            for (int d = 0; d < defense.Length; d++)
            {
                if (a + attackBonus > d + defenseBonus)
                {
                    chance += attack[a] * defense[d];
                }
            }
        }

        return chance;
    }

    /// <summary>The chance of each total (the index) when rolling <paramref name="dice"/>d6.</summary>
    private static double[] SumOdds(int dice)
    {
        double[] odds = [1];
        for (int die = 0; die < dice; die++)
        {
            var next = new double[odds.Length + 6];
            for (int total = 0; total < odds.Length; total++)
            {
                for (int face = 1; face <= 6; face++)
                {
                    next[total + face] += odds[total] / 6;
                }
            }

            odds = next;
        }

        return odds;
    }
}
