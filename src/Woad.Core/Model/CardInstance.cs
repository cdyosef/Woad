using Woad.Core.Content;

namespace Woad.Core.Model;

/// <summary>
/// One physical card in a run, or a stack of copies of a stackable card (see
/// <see cref="CardDefinition.IsStackable"/>): one card with a <see cref="Count"/>.
/// </summary>
public sealed class CardInstance
{
    internal CardInstance(int id, CardDefinition definition, int? broughtInByHeroId, int count = 1)
    {
        Id = id;
        Definition = definition;
        ChargesRemaining = definition.Charges;
        RoundsLeft = definition.BurnsFor;
        BroughtInByHeroId = broughtInByHeroId;
        Count = count;
        BroughtInCount = broughtInByHeroId is null ? 0 : count;
    }

    /// <summary>How many copies this is: 1, or more for a stack.</summary>
    public int Count { get; internal set; }

    /// <summary>How many of the stack <see cref="BroughtInByHeroId"/> brought into the run; the rest were found.</summary>
    public int BroughtInCount { get; internal set; }

    /// <summary>The copies found in the dungeon, not brought in by <paramref name="heroId"/>.</summary>
    public int FoundBy(int heroId) => Count - (BroughtInByHeroId == heroId ? BroughtInCount : 0);

    public int Id { get; }

    public CardDefinition Definition { get; }

    public int ChargesRemaining { get; internal set; }

    /// <summary>Rounds left before it burns out (see <see cref="CardDefinition.BurnsFor"/>).</summary>
    public int RoundsLeft { get; internal set; }

    /// <summary>Wear it has taken (acid eats armor): off its armor, or a weapon's value, until mended.</summary>
    public int Wear { get; internal set; }

    /// <summary>What it adds while equipped, less its wear.</summary>
    public Stats Modifiers => CardKey.Modifiers(Definition, Wear);

    /// <summary>How a character keeps it between runs: its id and its wear (and a packed belt, what it carries).</summary>
    public string Key => Packed is { } packed
        ? CardKey.Packed(CardKey.Of(Definition.Id, Wear), packed.Attached.SelectMany(Copies), packed.Held.SelectMany(Copies))
        : _contents.Count > 0 ? CardKey.Bagged(CardKey.Of(Definition.Id, Wear, Painted), _contents.SelectMany(Copies))
        : CardKey.Of(Definition.Id, Wear, Painted);

    private readonly List<CardInstance> _contents = [];

    /// <summary>What's in it, for a bag (see <see cref="CardDefinition.Bag"/>): a stack a slot.</summary>
    public IReadOnlyList<CardInstance> Contents => _contents;

    internal List<CardInstance> ContentsList => _contents;

    /// <summary>A shield with its owner's crest painted on it (Shield Paint): heroes beside its bearer defend at +1.</summary>
    public bool Painted { get; internal set; }

    private static IEnumerable<string> Copies(CardInstance card) => Enumerable.Repeat(card.Key, card.Count);

    /// <summary>A belt unbuckled whole: its buckle and mods, and the cards on it, kept on it till it's worn again.</summary>
    public PackedBelt? Packed { get; internal set; }

    /// <summary>
    /// A card from a character's key, with its wear, and for a packed belt what it carries; copies of
    /// a stackable card carried gather into one. <paramref name="nextId"/> hands out the ids.
    /// </summary>
    public static CardInstance FromKey(string key, ContentCatalog content, Func<int> nextId, int? broughtInByHeroId)
    {
        var card = new CardInstance(nextId(), content.GetCard(key), broughtInByHeroId) { Wear = CardKey.Wear(key), Painted = CardKey.Painted(key) };
        if (CardKey.Unpack(key) is var (attached, held))
        {
            List<CardInstance> Make(IEnumerable<string> keys)
            {
                var made = new List<CardInstance>();
                foreach (string inner in keys)
                {
                    CardInstance one = FromKey(inner, content, nextId, broughtInByHeroId);
                    if (one.Definition.IsStackable && made.FirstOrDefault(other => other.Definition == one.Definition) is { } stack)
                    {
                        stack.Absorb(one);
                    }
                    else
                    {
                        made.Add(one);
                    }
                }

                return made;
            }

            if (card.Definition.Hangs is not null)
            {
                card._contents.AddRange(Make(held));
            }
            else
            {
                card.Packed = new PackedBelt(Make(attached), Make(held));
            }
        }

        return card;
    }

    /// <summary>A crossbow that's been shot and not yet reloaded. Every crossbow is loaded again on reaching a new floor.</summary>
    public bool Unloaded { get; internal set; }

    /// <summary>
    /// The hero who brought this card into the run, or null if it was found in the dungeon.
    /// A downed hero keeps only the cards they brought in.
    /// </summary>
    public int? BroughtInByHeroId { get; private set; }

    private readonly Dictionary<Ailment, int> _soaked = [];

    /// <summary>What a Sineater Doll holds: stacks of each ailment it has soaked up.</summary>
    public IReadOnlyDictionary<Ailment, int> Soaked => _soaked;

    public int SoakedStacks => _soaked.Values.Sum();

    /// <summary>Soaks stacks of an ailment into the doll; null empties it.</summary>
    internal void Soak(Ailment? ailment, int stacks)
    {
        if (ailment is not { } soaked)
        {
            _soaked.Clear();
        }
        else if (stacks > 0)
        {
            _soaked[soaked] = _soaked.GetValueOrDefault(soaked) + stacks;
        }
    }

    /// <summary>Gathers another stack of the same card into this one.</summary>
    internal void Absorb(CardInstance other)
    {
        if (other.BroughtInByHeroId is { } owner && BroughtInByHeroId is null)
        {
            BroughtInByHeroId = owner;
            BroughtInCount = other.BroughtInCount;
        }
        else if (other.BroughtInByHeroId == BroughtInByHeroId)
        {
            BroughtInCount += other.BroughtInCount;
        }

        Count += other.Count;
    }

    /// <summary>Takes <paramref name="count"/> copies off this stack as a new card (found ones first).</summary>
    internal CardInstance Split(int newId, int count)
    {
        int found = Count - BroughtInCount;
        int brought = Math.Max(0, count - found);
        Count -= count;
        BroughtInCount -= brought;
        return new CardInstance(newId, Definition, brought > 0 ? BroughtInByHeroId : null, count) { BroughtInCount = brought };
    }

    public override string ToString() => Count > 1 ? $"{Definition.Name} x{Count} #{Id}" : $"{Definition.Name} #{Id}";
}

/// <summary>What a belt unbuckled whole carries: its buckle and mods (<see cref="Attached"/>), and its cards (<see cref="Held"/>).</summary>
public sealed record PackedBelt(IReadOnlyList<CardInstance> Attached, IReadOnlyList<CardInstance> Held);
