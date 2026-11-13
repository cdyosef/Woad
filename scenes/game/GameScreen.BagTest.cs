using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --bagtest</c>, with <see cref="DemoRun.Bags"/>): Aria's belt hangs a Rock Bag, a Medicine Bag, a
/// Sack and a Bomb Bag. Each knot opens its bag over the board, seen from above, and closes it again (the pack's too);
/// a dagger dragged onto the Sack goes in; the Rock Bag, double-clicked, asks USE and the Od Hand puts a Rock in her
/// off hand.
/// </summary>
public partial class GameScreen
{
    private async void RunBagTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            List<CardInstance> bags = [.. aria.Bags];
            Check(bags.Select(bag => bag.Definition.Id).SequenceEqual(["rock_bag", "medicine_bag", "sack", "bomb_bag"]), $"four bags hang under her belt ({string.Join(", ", bags.Select(bag => bag.Definition.Id))})");
            Check(_heroPanel.BagRow.Visible, "they hang on the sheet, under the belt");
            CardInstance medicine = bags[1], sack = bags[2], rockBag = bags[0];
            Check(medicine.Contents.Any(card => card.Definition.Id == "eye_drops") && rockBag.Contents.Single().Definition.Id == "rock",
                $"her cures went into the Medicine Bag and her rocks into the Rock Bag ({string.Join(", ", medicine.Contents.Select(card => card.Key))} / {string.Join(", ", rockBag.Contents.Select(card => card.Key))})");
            Check(aria.Belt.All(card => card.Definition.Id != "eye_drops"), "and none of them take a belt slot");
            await SaveShot("bags_belt");

            // Blind, the Medicine Bag pulls the Eye Drops (not the Antidote) into the Od Hand square; double-clicked, they cure her.
            List<StackCard> od = StackOf(_odHand, aria);
            CardInstance drops = medicine.Contents.Single(card => card.Definition.Id == "eye_drops");
            Check(od.Any(card => card.CardId == drops.Id) && od.All(card => card.CardId is not int id || aria.FindCard(id)?.Card.Definition.Id != "antidote"),
                $"the Medicine Bag pulls the Eye Drops into the Od Hand stack ({string.Join(", ", od.Select(card => card.Name))})");
            UseStackCard(_odHand, od.First(card => card.CardId == drops.Id));
            await WaitUntilIdle();
            await ClickTile(aria.Position);
            await WaitUntilIdle();
            await ConfirmIfHeld();
            Check(!aria.Conditions.Has(Condition.Blind), $"double-clicked, they cure her ({string.Join(" / ", _messages.TakeLast(2))})");

            // A knot opens its bag, seen from above; the knot at the top of it closes it.
            Vector2 row = _heroPanel.BagRow.GetGlobalRect().Position;
            await ClickAt(this, row + _heroPanel.BagRow.KnotPoint(1));
            await WaitUntilIdle();
            Check(_heroPanel.OpenPouchId == medicine.Id && _heroPanel.PouchCard(0).Card == medicine.Contents[0], "clicking the Medicine Bag's knot opens it, its cures inside");
            await SaveShot("bags_open");
            await ClickAt(this, _heroPanel.PouchMouth.GetGlobalRect().Position + _heroPanel.PouchMouth.KnotPoint);
            await WaitUntilIdle();
            Check(_heroPanel.OpenPouchId is null, "its knot on top closes it");
            await ClickAt(this, row + _heroPanel.BagRow.KnotPoint(2));
            await ClickAt(this, row + _heroPanel.BagRow.KnotPoint(2));
            Check(_heroPanel.OpenPouchId is null, "the belt knot closes it too");

            // The pack opens the same way, and its knot closes it.
            await Click(this, _heroPanel.BagDeck);
            await WaitUntilIdle();
            Check(_heroPanel.IsBagOpen, "the pack opens");
            await ClickAt(this, _heroPanel.BagMouth.GetGlobalRect().Position + _heroPanel.BagMouth.KnotPoint);
            Check(!_heroPanel.IsBagOpen, "the pack's knot closes it");

            // A dagger dropped on the Sack goes in.
            CardInstance dagger = aria.AllCards.Single(card => card.Definition.Id == "dagger");
            OnCardDropped(dagger.Id, new DropTarget(DropKind.Pack, BagId: sack.Id), all: true);
            await WaitUntilIdle();
            Check(aria.BagHolding(dagger) == sack, $"a dagger dropped on the Sack is packed into it ({aria.FindCard(dagger.Id)?.Location}: {string.Join(" / ", _messages.TakeLast(2))})");

            // The Rock Bag, double-clicked: USE, the Od Hand or Rumble; the Od Hand draws a Rock into the off hand.
            await DoubleClickAt(this, row + _heroPanel.BagRow.BodyPoint(0));
            await WaitUntilIdle();
            Check(BagUsePopup is not null, "double-clicking the Rock Bag asks USE");
            await SaveShot("bags_use");
            await Click(this, BagUsePopup!.GetNode<Button>("OdHand"));
            await WaitUntilIdle();
            Check(aria.Equipment.GetValueOrDefault(EquipSlot.OffHand)?.Definition.Id == "rock" && BagUsePopup is null,
                $"the Od Hand puts a Rock in her off hand ({aria.Equipment.GetValueOrDefault(EquipSlot.OffHand)?.Definition.Id}: {string.Join(" / ", _messages.TakeLast(2))})");

            // The Carry button on the sheet pulls the carrying window out, empty or not, and puts it back.
            await Click(this, _heroPanel.CarryButton);
            await Pause(0.3);
            Check(_carryTab.IsOut, "the sheet's Carry button pulls the carrying window out");
            await Click(this, _heroPanel.CarryButton);
            await Pause(0.3);
            Check(!_carryTab.IsOut, "and puts it back");

            GD.Print("Bag test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Bag test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
