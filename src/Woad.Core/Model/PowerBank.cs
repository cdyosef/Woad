using Woad.Core.Rules;

namespace Woad.Core.Model;

/// <summary>
/// Power Dice a hero or monster has banked. Every 6 rolled earns one: it can be added to that roll
/// (an extra d6, whose own 6 earns another) or banked, up to its <see cref="Capacity"/>, to add to a
/// later roll or spend on a spell. Banked dice last until used.
/// </summary>
/// <param name="capacity">How many it holds, if not <see cref="GameRules.MaxBankedPowerDice"/> (a hero's grows with Magic).</param>
public sealed class PowerBank(Func<int>? capacity = null)
{
    public int Banked { get; internal set; }

    public int Capacity => capacity?.Invoke() ?? GameRules.MaxBankedPowerDice;

    public bool IsFull => Banked >= Capacity;
}
