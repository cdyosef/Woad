using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Core.Characters;

/// <summary>Where a character keeps a card between runs.</summary>
public enum CardPlace
{
    /// <summary>Worn: in its slot, or a mod on the belt or buckle.</summary>
    Worn,

    Belt,

    Pack,

    /// <summary>In their arms (the Relic, and other carry-only things).</summary>
    Carried,
}

/// <summary>Managing what a character carries out of a dungeon: wearing, belting and packing their cards.</summary>
public sealed partial class Character
{
    /// <summary>The cards in one place, by key.</summary>
    public IReadOnlyList<string> In(CardPlace place) => List(place);

    /// <summary>The belt slots the worn belt has (0 when beltless).</summary>
    public int BeltCapacity(ContentCatalog content) =>
        _equipped.Select(key => content.GetCard(key)).FirstOrDefault(card => card.Slot == EquipSlot.Belt)?.BeltSlots ?? 0;

    /// <summary>The belt slots in use: a stack of one card takes one slot (bags hang below it, with their own places).</summary>
    public int BeltUsed(ContentCatalog content) =>
        _belt.Count(key => !content.GetCard(key).IsStackable) + _belt.Where(key => content.GetCard(key).IsStackable).Select(CardKey.Id).Distinct().Count();

    /// <summary>The slot each worn card fills, in the order they were put on (a card for either hand takes its other when its own is filled).</summary>
    public IReadOnlyDictionary<EquipSlot, string> WornSlots(ContentCatalog content)
    {
        var slots = new Dictionary<EquipSlot, string>();
        foreach (string key in _equipped)
        {
            foreach (EquipSlot slot in SlotsToFill(content.GetCard(key), slots))
            {
                slots[slot] = key;
            }
        }

        return slots;
    }

    /// <summary>Why the card (by key, in <paramref name="from"/>) can't be moved to <paramref name="to"/>, or null if it can.</summary>
    public string? CheckMove(string key, CardPlace from, CardPlace to, ContentCatalog content)
    {
        if (!List(from).Contains(key))
        {
            return $"{Name} hasn't got that there.";
        }

        CardDefinition card = content.GetCard(key);
        if (from == to)
        {
            return null;
        }

        switch (to)
        {
            case CardPlace.Worn when CardKey.Unpack(key) is not null:
                return null;
            case CardPlace.Worn when card.ModFor is { } on:
                Dictionary<EquipSlot, string> worn = new(WornSlots(content));
                if (!worn.ContainsKey(on))
                {
                    return on == EquipSlot.Belt ? $"The {card.Name} goes on a belt, and {Name} is beltless." : $"The {card.Name} goes on a buckle, and {Name} has none.";
                }

                return !card.Repeats && _equipped.Any(other => CardKey.Id(other) == card.Id) ? $"{Name}'s {(on == EquipSlot.Belt ? "belt" : "buckle")} has a {card.Name} already." : null;
            case CardPlace.Worn:
                return card.Slot is null ? $"The {card.Name} can't be worn." : null;
            case CardPlace.Belt:
                if (CardKey.Unpack(key) is not null || !Hero.CanBelt(card))
                {
                    return $"The {card.Name} doesn't go on a belt.";
                }

                if (BeltCapacity(content) == 0 || (from == CardPlace.Worn && card.Slot == EquipSlot.Belt))
                {
                    return $"{Name} has no belt on.";
                }

                bool joins = card.IsStackable && _belt.Any(other => CardKey.Id(other) == card.Id);
                return !joins && BeltUsed(content) >= BeltCapacity(content) ? $"{Name}'s belt is full." : null;
            case CardPlace.Carried:
                return card.CarryOnly ? null : $"The {card.Name} goes in the pack.";
            default:
                return card.CarryOnly ? $"The {card.Name} is carried in the arms." : null;
        }
    }

    /// <summary>
    /// Moves a card (by key) from one place to another: worn (pushing out whatever fills its slot), on the
    /// belt or in the pack. Taking the belt off packs it whole, with its buckle, mods and cards; wearing a
    /// belt packed whole buckles it all back on. Returns why not, or null when done.
    /// </summary>
    public string? Move(string key, CardPlace from, CardPlace to, ContentCatalog content)
    {
        if (CheckMove(key, from, to, content) is { } error)
        {
            return error;
        }

        if (from == to)
        {
            return null;
        }

        string moved = TakeFrom(from, key, content)!;
        PutIn(to, moved, content);
        return null;
    }

    /// <summary>
    /// Takes a card (by key) out of a place, ready to go elsewhere: a worn belt comes off packed whole, and
    /// a buckle's mods come off with it into the pack. Returns the card's key as it comes off, or null.
    /// </summary>
    public string? TakeFrom(CardPlace place, string key, ContentCatalog content)
    {
        if (!List(place).Remove(key))
        {
            return null;
        }

        if (place != CardPlace.Worn)
        {
            return key;
        }

        CardDefinition card = content.GetCard(key);
        if (card.Slot == EquipSlot.Belt)
        {
            List<string> attached = [.. _equipped.Where(other => content.GetCard(other) is var worn && (worn.Slot == EquipSlot.Buckle || worn.ModFor is not null))];
            foreach (string other in attached)
            {
                _equipped.Remove(other);
            }

            string packed = CardKey.Packed(key, attached, _belt);
            _belt.Clear();
            return packed;
        }

        if (card.Slot == EquipSlot.Buckle)
        {
            foreach (string mod in _equipped.Where(other => content.GetCard(other).ModFor == EquipSlot.Buckle).ToList())
            {
                _equipped.Remove(mod);
                _backpack.Add(mod);
            }
        }

        return key;
    }

    /// <summary>Puts a card (by key, already taken from wherever it was) in a place; see <see cref="CheckMove"/>.</summary>
    private void PutIn(CardPlace place, string key, ContentCatalog content)
    {
        if (place != CardPlace.Worn)
        {
            List(place).Add(key);
            return;
        }

        CardDefinition card = content.GetCard(key);
        if (card.ModFor is not null)
        {
            _equipped.Add(key);
            return;
        }

        // Whatever fills the slots it needs comes off first, into the pack (a belt packed whole).
        Dictionary<EquipSlot, string> worn = new(WornSlots(content));
        foreach (string displaced in SlotsToFill(card, worn).Select(slot => worn.GetValueOrDefault(slot)).OfType<string>().Distinct().ToList())
        {
            _backpack.Add(TakeFrom(CardPlace.Worn, displaced, content)!);
        }

        if (CardKey.Unpack(key) is { } packed)
        {
            // The belt worn before came off whole, buckle and all: this one goes on with its own.
            _equipped.Add(CardKey.Bare(key));
            _equipped.AddRange(packed.Attached);
            _belt.AddRange(packed.Held);
            return;
        }

        _equipped.Add(key);
    }

    /// <summary>The slots a card would fill, given those already filled: its own, or its other when its own is taken and the other free.</summary>
    private static IReadOnlyList<EquipSlot> SlotsToFill(CardDefinition card, IReadOnlyDictionary<EquipSlot, string> filled) =>
        card.AltSlot is { } alt && card.Slot is { } own && !card.TwoHanded && filled.ContainsKey(own) && !filled.ContainsKey(alt) ? [alt]
        : Hero.SlotsFor(card);

    private List<string> List(CardPlace place) => place switch
    {
        CardPlace.Worn => _equipped,
        CardPlace.Belt => _belt,
        CardPlace.Carried => _carried,
        _ => _backpack,
    };
}
