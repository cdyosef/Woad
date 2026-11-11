using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tavern;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Clod Hopper, frog hunter, and his gear: the Frog Gig, Bog Waders and Bog Lantern.</summary>
public sealed class ClodHopperTests
{
    private static GameSession Duel(string row, string[] cards, ScriptedDice dice) => TestRun.Start($"""
        .........
        .........
        .........
        .........
        {row}
        .........
        .........
        .........
        .........
        """, dice, heroes: row.Contains('2') ? 2 : 1, cards: cards);

    [Fact]
    public void TheGigSnagsSomethingSmallOutAtItsReachAndDragsItIn()
    {
        GameSession session = Duel("..1.d....", ["t_gig"], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Equal(new Snagged(hero.Id, dummy.Id), events.SingleOf<Snagged>());
        Assert.Equal(new GridPosition(3, 4), dummy.Position);
    }

    [Fact]
    public void BesideYouThereIsNothingToSnag()
    {
        GameSession session = Duel("..1d.....", ["t_gig"], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Empty(events.OfType<Snagged>());
    }

    [Fact]
    public void TheGigStrikesEarthCreaturesHarder()
    {
        GameSession session = Duel("..1ö.....", ["t_gig"], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();

        AttackRolled rolled = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id)).SingleOf<AttackRolled>();

        Assert.Equal(2, rolled.Bonus);
    }

    [Fact]
    public void TheLanternNeverBurnsOut()
    {
        GameSession session = Duel("..1d.....", ["t_lantern"], new ScriptedDice(1, 1, 1));
        Hero hero = session.HeroInSeat();
        CardInstance lantern = hero.Card("t_lantern");
        session.Do(new EquipCard(hero.Id, lantern.Id, EquipSlot.MainHand));

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Empty(events.OfType<TorchBurnedOut>());
        Assert.Equal(EquipSlot.MainHand, hero.SlotOf(lantern));
    }

    [Fact]
    public void ClodSaysHisPieceWhenHurtAndThenKeepsQuietAWhile()
    {
        GameSession session = Duel("..12.....", [], new ScriptedDice(1, 1, 1, 1, 1, 1, 1, 1));
        Hero first = session.HeroInSeat(0), clod = session.HeroInSeat(1);
        clod.Appearance = clod.Appearance with { Persona = GameSession.ClodPersona };
        clod.Hearts = 1;

        HeroSpoke spoke = session.Do(new EndTurn(first.Id)).SingleOf<HeroSpoke>();

        Assert.Equal((clod.Id, "common"), (spoke.HeroId, spoke.Language));
        Assert.Contains(spoke.Text, new[] { "Ribbit... I mean, ow.", "Bit me! The cheek of it.", "Granny's poultice, where'd I put it..." });
        session.Do(new EndTurn(clod.Id));
        Assert.Empty(session.Do(new EndTurn(first.Id)).OfType<HeroSpoke>());
    }

    [Fact]
    public void ClodWaitsAtTheRecruitersElbowWithHisGear()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        RecruitOffer clod = TavernServices.Clod;
        Assert.Equal(("Clod Hopper", "frog_hunter", "frog", GameSession.ClodPersona), (clod.Name, clod.Appearance.Look, clod.Appearance.CrestCharge, clod.Appearance.Persona));
        Assert.All(clod.Gear, id => Assert.NotNull(content.GetCard(id)));
        Assert.Contains(clod.Appearance.Speaks, language => language.Id == "gurgle");

        Character buyer = Character.Create("Wren", "woad", new StatAllocation(), content, DateTimeOffset.UnixEpoch);
        buyer.Earn(Money.Gold(20));
        (ServiceResult result, Character? recruit) = TavernServices.Hire(buyer, clod, content, DateTimeOffset.UnixEpoch);

        Assert.True(result.Done, result.Message);
        Assert.True(recruit!.Policy.PicksUpLoot);
        Assert.Equal(GameSession.ClodPersona, recruit.Appearance.Persona);
        Assert.Contains("frog_gig", recruit.Equipped.Select(CardKey.Id));
    }
}
