using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Powering up: the hero puts every die they have left this turn (Rumble, Wend and Od Hand) into one roll, and each 6
/// becomes a banked Power Die (as far as the bank has room). It takes the rest of the turn: the turn ends.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The dice a power-up would roll: every Rumble, Wend and Od Hand die left this turn.</summary>
    public int PowerUpDice => State.Turn is null ? 0 : AttackDiceLeft + MoveDiceLeft + OdHandDiceLeft;

    /// <summary>Why the active hero can't power up now, or null.</summary>
    public string? CheckPowerUp()
    {
        if (State.Turn is not { KeelingOver: false, PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return State.Turn is { KeelingOver: true } ? "Keeling Over, there's only the Keel Die." : "Finish the roll first.";
        }

        if (PowerUpDice == 0)
        {
            return "You've no dice left to roll for power.";
        }

        return ActiveHero.Power.IsFull ? "Your Power Dice bank is full." : null;
    }

    private string? Handle(PowerUp command, List<GameEvent> events)
    {
        if (CheckPowerUp() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        TurnState turn = Turn;
        int dice = PowerUpDice;
        Stats stats = hero.EffectiveStats;
        turn.AttackDiceUsed = Math.Max(turn.AttackDiceUsed, stats.AttackDice);
        turn.MoveDiceUsed = Math.Max(turn.MoveDiceUsed, stats.MoveDice + turn.ExtraMoveDice);
        turn.OdHandDiceUsed = Math.Max(turn.OdHandDiceUsed, stats.OdHandDice);
        turn.MovementPoints = 0;
        DiceRoll roll = _dice.Roll(dice);
        int sixes = roll.Faces.Count(face => face == GameRules.PowerDieFace);
        int banked = Math.Min(sixes, hero.Power.Capacity - hero.Power.Banked);
        hero.Power.Banked += banked;
        events.Add(new PoweredUp(hero.Id, roll, banked, hero.Power.Banked));
        return Handle(new EndTurn(hero.Id), events);
    }
}
