using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Cards dragged and dropped: let go over an equip square, a card goes into that slot; over the belt
/// square, onto the belt (a belt, buckle or mod is put on); over the bag, into it; over the carry tab,
/// into the hero's arms; anywhere else on the sheet, where it's worn (or onto the belt); over the
/// board, it's dropped there, asked first. The rules check each like the card buttons would.
/// </summary>
public partial class GameScreen
{
    public override void _EnterTree() => CardDrag.Dropped += OnCardDropped;

    public override void _ExitTree() => CardDrag.Dropped -= OnCardDropped;

    private void OnCardDropped(int cardId, DropTarget target)
    {
        if (Locked || ActiveHero is not { } hero || hero.FindCard(cardId) is not var (card, location))
        {
            return;
        }

        CardDefinition definition = card.Definition;
        bool worn = definition.Type is CardType.Belt or CardType.Buckle or CardType.Mod;
        switch (target.Kind)
        {
            case DropKind.Equip when target.Slot is { } slot:
                if (hero.SlotOf(card) == slot)
                {
                    return;
                }

                if (definition.Slot != slot && definition.AltSlot != slot && !(definition.TwoHanded && slot == EquipSlot.OffHand))
                {
                    Log($"The {definition.Name} doesn't go there.", Palette.TextDim);
                    return;
                }

                Execute(new EquipCard(hero.Id, cardId, definition.AltSlot is not null ? slot : null));
                break;
            case DropKind.Belt when location != CardLocation.Belt:
                Execute(worn || card.Packed is not null ? new EquipCard(hero.Id, cardId) : new BeltCard(hero.Id, cardId));
                break;
            case DropKind.Bag when location != CardLocation.Backpack:
                Execute(new StowCard(hero.Id, cardId));
                break;
            case DropKind.Carry when location != CardLocation.Carried:
                Execute(new CarryCard(hero.Id, cardId));
                break;
            case DropKind.Board:
                // Let go over the map: asked first, then dropped where the hero stands.
                _selectedCardId = cardId;
                RefreshPanels();
                _inventory.AskChoice(cardId, $"Drop the {definition.Name} here?", [("Drop it", () => Drop(card)), ("Keep it", RefreshPanels)]);
                break;
            case DropKind.Sheet when location != CardLocation.Equipped:
                if (definition.Slot is null && !worn)
                {
                    Execute(new BeltCard(hero.Id, cardId));
                }
                else if (definition.AltSlot is not null && card.Packed is null && !worn)
                {
                    _selectedCardId = cardId;
                    RefreshPanels();
                    AskHand(hero, card);
                }
                else
                {
                    Execute(new EquipCard(hero.Id, cardId));
                }

                break;
        }
    }
}
