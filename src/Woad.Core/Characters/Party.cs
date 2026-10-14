using Woad.Core.Rules;

namespace Woad.Core.Characters;

public static class Party
{
    /// <summary>
    /// The heroes for a run from characters in seat order. Each companion belongs to the nearest
    /// player seated before it.
    /// </summary>
    /// <exception cref="ArgumentException">A companion has no player seated before it.</exception>
    public static IReadOnlyList<HeroSetup> ToHeroSetups(IReadOnlyList<Character> seated)
    {
        var heroes = new List<HeroSetup>();
        int? owner = null;
        for (int seat = 0; seat < seated.Count; seat++)
        {
            Character character = seated[seat];
            if (!character.IsCompanion)
            {
                owner = seat;
                heroes.Add(character.ToHeroSetup());
                continue;
            }

            if (owner is null)
            {
                throw new ArgumentException($"{character.Name} is a companion and needs a player seated before them.", nameof(seated));
            }

            heroes.Add(character.ToHeroSetup(owner));
        }

        return heroes;
    }
}
