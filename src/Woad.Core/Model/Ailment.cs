namespace Woad.Core.Model;

/// <summary>
/// Whatever ails a hero or monster, in stacks: its condition tokens, plus being on fire (its fire dice)
/// and stunned. What Aid cures, and what the Sineater Doll soaks up and lets loose again.
/// </summary>
public enum Ailment
{
    Poison,
    Acid,
    Blind,
    Shine,
    Confused,
    Buried,
    Freeze,
    BlackHeart,
    Burning,
    Stun,
    Nekrot,
}

public static class Ailments
{
    /// <summary>What Aid cures with Od Hand alone.</summary>
    public static readonly IReadOnlyList<Ailment> AidCures = [Ailment.Poison, Ailment.Acid, Ailment.Blind, Ailment.Shine, Ailment.Confused, Ailment.Buried];

    /// <summary>What Aid cures only with a Medicine Bag to hand.</summary>
    public static readonly IReadOnlyList<Ailment> MedicineBagCures = [Ailment.Freeze, Ailment.BlackHeart, Ailment.Burning, Ailment.Stun, Ailment.Nekrot];

    public static readonly IReadOnlyList<Ailment> All = Enum.GetValues<Ailment>();

    /// <summary>The condition token an ailment is kept as, or null for Burning and Stun (kept apart).</summary>
    public static Condition? AsCondition(this Ailment ailment) => ailment switch
    {
        Ailment.Poison => Condition.Poison,
        Ailment.Acid => Condition.Acid,
        Ailment.Blind => Condition.Blind,
        Ailment.Shine => Condition.Shine,
        Ailment.Confused => Condition.Confused,
        Ailment.Buried => Condition.Buried,
        Ailment.Freeze => Condition.Freeze,
        Ailment.BlackHeart => Condition.BlackHeart,
        Ailment.Nekrot => Condition.Nekrot,
        _ => null,
    };

    public static string Name(this Ailment ailment) => ailment switch
    {
        Ailment.BlackHeart => "Black Hearts",
        Ailment.Burning => "Burning",
        Ailment.Stun => "Stun",
        _ => ailment.ToString(),
    };

    /// <summary>The stacks of it on someone: their tokens, fire dice, or stun (a Powerful Stun counting as one).</summary>
    public static int Stacks(this Ailment ailment, Conditions conditions, int fireDice, StunState stun) => ailment switch
    {
        Ailment.Burning => fireDice,
        Ailment.Stun => stun.Stacks + stun.PowerfulStacks,
        _ => conditions[ailment.AsCondition()!.Value],
    };
}
