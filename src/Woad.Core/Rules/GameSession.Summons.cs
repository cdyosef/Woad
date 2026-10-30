using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// What Necro and Life dice bring into the fight. Necro's 6s leave Black Hearts; a last heart taken
/// with one kills outright, and the dead rise as zombies following whoever killed them (a hero's kill
/// rises as a companion, a hero slain by a monster rises as one of its monsters). Life's 6s grow plants
/// where the target stands, on the attacker's side; where plants grow already, a Sentient Shrub springs
/// up to fight for that side.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The Sentient Shrub's monster, for its look and a monster side's shrubs.</summary>
    private const string ShrubId = "sentient_shrub";

    /// <summary>What a hero slain by Necro dice rises as, on the monsters' side.</summary>
    private const string ZombieId = "zombie";

    /// <summary>Necrosis: a Black Heart for each 6, as many as the hearts they're missing.</summary>
    private static void Necrosis(Conditions conditions, int entityId, int missing, int stacks, int byId, List<GameEvent> events)
    {
        int black = Math.Min(stacks, missing - conditions[Condition.BlackHeart]);
        if (black > 0)
        {
            AddCondition(conditions, entityId, Condition.BlackHeart, black, byId, events);
        }
    }

    /// <summary>A monster a hero's Necro dice killed rises as a zombie of itself, at the hero's side. Not bosses, nor the raised dead.</summary>
    private void RiseForHero(Room room, Monster dead, Hero killer, List<GameEvent> events)
    {
        MonsterDefinition definition = dead.Definition;
        if (definition.Boss || definition.Miniboss || dead.RaisedBy is not null || room.FindMonster(dead.Id) is not null || !killer.IsInPlay)
        {
            return;
        }

        Stats stats = new()
        {
            Hearts = definition.Hearts,
            MoveDice = definition.WendDice,
            AttackDice = definition.AttackDice,
            DefenseDice = definition.DefenseDice,
            Weapon = definition.Weapon,
            Armor = definition.Armor,
        };
        Summon(room, killer, $"Zombie {definition.Name}", stats, definition.Id, dead.Position, fixedWend: null, events);
    }

    /// <summary>A hero a monster's Necro dice brought down rises as a zombie on its side.</summary>
    private void RiseForMonsters(Room room, Hero dead, List<GameEvent> events)
    {
        Stats stats = dead.EffectiveStats;
        MonsterDefinition zombie = Content.GetMonster(ZombieId) with
        {
            Name = $"Zombie {dead.Name}",
            Undead = UndeadKind.Greater,
            Hearts = Math.Max(1, dead.MaxHearts),
            AttackDice = Math.Max(1, stats.AttackDice),
            DefenseDice = Math.Max(0, stats.DefenseDice),
        };
        if (LandingNear(room, dead.Position) is not { } tile)
        {
            return;
        }

        Monster monster = CreateMonster(State.NextId(), zombie, tile, PartyStrength());
        room.AddMonster(monster);
        events.Add(new MonsterSpawned(monster.Id, zombie.Id, room.Id, tile, monster.Hearts) { Cause = SpawnCause.Raised });
    }

    /// <summary>
    /// After a Necro hit on a hero: if it took their last heart, it kills them outright, and they rise
    /// as a zombie on the monsters' side.
    /// </summary>
    private void NecroDeath(Room room, Hero hero, bool fromMonster, List<GameEvent> events)
    {
        if (!hero.IsKeelingOver || hero.KeelRolls > 0 || hero.DeathMarks > 0)
        {
            return;
        }

        Die(room, hero, events);
        if (fromMonster && State.Phase == RunPhase.InProgress)
        {
            RiseForMonsters(room, hero, events);
        }
    }

    /// <summary>
    /// Life: plants grow where the target stands, for the attacker's side; where plants grow already
    /// (either side's), each 6 raises a Sentient Shrub beside them for the attacker's side.
    /// </summary>
    /// <param name="hero">The hero who struck (a hero's side), or null for a monster's blow.</param>
    private void Grow(Room room, GridPosition tile, int stacks, Hero? hero, List<GameEvent> events)
    {
        bool heroes = hero is not null;
        for (int i = 0; i < stacks; i++)
        {
            if (!room.Plants.ContainsKey(tile))
            {
                room.Grow(tile, heroes);
                events.Add(new PlantsGrew(room.Id, tile, heroes));
                continue;
            }

            if (LandingNear(room, tile) is not { } spot)
            {
                return;
            }

            if (hero is not null)
            {
                Stats shrub = new() { Hearts = 1, AttackDice = 1, MoveDice = 1 };
                Summon(room, hero, "Sentient Shrub", shrub, ShrubId, spot, fixedWend: 1, events);
            }
            else
            {
                MonsterDefinition definition = Content.GetMonster(ShrubId);
                Monster monster = CreateMonster(State.NextId(), definition, spot, PartyStrength());
                room.AddMonster(monster);
                events.Add(new MonsterSpawned(monster.Id, definition.Id, room.Id, spot, monster.Hearts) { Cause = SpawnCause.Grown });
            }
        }
    }

    /// <summary>A summoned companion joins the fight beside <paramref name="near"/>, following the hero's player, ready to fight.</summary>
    private void Summon(Room room, Hero by, string name, Stats stats, string guise, GridPosition near, int? fixedWend, List<GameEvent> events)
    {
        if (LandingNear(room, near) is not { } tile)
        {
            return;
        }

        Hero owner = by.OwnerId is int ownerId ? State.GetHero(ownerId) : by;
        var summoned = new Hero(State.NextId(), State.Heroes.Max(hero => hero.Seat) + 1, name, stats, room.Id, tile)
        {
            OwnerId = owner.Id,
            Summoned = true,
            Guise = guise,
            FixedWendFace = fixedWend,
        };
        summoned.Order = CompanionOrder.Fight;
        summoned.Hearts = summoned.MaxHearts;
        State.AddHero(summoned);
        events.Add(new HeroSummoned(summoned.Id, owner.Id, name, guise, room.Id, tile));
    }
}
