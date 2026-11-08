using Godot;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;
using static Woad.Scenes.Debug.UiDriver;

namespace Woad.Scenes.Game;

/// <summary>
/// Debug mode (<c>-- --offhandtest</c>, with <see cref="DemoRun.OffHand"/>): Aria's off-hand Dagger stays out of
/// her Rumble attack unless she picks it (clicking it moves it to the Rumble square); after the Rumble attack, the
/// Od Hand card follows up with it, by a double-click on the ogre and one more click.
/// </summary>
public partial class GameScreen
{
    private async void RunOffHandTest()
    {
        try
        {
            await WaitUntilIdle();
            Hero aria = ActiveHero!;
            CardInstance sword = aria.MainWeapon!;
            CardInstance dagger = GameSession.OffHandItem(aria)!;
            Monster slime = _session.State.Rooms["yard"].Monsters.Single(); // the ogre
            Vector2 Square(HandView hand, CardInstance card) => hand.GetGlobalRect().Position + hand.CardPoint("item:" + card.Id);
            bool InSquare(HandView hand, CardInstance card) => StackOf(hand, aria).Any(stacked => stacked.CardId == card.Id);

            Check(InSquare(_odHand, dagger) && !InSquare(_rumbleHand, dagger), "the Dagger starts in the Od Hand square");

            // Sticky Gloves worn stay out of the Od Hand square and its rolls: they're played on their own (see --toolstest).
            CardInstance gloves = aria.EquippedCards.Single(card => card.Definition.Id == "sticky_gloves");
            Check(!InSquare(_odHand, gloves) && _session.ToolsBonusLeft == 0, "worn Sticky Gloves stay out of the Od Hand square and its rolls");
            Check(_session.WeaponAgainst(slime, ReadiedWeaponId)?.Weapon == sword, "left alone, Rumble swings the sword");

            // Clicking the Dagger picks it for Rumble: it moves to the Rumble square, the Od Hand card not lit with it.
            await ClickAt(this, Square(_odHand, dagger));
            await WaitUntilIdle();
            Check(ReadiedWeapon == dagger && InSquare(_rumbleHand, dagger) && !InSquare(_odHand, dagger),
                $"clicking the Dagger picks it for Rumble, moving it to the Rumble square ({ReadiedWeapon?.Definition.Name})");
            Check(_session.WeaponAgainst(slime, ReadiedWeaponId)?.Weapon == dagger, "Rumble would now swing the Dagger");
            await SaveShot("offhand_picked");

            // Clicking the sword goes back to it, and the Dagger back to the Od Hand square.
            await ClickAt(this, Square(_rumbleHand, sword));
            await WaitUntilIdle();
            Check(ReadiedWeapon == sword && InSquare(_odHand, dagger) && !InSquare(_rumbleHand, dagger), "clicking the sword picks it again; the Dagger goes back");

            // Double-clicking the Dagger picks it and aims Rumble with it; Rumble's targets are what it reaches.
            await DoubleClickAt(this, Square(_odHand, dagger));
            await WaitUntilIdle();
            Check(ReadiedWeapon == dagger && _rumbling && !_offHanding, $"double-clicking the Dagger aims Rumble with it ({ReadiedWeapon?.Definition.Name}, {_rumbling})");
            await ClickAt(this, Square(_rumbleHand, sword));
            await WaitUntilIdle();
            Check(ReadiedWeapon == sword && InSquare(_odHand, dagger), "and clicking the sword goes back to it");

            // Double-click the slime and click it: the sword's Rumble attack. The Dagger stays in hand.
            await DoubleClickAt(this, TileCenter(slime.Position));
            await ClickTile(slime.Position);
            await ConfirmIfHeld();
            await WaitUntilIdle();
            Check(_session.AttackDiceLeft == 0 && GameSession.OffHandItem(aria) == dagger, "the Rumble attack is the sword's; the Dagger is still in her off hand");
            Check(InSquare(_odHand, dagger), "with a follow-up to make, the Dagger is in the Od Hand square");

            Check(_session.State.Rooms["yard"].FindMonster(slime.Id) is not null, "the ogre takes the sword and stands");
            await DoubleClickAt(this, TileCenter(slime.Position));
            Check(_offHanding && !_session.State.Turn!.OffHandAttacked && _session.State.Turn.MovementRoll is null, "double-clicking the ogre aims the Dagger's follow-up (and rolls no Wend)");
            await ClickTile(slime.Position);
            await ConfirmIfHeld();
            await WaitUntilIdle();
            Check(_session.State.Turn!.OffHandAttacked && _session.State.Turn.OdHandDiceUsed == 1, "one more click follows up with the Dagger and an Od Hand die");

            GD.Print("Off hand test passed.");
            Sfx.QuitGame(this);
        }
        catch (Exception exception)
        {
            GD.PushError($"Off hand test failed: {exception.Message}");
            Sfx.QuitGame(this, 1);
        }
    }
}
