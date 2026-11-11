using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Companions with a persona of their own say their piece at the start of some of their turns. Clod Hopper, frog hunter
/// of the bog country, is sure every big wet thing is a frog: he mutters at the big ones, calls to the water ones (in
/// what Gurgle he picked up in the marsh), grumbles when hurt, and makes himself at home in the water. Never more than
/// once every few rounds, so he doesn't go on.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Clod Hopper's persona.</summary>
    public const string ClodPersona = "clod";

    /// <summary>Rounds a persona keeps quiet after speaking.</summary>
    private const int PersonaQuiet = 3;

    private static readonly string[] ClodBig = ["That's no newt...", "Now THAT is a frog worth gigging.", "Easy... easy... nobody croak."];
    private static readonly string[] ClodWet = ["Here, froggy froggy.", "Come to the gig, little one.", "You are a long frog. Join me."];
    private static readonly string[] ClodHurt = ["Ribbit... I mean, ow.", "Bit me! The cheek of it.", "Granny's poultice, where'd I put it..."];
    private static readonly string[] ClodWater = ["Smells like home.", "Ahh, a proper bog.", "Feet wet, heart happy."];
    private static readonly string[] ClodIdle = ["Never trust a toad that smiles.", "My granny gigged one the size of a cart.", "Quiet. Too quiet. Not even a peep.", "If it hops, it's mine."];

    private void PersonaSpeaks(Hero hero, List<GameEvent> events)
    {
        if (hero.Appearance.Persona != ClodPersona || !hero.IsActive || hero.SpokeRound is int last && State.Round - last < PersonaQuiet)
        {
            return;
        }

        Room room = State.RoomOf(hero);
        List<Monster> awake = [.. room.Monsters.Where(monster => !monster.Asleep && monster.Position.ChebyshevDistanceTo(hero.Position) <= 8)];
        (string Language, string[] Lines)? said =
            awake.FirstOrDefault(monster => monster.Definition.Swimmer || monster.Definition.WaterOnly) is not null
                ? (hero.Appearance.Speaks.Any(language => language.Id == Languages.Gurgle.Id) && RollOneDie() >= 4 ? Languages.Gurgle.Id : Languages.CommonId, ClodWet)
            : awake.Any(monster => monster.Definition.Boss || monster.Definition.Miniboss || monster.Tiles.Count() > 1) ? (Languages.CommonId, ClodBig)
            : hero.Hearts * 3 <= hero.MaxHearts ? (Languages.CommonId, ClodHurt)
            : InWater(room, hero.Position) ? (Languages.CommonId, ClodWater)
            : RollOneDie() == 6 ? (Languages.CommonId, ClodIdle)
            : null;
        if (said is not { } line)
        {
            return;
        }

        hero.SpokeRound = State.Round;
        events.Add(new HeroSpoke(hero.Id, line.Language, line.Lines[RollOneDie() % line.Lines.Length]));
    }
}
