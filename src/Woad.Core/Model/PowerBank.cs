using Woad.Core.Rules;

namespace Woad.Core.Model;

/// <summary>
/// Power Dice a hero or monster has banked. Every 6 rolled earns one: it can be added to that roll
/// (an extra d6, whose own 6 earns another) or banked, up to <see cref="GameRules.MaxBankedPowerDice"/>,
/// to add to a later roll. Banked dice last until used.
/// </summary>
public sealed class PowerBank
{
    public int Banked { get; internal set; }

    public bool IsFull => Banked >= GameRules.MaxBankedPowerDice;
}
