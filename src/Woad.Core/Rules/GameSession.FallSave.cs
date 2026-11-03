using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The save after a fall to a lower floor: what lights and spells a hero has (torches in hand, the flame of
/// Hold Fire, Shining Light, Shadowbind, Snowshoes) stay only if one exploding die and the Focus Bonus reach
/// <see cref="GameRules.TorchFallSave"/> for each floor fallen. Short of it, the hero can boost it: add the
/// Od Hand dice they have unspent or their banked Power Dice, one at a time (exploding too), or try their Luck on a die. The save waits
/// on the hero (see <see cref="Hero.PendingSave"/>) until they let it stand, it's made, or their next turn
/// starts; a companion's, or one that can't be boosted, settles at once.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The hero has lights or spells a fall could put out.</summary>
    private static bool HasLightsAtStake(Hero hero) =>
        hero.Flame || hero.EquippedCards.Any(card => card.Definition.Torch)
        || hero.ShiningRounds > 0 || hero.ShadowboundRounds > 0 || hero.SnowshoesRounds > 0;

    /// <summary>
    /// Od Hand dice a hero has unspent: on their turn, what's left of it; off it, what they didn't spend on
    /// their last turn (spent dice come back only at the start of their own turn).
    /// </summary>
    public int OdHandUnspent(Hero hero) =>
        hero.IsCarrying ? 0
        : State.Turn?.HeroId == hero.Id ? OdHandDiceLeft
        : Math.Max(0, hero.EffectiveStats.OdHandDice - hero.OdHandSpent);

    /// <summary>Spends one of a hero's unspent Od Hand dice, on their turn or off it.</summary>
    private void SpendOdHandDie(Hero hero)
    {
        if (State.Turn?.HeroId == hero.Id)
        {
            Turn.OdHandDiceUsed++;
        }
        else
        {
            hero.OdHandSpent++;
        }
    }

    /// <summary>A die that explodes: a 6 rolls another and adds it, and so on.</summary>
    private List<int> RollExploding()
    {
        var faces = new List<int>();
        int face;
        do
        {
            face = RollOneDie();
            faces.Add(face);
        }
        while (face == GameRules.PowerDieFace);

        return faces;
    }

    /// <summary>Landing after a fall of <paramref name="floors"/> floors: the save for what lights and spells they have.</summary>
    private void StartFallSave(Hero hero, int floors, List<GameEvent> events)
    {
        if (!HasLightsAtStake(hero))
        {
            return;
        }

        var save = new FallSave(hero.Focus, GameRules.TorchFallSave * floors);
        save.Faces.AddRange(RollExploding());
        events.Add(new FallSaveRolled(hero.Id, [.. save.Faces], save.Focus, save.Difficulty));
        hero.PendingSave = save;
        if (save.Made || hero.IsCompanion || !CanBoost(hero))
        {
            SettleFallSave(hero, events);
        }
    }

    /// <summary>Whether a hero has anything to boost a save with: unspent Od Hand dice, Luck, or banked Power Dice.</summary>
    private bool CanBoost(Hero hero) => OdHandUnspent(hero) > 0 || hero.LuckLeft > 0 || hero.Power.Banked > 0;

    /// <summary>Settles a hero's save: made, everything stays lit; missed, the torches in hand burn out and the flame and spells on them end.</summary>
    private void SettleFallSave(Hero hero, List<GameEvent> events)
    {
        if (hero.PendingSave is not { } save)
        {
            return;
        }

        hero.PendingSave = null;
        events.Add(new FallSaveSettled(hero.Id, save.Total, save.Difficulty, save.Made));
        if (save.Made)
        {
            return;
        }

        if (hero.Flame)
        {
            hero.Flame = false;
            events.Add(new FlameWentOut(hero.Id));
        }

        foreach (CardInstance torch in hero.EquippedCards.Where(card => card.Definition.Torch).ToList())
        {
            hero.Remove(torch);
            events.Add(new TorchBurnedOut(hero.Id, torch.Id));
        }

        foreach ((Boon boon, bool on) in new[] { (Boon.ShiningLight, hero.ShiningRounds > 0), (Boon.Shadowbound, hero.ShadowboundRounds > 0), (Boon.Snowshoes, hero.SnowshoesRounds > 0) })
        {
            if (on)
            {
                events.Add(new BoonEnded(hero.Id, boon));
            }
        }

        hero.ShiningRounds = hero.ShadowboundRounds = hero.SnowshoesRounds = 0;
    }

    /// <summary>Why the hero can't do something to their waiting save now, or null.</summary>
    public string? CheckFallSave(int heroId, bool boost = false, bool luck = false, bool power = false)
    {
        if (State.Heroes.FirstOrDefault(hero => hero.Id == heroId) is not { PendingSave: not null } hero)
        {
            return "There's no save waiting.";
        }

        return power ? (hero.Power.Banked < 1 ? "You've no Power Dice banked." : null)
            : boost && OdHandUnspent(hero) < 1 ? "You've no Od Hand dice left to add."
            : luck && hero.LuckLeft < 1 ? "You have no Luck left."
            : null;
    }

    private string? Handle(BoostFallSave command, List<GameEvent> events)
    {
        if (CheckFallSave(command.HeroId, boost: !command.Power, power: command.Power) is { } error)
        {
            return error;
        }

        Hero hero = State.GetHero(command.HeroId);
        FallSave save = hero.PendingSave!;
        if (command.Power)
        {
            SpendBankedPowerDice(hero, 1, events);
        }
        else
        {
            SpendOdHandDie(hero);
        }

        List<int> faces = RollExploding();
        save.Faces.AddRange(faces);
        events.Add(new FallSaveBoosted(hero.Id, [.. faces], save.Total));
        if (save.Made || !CanBoost(hero))
        {
            SettleFallSave(hero, events);
        }

        return null;
    }

    private string? Handle(LuckOnFallSave command, List<GameEvent> events)
    {
        if (CheckFallSave(command.HeroId, luck: true) is { } error)
        {
            return error;
        }

        Hero hero = State.GetHero(command.HeroId);
        FallSave save = hero.PendingSave!;
        if (command.DieIndex < 0 || command.DieIndex >= save.Faces.Count)
        {
            return "There's no such die.";
        }

        if (save.Faces[command.DieIndex] == GameRules.PowerDieFace)
        {
            return "That die is a 6 already: Luck can't better it.";
        }

        // The Luck die rolls over it, and the higher stays; a lucky 6 isn't spent.
        int old = save.Faces[command.DieIndex];
        int luck = RollOneDie();
        if (luck != GameRules.PowerDieFace)
        {
            hero.SpendLuck();
        }

        save.Faces[command.DieIndex] = Math.Max(old, luck);
        events.Add(new DieRerolled(hero.Id, command.DieIndex, old, save.Faces[command.DieIndex], new Dice.DiceRoll([.. save.Faces]), null) { LuckFace = luck });
        if (save.Made || !CanBoost(hero))
        {
            SettleFallSave(hero, events);
        }

        return null;
    }

    private string? Handle(LetSaveStand command, List<GameEvent> events)
    {
        if (CheckFallSave(command.HeroId) is { } error)
        {
            return error;
        }

        SettleFallSave(State.GetHero(command.HeroId), events);
        return null;
    }

    /// <summary>Why the active hero can't add an Od Hand die to the Focus check waiting on Luck, or null.</summary>
    public string? CheckAddOdHandDie(bool power = false) =>
        State.Turn?.PendingRoll is not { Check.Kind: OdHandCheckKind.Learn } ? "Only a Focus check (learning a spell) can take more dice now."
        : power ? (ActiveHero.Power.Banked < 1 ? "You've no Power Dice banked." : null)
        : OdHandDiceLeft < 1 ? "You've no Od Hand dice left to add."
        : null;

    private string? Handle(AddOdHandDie command, List<GameEvent> events)
    {
        if (CheckAddOdHandDie(command.Power) is { } error)
        {
            return error;
        }

        // A die more on the Focus check, an Od Hand die or a banked Power Die: it waits again while there's more to add, or Luck to try.
        PendingRoll pending = Turn.PendingRoll!;
        if (command.Power)
        {
            SpendBankedPowerDice(ActiveHero, 1, events);
        }
        else
        {
            Turn.OdHandDiceUsed++;
        }

        int face = RollOneDie();
        pending.Roll = pending.Roll.With(face);
        events.Add(new OdHandDieAdded(command.HeroId, face, pending.Total));
        EarnPowerDice(ActiveHero, face == GameRules.PowerDieFace ? 1 : 0, events);
        return null;
    }
}
