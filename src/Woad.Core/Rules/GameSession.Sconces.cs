using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Wall sconces: an iron bracket on a room's wall, over the floor tile beside it, that holds a torch. A lit one
/// is a light and a fire, like a lit brazier. From under it (or beside), a hero takes its torch (into a free
/// hand, else the belt or bag), free; or puts a torch from their hand into an empty one.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The torch a sconce holds, as a card.</summary>
    public const string SconceTorchId = "torch";

    /// <summary>Why the active hero can't take the torch from the sconce over <paramref name="tile"/>, or null.</summary>
    public string? CheckTakeTorch(GridPosition tile)
    {
        if (CheckSconce(tile) is { } error)
        {
            return error;
        }

        return ActiveRoom.Sconces[tile] ? Content.FindCard(SconceTorchId) is null ? "There's no torch to take." : null : "The sconce is empty.";
    }

    /// <summary>Why the active hero can't put a torch from their hand into the sconce over <paramref name="tile"/>, or null.</summary>
    public string? CheckPutTorch(GridPosition tile)
    {
        if (CheckSconce(tile) is { } error)
        {
            return error;
        }

        if (ActiveRoom.Sconces[tile])
        {
            return "There's a torch in it already.";
        }

        return HandTorch(ActiveHero) is null ? "You hold no torch to put in it." : null;
    }

    private string? CheckSconce(GridPosition tile)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (!ActiveRoom.Sconces.ContainsKey(tile))
        {
            return "There's no sconce there.";
        }

        return ActiveHero.Position.ChebyshevDistanceTo(tile) <= 1 ? null : "Go and stand under it first.";
    }

    /// <summary>A torch in the hero's hands that could go in a sconce (not a flame of Hold Fire).</summary>
    private static CardInstance? HandTorch(Hero hero) =>
        new[] { EquipSlot.OffHand, EquipSlot.MainHand }.Select(hand => hero.Equipment.GetValueOrDefault(hand)).FirstOrDefault(card => card is { Definition: { Torch: true, HeldFire: false } });

    private string? Handle(TakeTorch command, List<GameEvent> events)
    {
        if (CheckTakeTorch(command.Sconce) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        room.SetSconce(command.Sconce, lit: false);
        var torch = new CardInstance(State.NextId(), Content.GetCard(SconceTorchId), broughtInByHeroId: null);
        if (new[] { EquipSlot.OffHand, EquipSlot.MainHand }.FirstOrDefault(hand => !hero.Equipment.ContainsKey(hand), (EquipSlot)(-1)) is var free && free >= 0
            && !(hero.MainWeapon?.Definition.TwoHanded ?? false))
        {
            hero.Equip(torch, free);
        }
        else
        {
            hero.AddLoot(torch);
        }

        events.Add(new TorchTaken(hero.Id, room.Id, command.Sconce, torch.Id));
        return null;
    }

    private string? Handle(PutTorch command, List<GameEvent> events)
    {
        if (CheckPutTorch(command.Sconce) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance torch = HandTorch(hero)!;
        TakeFromHero(hero, torch, 1);
        ActiveRoom.SetSconce(command.Sconce, lit: true);
        events.Add(new TorchPlaced(hero.Id, ActiveRoom.Id, command.Sconce));
        return null;
    }
}
