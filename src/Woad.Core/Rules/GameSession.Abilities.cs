using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Monster abilities beyond plain fighting: the Mimic waiting in a chest and grabbing, the Ogre's
/// knockback, the Hexer's hex, the Necromancer raising the dead and the Gelatinous Cube engulfing loot.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// The monster holding the hero, or null. A grab holds only while its monster is right beside them
    /// and able: stunned, frozen, knocked flat or collapsed, it lets go (and being hurt shakes it loose).
    /// </summary>
    public Monster? HeldBy(Hero hero)
    {
        if (hero.GrabbedBy is not int id || State.RoomOf(hero).FindMonster(id) is not { } holder)
        {
            return null;
        }

        bool holds = holder.Position.ManhattanDistanceTo(hero.Position) == 1
                     && !holder.Stun.IsStunned && holder.FrozenTurns == 0 && !holder.Prone && !holder.Collapsed;
        return holds ? holder : null;
    }

    private string? CheckHeld(Hero hero) =>
        hero.Shackled is int lockDifficulty ? $"You're in shackles: use a key, or pick the lock with Od Hand ({lockDifficulty}+). An ally beside you can too."
        : HeldBy(hero) is { } holder ? $"The {holder.Definition.Name} has hold of you: hurt it, shove it or stun it to break free." : null;

    /// <summary>Being hurt makes a monster let go of whoever it holds.</summary>
    private void LetGo(Monster monster)
    {
        foreach (Hero hero in State.Heroes.Where(hero => hero.GrabbedBy == monster.Id))
        {
            hero.GrabbedBy = null;
        }
    }

    /// <summary>What a monster's hit does besides the heart: a grab or a hex (knockback comes after, see <see cref="KnockBack"/>).</summary>
    private void AfterMonsterHit(Room room, Monster monster, Hero hero, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        if (definition.Grabs && hero.GrabbedBy != monster.Id)
        {
            hero.GrabbedBy = monster.Id;
            events.Add(new Grabbed(monster.Id, hero.Id));
        }

        if (definition.Hexes)
        {
            Hex(monster, hero, events);
        }

        if (definition.CarriesOff && hero.IsActive && hero.Carrier is null)
        {
            CarryOff(room, monster, hero, events);
        }
    }

    /// <summary>How far a monster that carries heroes off takes them toward open air.</summary>
    public const int CarryOffReach = 6;

    /// <summary>
    /// Seized and carried off: the hero is taken up to <see cref="CarryOffReach"/> tiles toward the nearest
    /// open air (a pit), over tiles nobody stands on, and dropped in if they get there; short of it, they're
    /// set down where the flight ended. With no open air in the room, nothing happens.
    /// </summary>
    internal void CarryOff(Room room, Monster monster, Hero hero, List<GameEvent> events)
    {
        bool Open(GridPosition tile) => room.Layout.Contains(tile)
            && (room.Layout[tile] == TileKind.Pit || (room.Layout[tile].IsWalkable(flying: true) && !IsOccupied(room, tile)));
        PathSearch flight = PathSearch.Run(hero.Position, Open, canPass: tile => room.Layout[tile] != TileKind.Pit);
        GridPosition? air = flight.Distances.Keys.Where(tile => room.Layout[tile] == TileKind.Pit)
            .OrderBy(tile => flight.Distances[tile]).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile).FirstOrDefault();
        if (air is not { } edge)
        {
            return;
        }

        List<GridPosition> path = [.. flight.PathTo(edge).Take(CarryOffReach)];
        if (path.Count == 0)
        {
            return;
        }

        events.Add(new CarriedOff(monster.Id, hero.Id, path));
        GridPosition end = path[^1];
        if (room.Layout[end] == TileKind.Pit)
        {
            hero.Position = path.Count > 1 ? path[^2] : hero.Position;
            PushHero(null, room, hero, end, events, announce: false);
            return;
        }

        // Short of the edge: dropped on the last open stone it reached.
        GridPosition landing = path.LastOrDefault(tile => room.Layout[tile].IsWalkable() && !IsOccupied(room, tile), hero.Position);
        hero.Position = landing;
    }

    /// <summary>A hex steals a point of Luck (their Luck first, then Clover Luck), the Hexer keeping it; with none left, it stuns.</summary>
    private static void Hex(Monster monster, Hero hero, List<GameEvent> events)
    {
        if (hero.LuckLeft > 0)
        {
            hero.SpendLuck();
            monster.Luck++;
            events.Add(new Hexed(monster.Id, hero.Id, Drained: true, hero.LuckLeft));
            return;
        }

        events.Add(new Hexed(monster.Id, hero.Id, Drained: false, 0));
        Stun(hero, powerful: false, 1, events);
    }

    /// <summary>
    /// Knocks the hero up to <paramref name="tiles"/> tiles straight away from the monster. Something
    /// solid in the way stops them; a pit or a trap takes them where it lies.
    /// </summary>
    private void KnockBack(Room room, Monster monster, Hero hero, int tiles, List<GameEvent> events)
    {
        GridPosition from = hero.Position;
        GridPosition near = monster.NearestTile(from);
        int dx = Math.Sign(from.X - near.X), dy = Math.Sign(from.Y - near.Y);
        GridPosition to = from;
        for (int moved = 0; moved < tiles && (dx, dy) != (0, 0); moved++)
        {
            var next = new GridPosition(to.X + dx, to.Y + dy);
            if (!room.Layout.Contains(next) || IsOccupied(room, next)
                || !(room.Layout[next].IsWalkable() || room.Layout[next] is TileKind.DeepWater or TileKind.Pit))
            {
                break;
            }

            to = next;
            if (room.Layout[next] == TileKind.Pit || room.TrapAt(next) is not null)
            {
                break;
            }
        }

        if (to == from)
        {
            return;
        }

        events.Add(new KnockedBack(monster.Id, hero.Id, from, to));
        PushHero(null, room, hero, to, events, announce: false);
    }

    /// <summary>
    /// A mimic wakes where its chest stood. Woken by a hero trying the chest, it bites them at once;
    /// found out by a search, it only wakes.
    /// </summary>
    private void WakeMimic(Room room, GridPosition chest, Hero? waker, List<GameEvent> events)
    {
        MonsterDefinition definition = Content.GetMonster(Content.GetTheme(State.ThemeId).Mimics!.Monster);
        room.WakeMimic(chest);
        Monster mimic = CreateMonster(State.NextId(), definition, chest, PartyStrength());
        room.AddMonster(mimic);
        events.Add(new MonsterSpawned(mimic.Id, definition.Id, room.Id, chest, mimic.Hearts) { Cause = SpawnCause.Mimic });
        events.Add(new MimicWoke(mimic.Id, room.Id, chest, waker?.Id ?? 0));
        if (waker is not { IsActive: true })
        {
            return;
        }

        mimic.AttackDiceThisTurn = definition.AttackDice;
        _actingMonsterId = mimic.Id;
        try
        {
            MonsterAttacks(room, mimic, waker, events);
        }
        finally
        {
            _actingMonsterId = null;
        }
    }

    /// <summary>One of the dead climbs out of an opened grave, beside it, and strikes at whoever opened it.</summary>
    private void RiseFromGrave(Room room, GridPosition grave, string monsterId, Hero opener, List<GameEvent> events)
    {
        room.RemoveRiser(grave);
        MonsterDefinition definition = Content.GetMonster(monsterId);
        Monster risen = CreateMonster(State.NextId(), definition, FreeTileNear(State, room, grave), PartyStrength());
        room.AddMonster(risen);
        events.Add(new MonsterSpawned(risen.Id, definition.Id, room.Id, risen.Position, risen.Hearts) { Cause = SpawnCause.Risen });
        events.Add(new RoseFromGrave(risen.Id, room.Id, grave, opener.Id));
        if (!opener.IsActive || risen.NearestTile(opener.Position).ChebyshevDistanceTo(opener.Position) > 1)
        {
            return;
        }

        risen.AttackDiceThisTurn = definition.AttackDice;
        _actingMonsterId = risen.Id;
        try
        {
            MonsterAttacks(room, risen, opener, events);
        }
        finally
        {
            _actingMonsterId = null;
        }
    }

    private int PartyStrength() => GameRules.PartyStrength(State.Heroes.Count(hero => !hero.IsCompanion), State.Heroes.Count(hero => hero.IsCompanion));

    /// <summary>Every other turn (its first among them), a raiser brings one more of the dead up beside it, up to its limits.</summary>
    private void RaiseDead(Room room, Monster raiser, List<GameEvent> events)
    {
        MonsterDefinition definition = raiser.Definition;
        if (definition.Raises is not { } raisedId || raiser.TurnsTaken % 2 == 0 || raiser.Raised >= definition.RaiseTotal
            || room.Monsters.Count(monster => monster.RaisedBy == raiser.Id) >= definition.RaiseMax)
        {
            return;
        }

        MonsterDefinition raised = Content.GetMonster(raisedId);
        bool CanRise(GridPosition tile) => room.Layout.Contains(tile) && room.Layout[tile].IsWalkable(raised.Flying) && !IsOccupied(room, tile) && room.TrapAt(tile) is null;

        // From a grave nearby if there is one, otherwise from the floor beside it.
        GridPosition? spot = room.Tombstones
            .Select(stone => stone.Position)
            .Where(tile => tile.ChebyshevDistanceTo(raiser.Position) <= 3 && CanRise(tile))
            .OrderBy(tile => tile.ChebyshevDistanceTo(raiser.Position))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault()
            ?? raiser.Position.SurroundingTiles().Where(CanRise).Select(tile => (GridPosition?)tile).FirstOrDefault();
        if (spot is not { } tile)
        {
            return;
        }

        Monster risen = CreateMonster(State.NextId(), raised, tile, PartyStrength());
        risen.RaisedBy = raiser.Id;
        raiser.Raised++;
        room.AddMonster(risen);
        events.Add(new MonsterSpawned(risen.Id, raised.Id, room.Id, tile, risen.Hearts) { Cause = SpawnCause.Raised });
        events.Add(new MonsterRaised(raiser.Id, risen.Id));
    }

    /// <summary>When a raiser falls, all it raised fall to dust (dropping anything they carried).</summary>
    private void CrumbleRaised(Room room, Monster raiser, List<GameEvent> events)
    {
        foreach (Monster risen in room.Monsters.Where(monster => monster.RaisedBy == raiser.Id).ToList())
        {
            room.RemoveMonster(risen);
            LetGo(risen);
            events.Add(new Crumbled(risen.Id));
            DropStolen(room, risen, SafeTile(room, risen.Position), events);
        }
    }

    /// <summary>An engulfing monster swallows the cards and gold on each tile it crawled over.</summary>
    private static void Engulf(Room room, Monster monster, IReadOnlyList<GridPosition> path, List<GameEvent> events)
    {
        foreach (GridPosition tile in path)
        {
            IReadOnlyList<CardInstance> cards = room.TakeLoot(tile, int.MaxValue);
            int gold = room.TakeCoins(tile);
            if (cards.Count == 0 && gold == 0)
            {
                continue;
            }

            monster.StolenCards.AddRange(cards);
            monster.StolenCoins += gold;
            events.Add(new Engulfed(monster.Id, tile, cards.Select(card => card.Id).ToList(), gold));
        }
    }
}
