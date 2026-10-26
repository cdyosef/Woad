using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Torches: a fire die on Rumble attacks, a die on searches, burned out by a 1, one to a hand.</summary>
public class TorchTests
{
    /// <summary>The hero at (4,4) with a dummy (no defense dice, 3 hearts) right beside them at (5,4).</summary>
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
    public void ATorchsSixRollsAnotherFireDieAndSetsWhatItHitsAlight()
    {
        // Rumble 3, the torch's 6, and the fire die that brings (2).
        GameSession session = TestRun.Start(Beside, new ScriptedDice(3, 6, 2), cards: ["t_torch"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal([3, 6, 2], attack.Roll.Faces);
        Assert.Equal(1, attack.TorchDice);
        Assert.Equal(1, dummy.FireDice);
        Assert.Equal(1, hero.TorchesHeld);
    }

    [Fact]
    public void AOneInATorchLitAttackBurnsTheTorchOut()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(1, 3), cards: ["t_torch"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Single(events.OfType<TorchBurnedOut>());
        Assert.Equal(0, hero.TorchesHeld);
    }

    [Fact]
    public void AHeldTorchAddsADieToSearches()
    {
        GameSession session = TestRun.Start(Beside, new ScriptedDice(2, 3), cards: ["t_torch"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new Search(hero.Id));

        Assert.Equal(5, events.SingleOf<OdHandRolled>().Total);
    }

    [Fact]
    public void TorchesGoOneToAHandAndTheRestStack()
    {
        GameSession session = TestRun.Start(Beside, cards: ["sword", "t_torch", "t_torch", "t_torch"]);
        Hero hero = session.HeroInSeat();
        CardInstance stack = hero.Backpack.Single(card => card.Definition.Torch);
        Assert.Equal((1, 2), (hero.TorchesHeld, stack.Count));

        session.Do(new EquipCard(hero.Id, stack.Id, EquipSlot.MainHand));

        Assert.Equal(2, hero.TorchesHeld);
        Assert.Equal(1, hero.Backpack.Single(card => card.Definition.Torch).Count);
    }
}
