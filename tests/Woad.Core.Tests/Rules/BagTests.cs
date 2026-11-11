using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Bags hung under the belt: their own places, as many as the belt's slots, to hand like the belt.</summary>
public sealed class BagTests
{
    private const string Quiet = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    private const string Beside = """
        .........
        .........
        .........
        .........
        ....1d...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void ToolsHungUnderTheBeltWorkFromThere()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(4, 1), cards: ["t_kit"], skills: Skill.None);
        Hero hero = session.HeroInSeat();
        Assert.Single(hero.Bags);

        OdHandRolled rolled = session.Do(new Steal(hero.Id, session.OnlyMonster().Id)).SingleOf<OdHandRolled>();

        Assert.Equal((1, 1, 5), (rolled.ToolDice, rolled.Bonus, rolled.Total));
    }

    [Fact]
    public void AMedicineBagUnderTheBeltHelpsAid()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(), cards: ["t_medsack"]);
        Hero hero = session.HeroInSeat();
        hero.Conditions.Add(Condition.Nekrot, 1);

        Assert.Contains((Ailment.Nekrot, GameRules.NekrotAidDifficulty), session.GetAidCures(hero.Id));
    }

    [Fact]
    public void CuresPickedUpGoIntoTheMedicineBagNotTheBelt()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_medsack"]);
        Hero hero = session.HeroInSeat();
        CardInstance bag = hero.Bags.Single();
        int used = hero.BeltUsed;
        var drops = new CardInstance(session.State.NextId(), session.Content.GetCard("drops"), broughtInByHeroId: null);
        session.RoomById().AddLoot(hero.Position, [drops]);

        session.Do(new PickUpLoot(hero.Id));

        Assert.Same(bag, hero.BagHolding(drops));
        Assert.Equal(CardLocation.Bag, hero.FindCard(drops.Id)!.Value.Location);
        Assert.Equal(used, hero.BeltUsed);
    }

    [Fact]
    public void ASackTakesAnythingPackedIntoItTillItsFull()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_sack", "scroll", "bomb", "potion", "holy", "drops"]);
        Hero hero = session.HeroInSeat();
        CardInstance sack = hero.Bags.Single();
        List<CardInstance> things = [.. new[] { "scroll", "bomb", "potion", "holy", "drops" }.Select(hero.Card)];

        foreach (CardInstance thing in things.Take(4))
        {
            session.Do(new PackCard(hero.Id, thing.Id, sack.Id));
        }

        Assert.Equal(4, sack.Contents.Count);
        Assert.Contains("full", session.Refuse(new PackCard(hero.Id, things[4].Id, sack.Id)));
    }

    [Fact]
    public void ASackDoesntCatchPickupsOnItsOwn()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_sack"]);
        Hero hero = session.HeroInSeat();
        var scroll = new CardInstance(session.State.NextId(), session.Content.GetCard("scroll"), broughtInByHeroId: null);
        session.RoomById().AddLoot(hero.Position, [scroll]);

        session.Do(new PickUpLoot(hero.Id));

        Assert.Empty(hero.Bags.Single().Contents);
        Assert.NotEqual(CardLocation.Bag, hero.FindCard(scroll.Id)!.Value.Location);
    }

    [Fact]
    public void TheBeltHangsOnlyAsManyBagsAsItHasSlots()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_sack", "t_sack", "t_sack", "t_sack", "t_sack"]);
        Hero hero = session.HeroInSeat();

        Assert.Equal(hero.BeltCapacity, hero.Bags.Count());
        CardInstance spare = hero.Backpack.Single(card => card.Definition.Id == "t_sack");
        Assert.Contains("only 4 bags", session.Refuse(new EquipCard(hero.Id, spare.Id)));
    }

    [Fact]
    public void ASackGoesIntoTheBackpackToo()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_sack"]);
        Hero hero = session.HeroInSeat();
        CardInstance sack = hero.Bags.Single();

        session.Do(new StowCard(hero.Id, sack.Id));

        Assert.Contains(sack, hero.Backpack);
        Assert.Empty(hero.Bags);
    }

    [Fact]
    public void NothingComesOutOfTheBackpackIntoABagInAFight()
    {
        GameSession session = TestRun.Start(Beside, cards: ["t_sack", "potion", "drops", "holy", "bomb", "scroll"]);
        Hero hero = session.HeroInSeat();
        CardInstance scroll = hero.Card("scroll");
        Assert.Contains(scroll, hero.Backpack);

        session.Refuse(new PackCard(hero.Id, scroll.Id, hero.Bags.Single().Id));
    }

    [Fact]
    public void WhatsInABagKeepsThroughItsKey()
    {
        GameSession session = TestRun.Start(Quiet, cards: ["t_sack", "scroll", "bomb"]);
        Hero hero = session.HeroInSeat();
        CardInstance sack = hero.Bags.Single();
        session.Do(new PackCard(hero.Id, hero.Card("scroll").Id, sack.Id));
        session.Do(new PackCard(hero.Id, hero.Card("bomb").Id, sack.Id));

        CardInstance again = CardInstance.FromKey(sack.Key, session.Content, session.State.NextId, null);

        Assert.Equal(["scroll", "bomb"], again.Contents.Select(card => card.Definition.Id).Order().Reverse());
    }

    [Fact]
    public void UsingACureFromTheBagIsTheFreeBeltItem()
    {
        GameSession session = TestRun.Start(Beside, cards: ["t_medsack"]);
        Hero hero = session.HeroInSeat();
        var potion = new CardInstance(session.State.NextId(), session.Content.GetCard("potion"), broughtInByHeroId: null);
        hero.PackInto(hero.Bags.Single(), potion);
        hero.Hearts = 1;

        session.Do(new UseCard(hero.Id, potion.Id));

        Assert.True(session.State.Turn!.UsedBeltItem);
        Assert.Null(hero.FindCard(potion.Id));
    }
}
