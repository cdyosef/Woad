using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Characters;

/// <summary>
/// Learning a spell from a scroll off the board (on the Woarld Map or in town): the same Od Hand roll plus
/// Magic as in a dungeon (see <c>GameSession.LearnSpell</c>), its sixes rolling another die straight in.
/// The scroll is used up either way. Failed, it goes off among the party, who travel together: its dice
/// attack each of them, and each one it beats loses a heart.
/// </summary>
public static class ScrollStudy
{
    /// <summary>What came of an attempt: whether it was learned, the roll, and who the backfire wounded.</summary>
    public sealed record Result(bool Learned, SpellDefinition Spell, DiceRoll Roll, int Total, int Difficulty, IReadOnlyList<Character> Wounded, int Backfire, string Message);

    /// <summary>Why <paramref name="learner"/> can't try to learn from the scroll <paramref name="cardKey"/>, or null.</summary>
    public static string? Check(Character learner, string cardKey, ContentCatalog content)
    {
        if (content.FindCard(cardKey) is not { Teaches: { } spellId } card)
        {
            return "There's no spell on that to learn.";
        }

        if (!learner.Equipped.Concat(learner.Belt).Concat(learner.Backpack).Contains(cardKey))
        {
            return $"{learner.Name} isn't carrying the {card.Name}.";
        }

        return learner.Spells.Contains(spellId) ? $"{learner.Name} knows {content.GetSpell(spellId).Name} already." : null;
    }

    /// <summary>Spends the scroll on an attempt to learn its spell; a backfire attacks everyone in <paramref name="party"/> (the learner too).</summary>
    /// <exception cref="InvalidOperationException">The attempt isn't allowed (see <see cref="Check"/>).</exception>
    public static Result Attempt(Character learner, string cardKey, IReadOnlyList<Character> party, ContentCatalog content, IRandomSource random)
    {
        if (Check(learner, cardKey, content) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        SpellDefinition spell = content.GetSpell(content.GetCard(cardKey).Teaches!);
        learner.TakeCard(cardKey);
        var dice = new DiceRoller(random);
        DiceRoll roll = Exploding(dice, Math.Max(1, learner.Stats(content).OdHandDice));
        int total = roll.Total + Math.Max(0, learner.Stats(content).Magic);
        int difficulty = GameSession.LearnDifficulty(spell);
        if (total >= difficulty)
        {
            learner.LearnFromScroll(spell.Id);
            return new Result(true, spell, roll, total, difficulty, [], 0, $"{learner.Name} studies the scroll ({total}) and learns {spell.Name}!");
        }

        // It goes off among them: each defends with a die and their armor.
        int backfire = Exploding(dice, spell.Dice).Total;
        List<Character> wounded = [.. party.Append(learner).Distinct().Where(hero => backfire > dice.Roll(1).Total + Math.Max(0, hero.Stats(content).Armor))];
        string hurt = wounded.Count == 0 ? "nobody is hurt" : $"{string.Join(", ", wounded.Select(hero => hero.Name))} {(wounded.Count == 1 ? "loses" : "lose")} a heart";
        return new Result(false, spell, roll, total, difficulty, wounded, backfire,
            $"{learner.Name} studies the scroll ({total}, needing {difficulty}) and it goes off in their hands ({backfire}): {hurt}.");
    }

    /// <summary>Dice whose sixes roll another die straight in.</summary>
    private static DiceRoll Exploding(DiceRoller dice, int count)
    {
        var faces = new List<int>(dice.Roll(count).Faces);
        for (int sixes = faces.Count(face => face == GameRules.PowerDieFace); sixes > 0; sixes--)
        {
            int face = dice.Roll(1).Faces[0];
            faces.Add(face);
            sixes += face == GameRules.PowerDieFace ? 1 : 0;
        }

        return new DiceRoll(faces);
    }
}
