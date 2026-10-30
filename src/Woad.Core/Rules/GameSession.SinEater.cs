using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The Sineater Doll: it soaks up what ails you or an ally beside you, no roll needed, up to
/// <see cref="GameRules.DollCapacity"/> stacks of anything. Thrown like a bomb (but bursting where it
/// lands), it lays every stack it holds on everyone within a tile, friend or foe, with no save, and is gone.
/// The undead shrug off its Poison.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't soak <paramref name="ailment"/> off <paramref name="targetId"/> into the doll, or null.</summary>
    public string? CheckSoakUp(int cardId, int targetId, Ailment ailment)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (hero.FindCard(cardId) is not var (doll, location) || !doll.Definition.SinEater)
        {
            return "You don't have a Sineater Doll.";
        }

        if (doll.SoakedStacks >= GameRules.DollCapacity)
        {
            return $"The doll is full: it holds {GameRules.DollCapacity} stacks already. Throw it.";
        }

        if (State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId && other.IsActive) is not { } patient)
        {
            return "There's nobody there.";
        }

        if (patient.Position.ChebyshevDistanceTo(hero.Position) > 1)
        {
            return $"Stand next to {patient.Name}.";
        }

        if (ailment.Stacks(patient.Conditions, patient.FireDice, patient.Stun) == 0)
        {
            return $"{(patient == hero ? "You have" : $"{patient.Name} has")} no {ailment.Name()}.";
        }

        if (location == CardLocation.Belt)
        {
            return Turn.UsedBeltItem ? "You've already used your free belt item this turn." : null;
        }

        return Turn.HasActed ? "Using a backpack card takes your action, which you've used." : null;
    }

    private string? Handle(SoakUp command, List<GameEvent> events)
    {
        if (CheckSoakUp(command.CardId, command.TargetId, command.Ailment) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        (CardInstance doll, CardLocation location) = hero.FindCard(command.CardId)!.Value;
        if (location == CardLocation.Belt)
        {
            Turn.UsedBeltItem = true;
        }
        else
        {
            SpendAction();
        }

        Hero patient = State.GetHero(command.TargetId);
        int taken = RemoveAilment(patient, command.Ailment, GameRules.DollCapacity - doll.SoakedStacks, hero.Id, events);
        doll.Soak(command.Ailment, taken);
        events.Add(new SoakedUp(hero.Id, doll.Id, patient.Id, command.Ailment, taken, doll.SoakedStacks));
        return null;
    }

    /// <summary>Where a thrown doll can land: open ground in range and sight, with something in it to let loose.</summary>
    private string? CheckDollThrow(Hero hero, Room room, CardEffect effect, GridPosition? tile)
    {
        if (tile is not { } aim)
        {
            return "Choose where to throw it.";
        }

        if (room.Layout[aim] == TileKind.Wall || !Targeting.InRangeAndSight(room.Layout, hero.Position, aim, effect.Range)
            || (IsBlind(hero.Conditions) && aim.ChebyshevDistanceTo(hero.Position) > 1))
        {
            return "That tile is out of range.";
        }

        return null;
    }

    /// <summary>The doll bursts: every stack it holds on everyone within its radius, no save.</summary>
    private void BurstDoll(Hero hero, Room room, CardInstance doll, CardEffect effect, GridPosition tile, List<GameEvent> events)
    {
        List<Monster> monsters = [.. room.Monsters.Where(monster => monster.NearestTile(tile).ChebyshevDistanceTo(tile) <= effect.AreaRadius)];
        List<Hero> heroes = [.. State.ActiveHeroesIn(room).Where(other => other.Position.ChebyshevDistanceTo(tile) <= effect.AreaRadius)];
        events.Add(new DollBurst(hero.Id, tile, [.. monsters.Select(monster => monster.Id), .. heroes.Select(other => other.Id)], doll.Soaked.ToDictionary()));
        foreach ((Ailment ailment, int stacks) in doll.Soaked)
        {
            foreach (Monster monster in monsters.Where(monster => room.FindMonster(monster.Id) is not null))
            {
                Afflict(room, monster, ailment, stacks, hero, events);
            }

            foreach (Hero caught in heroes.Where(other => other.IsActive))
            {
                Afflict(caught, ailment, stacks, hero.Id, events);
            }
        }

        doll.Soak(null, 0);
    }

    private void Afflict(Room room, Monster monster, Ailment ailment, int stacks, Hero by, List<GameEvent> events)
    {
        switch (ailment)
        {
            case Ailment.Poison when monster.Definition.Undead is not null:
                break;

            case Ailment.Burning:
                for (int i = 0; i < stacks; i++)
                {
                    Ignite(room, monster, by.Id, by.Id, events);
                }

                break;

            case Ailment.Stun:
                Stun(room, monster, powerful: false, stacks, by, events);
                break;

            case Ailment.BlackHeart:
                Necrosis(monster.Conditions, monster.Id, monster.MaxHearts - monster.Hearts, stacks, by.Id, events);
                break;

            default:
                Afflict(monster, by);
                AddCondition(monster.Conditions, monster.Id, ailment.AsCondition()!.Value, stacks, by.Id, events);
                break;
        }
    }

    private void Afflict(Hero hero, Ailment ailment, int stacks, int byId, List<GameEvent> events)
    {
        switch (ailment)
        {
            case Ailment.Burning:
                for (int i = 0; i < stacks; i++)
                {
                    Ignite(hero, byId, events);
                }

                break;

            case Ailment.Stun:
                Stun(hero, powerful: false, stacks, events);
                break;

            case Ailment.BlackHeart:
                Necrosis(hero.Conditions, hero.Id, hero.MaxHearts - hero.Hearts, stacks, byId, events);
                break;

            default:
                AddCondition(hero.Conditions, hero.Id, ailment.AsCondition()!.Value, stacks, byId, events);
                break;
        }
    }
}
