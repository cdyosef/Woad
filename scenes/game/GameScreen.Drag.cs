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
    public override void _EnterTree()
    {
        CardDrag.Dropped += OnCardDropped;
        CardDrag.AbilityDropped += OnAbilityDropped;
    }

    public override void _ExitTree()
    {
        // Leaving mid-question, the game mustn't stay in slow motion.
        Engine.TimeScale = 1;
        CardDrag.Dropped -= OnCardDropped;
        CardDrag.AbilityDropped -= OnAbilityDropped;
    }

    /// <summary>
    /// An ability card let go over its own hand: into the square it's played there, ready to use (the pool's
    /// own card puts the dice card in front); back over the fan, the skill in the square goes back into the hand.
    /// </summary>
    private void OnAbilityDropped(string title, string name, bool intoSquare)
    {
        _spread.Close();
        if (Locked || ActiveHero is not { } hero || Hands.FirstOrDefault(hand => hand.Title == title && hand.Default is not null) is not { } hand
            || hand.Cards.FirstOrDefault(card => card.Name == name) is not { } card)
        {
            return;
        }

        var key = (hero.Id, hand.Title);
        if (intoSquare && card == hand.Default)
        {
            _fronts[key] = HandView.DiceKey;
        }
        else if (intoSquare)
        {
            _playedCards[key] = card.Name;
            _fronts[key] = HandView.SkillKey;
        }
        else if (_playedCards.GetValueOrDefault(key) == name)
        {
            _playedCards.Remove(key);
            _fronts[key] = HandView.DiceKey;
        }

        RefreshPanels();
    }

    private void OnCardDropped(int cardId, DropTarget target, bool all)
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
            case DropKind.Pack when target.BagId is int bagId && hero.BagHolding(card)?.Id != bagId:
                Execute(new PackCard(hero.Id, cardId, bagId));
                break;
            case DropKind.Carry when location != CardLocation.Carried:
                Execute(new CarryCard(hero.Id, cardId));
                break;
            case DropKind.Board:
                // Let go over the map: asked first, then dropped where the hero stands.
                _selectedCardId = cardId;
                RefreshPanels();
                // A quick drag off a stack drops one; held first, the whole stack.
                int count = all ? card.Count : 1;
                string what = count > 1 ? $"the {count} {definition.Name}s" : card.Count > 1 ? $"a {definition.Name}" : $"the {definition.Name}";
                _inventory.AskChoice(cardId, $"Drop {what} here?", [("Drop it", () => ExecuteForActiveHero(heroId => new DropCard(heroId, card.Id, count))), ("Keep it", RefreshPanels)]);
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
