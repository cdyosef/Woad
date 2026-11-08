using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Hold Fire as a card: a flame in a hand, one to a hand, that's hurled, dropped (burning) or handed on, and never put away.</summary>
public class HeldFireTests
{
    private static GameSession Run(ScriptedDice dice, int heroes = 1) => TestRun.Start("""
        .........
        .........
        .........
        .........
        .12.d....
        .........
        .........
        .........
        .........
        """, dice, heroes: heroes, stats: TestRun.HeroStats with { AttackDice = 3, OdHandDice = 1 }, cards: []);

    private static Hero Mage(GameSession session, int power, params string[] spells)
    {
        Hero hero = session.HeroInSeat();
        foreach (string spell in spells)
        {
            hero.Learn(TestContent.Catalog.GetSpell(spell));
        }

        hero.Power.Banked = power;
        return hero;
    }

    [Fact]
    public void HurlFireOnYourselfPutsAFlameInAHandOneToAHand()
    {
        GameSession session = Run(new ScriptedDice(3, 3, 3, 3, 3, 3));
        Hero hero = Mage(session, power: 3, "hurl_fire");

        session.Do(new CastSpell(hero.Id, "hurl_fire", hero.Position));
        Assert.Equal(GameSession.HoldFireId, hero.Equipment[EquipSlot.OffHand].Definition.Id);
        session.State.Turn!.AttackDiceUsed = 0;
        session.Do(new CastSpell(hero.Id, "hurl_fire", hero.Position));
        Assert.Equal(GameSession.HoldFireId, hero.Equipment[EquipSlot.MainHand].Definition.Id);
        Assert.Equal(2, hero.TorchesHeld);

        // A third: both hands hold fire already.
        session.State.Turn!.AttackDiceUsed = 0;
        session.Do(new CastSpell(hero.Id, "hurl_fire", hero.Position));
        Assert.Equal(2, hero.HeldFires.Count());
    }

    [Fact]
    public void AFlameWontBePutAwayButDroppedItSetsTheFloorBurning()
    {
        GameSession session = Run(new ScriptedDice(3, 3));
        Hero hero = Mage(session, power: 1, "hurl_fire");
        session.Do(new CastSpell(hero.Id, "hurl_fire", hero.Position));
        CardInstance flame = hero.HeldFires.Single();

        Assert.Contains("flame in your hand", session.Refuse(new BeltCard(hero.Id, flame.Id)));
        Assert.Contains("flame in your hand", session.Refuse(new StowCard(hero.Id, flame.Id)));
        session.Do(new EquipCard(hero.Id, flame.Id, EquipSlot.MainHand));
        Assert.Equal(EquipSlot.MainHand, hero.SlotOf(flame));

        session.Do(new DropCard(hero.Id, flame.Id));
        Assert.False(hero.Flame);
        Assert.True(session.RoomById().Flames.ContainsKey(hero.Position));
        Assert.Empty(session.RoomById().LootAt(hero.Position));
    }

    [Fact]
    public void HandedToAnAllyItBurnsInTheirArmsAndFallsIfTheyDontTakeItUp()
    {
        GameSession session = Run(new ScriptedDice(3, 3, 3, 3, 3, 3), heroes: 2);
        Hero caster = Mage(session, power: 1, "hurl_fire");
        Hero ally = session.HeroInSeat(1);
        session.Do(new CastSpell(caster.Id, "hurl_fire", caster.Position));

        session.Do(new GiveCard(caster.Id, ally.Id, caster.HeldFires.Single().Id));
        Assert.False(caster.Flame);
        Assert.Single(ally.Carried, card => card.Definition.HeldFire);
        Assert.Equal(1, ally.TorchesHeld);

        session.Do(new EndTurn(caster.Id));
        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(ally.Id));
        Assert.False(ally.Flame);
        Assert.Contains(events, gameEvent => gameEvent is CardDropped);
        Assert.True(session.RoomById().Flames.ContainsKey(ally.Position));
    }

    [Fact]
    public void HurlFireOneAddsAFireToTheHurledFlame()
    {
        // The Od Hand die's 3 and the Fire die's 2, plus Hurl Fire 1's +1: 6.
        GameSession session = Run(new ScriptedDice(3, 3, 2));
        Hero hero = Mage(session, power: 1, "hurl_fire", "hurl_fire_1");
        session.Do(new CastSpell(hero.Id, "hurl_fire", hero.Position));
        Assert.Contains("isn't cast", session.Refuse(new CastSpell(hero.Id, "hurl_fire_1", hero.Position)));

        IReadOnlyList<GameEvent> events = session.Do(new HurlHeldFire(hero.Id, session.OnlyMonster().Position, Dice: 1));

        Assert.Equal(6, events.SingleOf<AttackRolled>().Total);
    }

    [Fact]
    public void HurlFireOneIsLearnedOnlyByOneWhoKnowsHurlFire()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Assert.DoesNotContain(Spellbook.Learnable([], content), spell => spell.Id == "hurl_fire_1");
        Assert.Contains(Spellbook.Learnable(["hurl_fire"], content), spell => spell.Id == "hurl_fire_1");
    }
}
