using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Learning a spell from a scroll: an Od Hand roll plus the hero's Magic rank against 6 plus the spell's
/// level. It uses up the scroll either way. Beaten, the hero knows the spell for good; failed, the scroll
/// goes off in their hands, centered on them: its dice attack them and everyone on the tiles around
/// them, friend and foe, friendly fire or no. Magic is dangerous.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What a roll to learn a spell from a scroll must reach: 6 plus the spell's level.</summary>
    public static int LearnDifficulty(SpellDefinition spell) => GameRules.LearnBase + spell.Level;

    /// <summary>Why the active hero can't try to learn the spell on the scroll <paramref name="cardId"/> now, or null.</summary>
    public string? CheckLearnSpell(int cardId) => CheckOdHandStart() ?? FindScrollSpell(cardId, out _, out _);

    private string? Handle(LearnSpell command, List<GameEvent> events)
    {
        string? findError = FindScrollSpell(command.CardId, out CardInstance? scroll, out SpellDefinition? spell);
        if ((CheckOdHandCommand(command, out int dice) ?? findError) is { } error)
        {
            return error;
        }

        // The scroll is spent on the attempt, whatever comes of it.
        Hero hero = ActiveHero;
        if (scroll!.Count > 1)
        {
            scroll.Count--;
            scroll.BroughtInCount = Math.Min(scroll.BroughtInCount, scroll.Count);
            events.Add(new CardConsumed(hero.Id, scroll.Id, scroll.Count));
        }
        else
        {
            hero.Remove(scroll);
            events.Add(new CardConsumed(hero.Id, scroll.Id));
        }

        events.Add(new SpellStudied(hero.Id, scroll.Id, spell!.Id));
        RollOdHand(command, dice, new OdHandCheck(OdHandCheckKind.Learn, LearnDifficulty(spell), hero.Position, SpellId: spell.Id), events);
        return null;
    }

    /// <summary>The scroll the hero holds and the spell it teaches, or why it can't be learned.</summary>
    private string? FindScrollSpell(int cardId, out CardInstance? scroll, out SpellDefinition? spell)
    {
        scroll = null;
        spell = null;
        Hero hero = ActiveHero;
        if (hero.FindCard(cardId) is not var (card, _))
        {
            return "You aren't carrying that.";
        }

        if (card.Definition.Teaches is not { } spellId)
        {
            return $"There's no spell to learn from the {card.Definition.Name}.";
        }

        spell = Content.GetSpell(spellId);
        if (hero.Spells.Any(known => known.Id == spellId))
        {
            return $"You know {spell.Name} already.";
        }

        scroll = card;
        return null;
    }

    /// <summary>The roll is in: the spell is learned, or the scroll goes off around the hero.</summary>
    private void ResolveLearning(Hero hero, Room room, string spellId, bool success, List<GameEvent> events)
    {
        SpellDefinition spell = Content.GetSpell(spellId);
        if (success)
        {
            hero.Learn(spell);
            events.Add(new SpellLearned(hero.Id, spell.Id));
            return;
        }

        List<int> caught =
        [
            .. State.ActiveHeroesIn(room).Where(other => other.Position.ChebyshevDistanceTo(hero.Position) <= 1).Select(other => other.Id),
            .. room.Monsters.Where(monster => monster.Tiles.Any(tile => tile.ChebyshevDistanceTo(hero.Position) <= 1)).Select(monster => monster.Id),
        ];
        events.Add(new Backfired(hero.Id, spell.Id, caught));
        RollHeroAttack(hero, 0, 0, caught, DamageSource.Spell, events, typed: [(spell.DieType, spell.Dice)]);
    }
}
