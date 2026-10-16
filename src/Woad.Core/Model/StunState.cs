namespace Woad.Core.Model;

/// <summary>
/// Stuns on a hero or monster: Stun (the Boomerang's sixes) and Powerful Stun (bomb blasts). On its
/// next turn a stunned creature loses its Od Hand action and its free actions (free loot, free belt
/// item), or a Wend die if it has no Od Hand, and a flyer can't fly; each further Stun takes another
/// die, Wend dice first, then Rumble, then Defense. A Powerful Stun takes 2 more dice to begin with (3
/// without Od Hand) and 3 for each further one. Attacks on a stunned creature get +1 per Stun and +3
/// per Powerful Stun. It all wears off at the end of the creature's next turn.
/// </summary>
public sealed class StunState
{
    public int Stacks { get; private set; }

    public int PowerfulStacks { get; private set; }

    public bool IsStunned => Stacks + PowerfulStacks > 0;

    /// <summary>Added to the total of every attack on the stunned creature.</summary>
    public int AttackBonus => Stacks + 3 * PowerfulStacks;

    /// <summary>The creature's turn began stunned: the stun wears off at the end of it.</summary>
    internal bool WearsOffThisTurn { get; set; }

    /// <summary>Dice taken, beyond the Od Hand action (which goes with the first stack, if there is one).</summary>
    public int DiceLost(bool hasOdHand)
    {
        if (PowerfulStacks > 0)
        {
            return (hasOdHand ? 2 : 3) + 3 * (PowerfulStacks - 1) + Stacks;
        }

        return Stacks == 0 ? 0 : (hasOdHand ? 0 : 1) + Stacks - 1;
    }

    /// <summary>The dice taken, from the creature's Wend dice first, then Rumble, then Defense.</summary>
    public (int Wend, int Rumble, int Defense) Lost(bool hasOdHand, int wend, int rumble, int defense)
    {
        int left = DiceLost(hasOdHand);
        int Take(int pool)
        {
            int taken = Math.Min(left, Math.Max(0, pool));
            left -= taken;
            return taken;
        }

        return (Take(wend), Take(rumble), Take(defense));
    }

    internal void Add(bool powerful, int stacks = 1)
    {
        if (powerful)
        {
            PowerfulStacks += stacks;
        }
        else
        {
            Stacks += stacks;
        }
    }

    internal void Clear()
    {
        Stacks = 0;
        PowerfulStacks = 0;
        WearsOffThisTurn = false;
    }
}
