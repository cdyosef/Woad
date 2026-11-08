using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Languages: who understands what (Gurgle and Gravel are tongues of Primordial), and Leviathan Jr.'s grumbling.</summary>
public sealed class LanguageTests
{
    [Fact]
    public void EveryoneSpeaksCommonAndAppearanceListsTheRest()
    {
        Assert.Equal([Languages.Common, Languages.Gurgle], new Appearance(Languages: "gurgle,klingon").Speaks);
        Assert.Equal([Languages.Common], Appearance.Default.Speaks);
    }

    [Fact]
    public void PrimordialSpeakersMakeOutSomeGurgleAndAllGravelSlowly()
    {
        Language[] primordial = [Languages.Common, Languages.Primordial];
        Assert.Equal(Understanding.Some, Languages.Of(Languages.Gurgle, primordial));
        Assert.Equal(Understanding.Slowly, Languages.Of(Languages.Gravel, primordial));
        Assert.Equal(Understanding.Fully, Languages.Of(Languages.Gurgle, [Languages.Common, Languages.Gurgle]));
        Assert.Equal(Understanding.None, Languages.Of(Languages.Gurgle, [Languages.Common]));
        Assert.Equal(Understanding.Fully, Languages.Of(Languages.Common, []));

        Assert.True(Languages.MakesOut(Languages.Gurgle, Understanding.Some, "freaks!"));
        Assert.False(Languages.MakesOut(Languages.Gurgle, Understanding.Some, "relax!"));
        Assert.Equal(Understanding.Some, Languages.Best(Languages.Gurgle, [[Languages.Common], primordial]));
    }

    private const string Pool = """
        .........
        .........
        ...===...
        ...=¦=...
        ....1....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void ItGrumblesALineARoundInOrderThenFallsQuiet()
    {
        GameSession session = TestRun.Start(Pool, new ScriptedDice(1, 1, 1, 1, 1, 1, 1, 1, 1), stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 20 });
        Monster grumbler = session.OnlyMonster();

        string?[] said = [.. Enumerable.Range(0, 3).Select(_ => session.EndRound().OfType<MonsterSpoke>().SingleOrDefault()?.Text)];

        Assert.Equal(["I am relaxing!", "Go away!", null], said);
        Assert.Equal("gurgle", grumbler.Definition.Speech!.Language);
    }

    [Fact]
    public void TheFirstTimeItDragsSomeoneDownItSaysItsDragLineFirst()
    {
        GameSession session = TestRun.Start(Pool, new ScriptedDice(6, 2, 1, 1, 1, 1), stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 0, AttackDice = 2 });

        IReadOnlyList<GameEvent> events = session.EndRound();

        List<GameEvent> said = [.. events.Where(gameEvent => gameEvent is MonsterSpoke or DraggedUnder)];
        Assert.Equal("Come on in!", Assert.IsType<MonsterSpoke>(said[^2]).Text);
        Assert.IsType<DraggedUnder>(said[^1]);
    }
}
