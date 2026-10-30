using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Learning a spell from a scroll: Od Hand plus Magic against 6 plus its level, or it backfires.</summary>
public class ScrollLearningTests
{
    private const string Huddle = """
        .........
        .........
        .........
        ....d....
        ....12...
        .........
        .........
        .........
        .......d.
        """;

    [Fact]
    public void BeatingSixPlusTheSpellsLevelLearnsItAndUsesUpTheScroll()
    {
        // One Od Hand die (3), the free die that starts every attempt (2) and Magic 4: 9, just enough for a level 3 spell.
        GameSession session = TestRun.Start(Huddle, new ScriptedDice(3, 2), cards: ["t_scroll", "t_scroll"], stats: TestRun.HeroStats with { OdHandDice = 2 });
        Hero hero = session.HeroInSeat();
        hero.BaseStats = hero.BaseStats with { Magic = 4 };
        CardInstance scroll = hero.Card("t_scroll");

        IReadOnlyList<GameEvent> events = session.Do(new LearnSpell(hero.Id, scroll.Id, Dice: 1));

        Assert.Equal(9, events.SingleOf<OdHandRolled>().Total);
        Assert.Equal(new SpellLearned(hero.Id, "flame_blast"), events.SingleOf<SpellLearned>());
        Assert.Contains(hero.Spells, spell => spell.Id == "flame_blast");
        Assert.Equal(1, scroll.Count);
        Assert.Contains("know Flame Blast already", session.Refuse(new LearnSpell(hero.Id, scroll.Id)));
    }

    [Fact]
    public void AFailedAttemptSetsTheScrollOffOnEveryoneAroundFriendlyFireOrNo()
    {
        // Od Hand 2 and the free die's 1 against 9. Then the scroll's three Fire dice (5, 5, 5) at the hero (defending with a 1),
        // their ally (a 1) and the dummy beside them; the dummy across the room is out of it.
        GameSession session = TestRun.Start(Huddle, new ScriptedDice(2, 1, 5, 5, 5, 1, 1), heroes: 2, cards: ["t_scroll", "t_scroll"]);
        Hero hero = session.HeroInSeat();
        Hero ally = session.HeroInSeat(1);
        Assert.False(session.State.FriendlyFire);
        CardInstance scrolls = hero.Card("t_scroll");
        Assert.Equal(2, scrolls.Count);
        Monster near = session.RoomById().Monsters.Single(monster => monster.Position.ChebyshevDistanceTo(hero.Position) == 1);

        IReadOnlyList<GameEvent> events = session.Do(new LearnSpell(hero.Id, scrolls.Id));

        Assert.Equal(new[] { hero.Id, ally.Id, near.Id }.Order(), events.SingleOf<Backfired>().TargetIds.Order());
        Assert.Equal(TestRun.HeroStats.Hearts - 1, hero.Hearts);
        Assert.Equal(TestRun.HeroStats.Hearts - 1, ally.Hearts);
        Assert.Equal(2, near.Hearts);
        Assert.Empty(hero.Spells);
        Assert.Equal(1, scrolls.Count);
    }

    [Fact]
    public void OnlyAScrollWithASpellOnItCanBeLearned()
    {
        GameSession session = TestRun.Start(Huddle, cards: ["shield"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("no spell to learn", session.Refuse(new LearnSpell(hero.Id, hero.Card("shield").Id)));
        Assert.Equal(9, GameSession.LearnDifficulty(TestContent.Catalog.GetSpell("flame_blast")));
    }

    [Fact]
    public void OnTheWoarldMapTheScrollGoesOffAmongTheParty()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character learner = Character.Create("Morgana", "moss", StatAllocation.None, content, DateTimeOffset.UnixEpoch);
        Character friend = Character.Create("Wren", "moss", StatAllocation.None, content, DateTimeOffset.UnixEpoch);
        learner.GiveCard("flame_scroll", content);

        // Every die a 1: the roll (1) can't learn it, and the backfire's three 1s (3) beat both their
        // defenses (a 1 and no armor), wounding them both.
        ScrollStudy.Result result = ScrollStudy.Attempt(learner, "flame_scroll", [learner, friend], content, new Ones());

        Assert.False(result.Learned);
        Assert.Equal((1, 9, 3), (result.Total, result.Difficulty, result.Backfire));
        Assert.Equal([learner, friend], result.Wounded);
        Assert.DoesNotContain("flame_scroll", learner.Backpack.Concat(learner.Belt));
        Assert.NotNull(ScrollStudy.Check(learner, "flame_scroll", content));
    }

    [Fact]
    public void OnTheWoarldMapAGoodRollLearnsTheSpellForGood()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character learner = Character.Create("Morgana", "moss", new StatAllocation { OdHandDice = 2 }, content, DateTimeOffset.UnixEpoch);
        learner.GiveCard("frost_scroll", content);

        ScrollStudy.Result result = ScrollStudy.Attempt(learner, "frost_scroll", [learner], content, new Fives());

        Assert.True(result.Learned);
        Assert.Equal(["frost_blast"], learner.Spells);
        Assert.Equal(["frost_blast"], learner.ToHeroSetup().Spells);
    }

    /// <summary>Every die comes up 1.</summary>
    private sealed class Ones : IRandomSource
    {
        public uint NextUInt() => 0;

        public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
    }

    /// <summary>Every die comes up 5.</summary>
    private sealed class Fives : IRandomSource
    {
        public uint NextUInt() => 4;

        public int NextInt(int minInclusive, int maxExclusive) => Math.Min(maxExclusive - 1, minInclusive + 4);
    }
}
