using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// XP for the work of a dungeon besides fighting, and the tallies behind the end-of-run awards: a key used
/// (<see cref="GameRules.KeyXp"/>), a lock picked (<see cref="GameRules.PickLockXp"/>), a trap disarmed
/// (<see cref="GameRules.DisarmXp"/>); doors unlocked and traps set off are counted too. A monster's XP is
/// shared by the damage done to it (see <see cref="ShareKillXp"/>).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Credits what a command's events show the heroes doing: XP and the award tallies.</summary>
    private void CreditDeeds(List<GameEvent> events)
    {
        var xp = new Dictionary<Hero, int>();
        void Earn(int heroId, int amount)
        {
            if (State.Heroes.FirstOrDefault(hero => hero.Id == heroId) is { } hero)
            {
                xp[hero] = xp.GetValueOrDefault(hero) + amount;
            }
        }

        Hero? Doer(int heroId) => State.Heroes.FirstOrDefault(hero => hero.Id == heroId);
        HashSet<int> keyed = [.. events.OfType<KeyUsed>().Select(used => used.HeroId)];
        foreach (GameEvent gameEvent in events.ToList())
        {
            switch (gameEvent)
            {
                case KeyUsed used when Doer(used.HeroId) is { } hero:
                    hero.KeysUsed++;
                    Earn(hero.Id, GameRules.KeyXp);
                    break;

                case DoorUnlocked door when Doer(door.HeroId) is { } hero:
                    if (door.LocksLeft == 0)
                    {
                        hero.DoorsUnlocked++;
                    }

                    if (door.Picked)
                    {
                        Earn(hero.Id, GameRules.PickLockXp + door.Difficulty / GameRules.LockXpPer);
                    }
                    else if (!keyed.Contains(hero.Id))
                    {
                        // The Boss Key, or a gate's key (the Emerald Key): a key used too.
                        hero.KeysUsed++;
                        Earn(hero.Id, GameRules.KeyXp);
                    }
                    else
                    {
                        Earn(hero.Id, door.Difficulty / GameRules.LockXpPer);
                    }

                    break;

                case GateOpened gate when Doer(gate.HeroId) is { } hero:
                    hero.DoorsUnlocked++;
                    if (gate.Picked)
                    {
                        Earn(hero.Id, GameRules.PickLockXp);
                    }

                    break;

                case ChestUnlocked { Picked: true } chest:
                    Earn(chest.HeroId, GameRules.PickLockXp + chest.Difficulty / GameRules.LockXpPer);
                    break;

                case ChestUnlocked chest:
                    Earn(chest.HeroId, chest.Difficulty / GameRules.LockXpPer);
                    break;

                case TrapDisarmed disarmed when Doer(disarmed.HeroId) is { } hero:
                    hero.TrapsDisarmed++;
                    Earn(hero.Id, GameRules.DisarmXp);
                    break;

                case TrapSprung sprung when Doer(sprung.HeroId) is { } hero:
                    hero.TrapsSprung++;
                    break;
            }
        }

        foreach ((Hero hero, int amount) in xp)
        {
            GrantXp(hero, amount, events);
        }
    }

    /// <summary>Takes hearts off a monster, crediting <paramref name="by"/> with those it really lost (no overkill).</summary>
    private static void Wound(Monster monster, Hero? by, int damage)
    {
        int dealt = Math.Min(Math.Max(0, damage), monster.Hearts);
        monster.Hearts -= dealt;
        if (by is not null && dealt > 0)
        {
            monster.DamageBy[by.Id] = monster.DamageBy.GetValueOrDefault(by.Id) + dealt;
            by.DamageDealt += dealt;
        }
    }

    /// <summary>
    /// A defeated monster's XP, shared among the heroes who hurt it, by the hearts each took off it; only those
    /// still in it (Keeling Over will do; the dead's share goes to the rest), the odd points to whoever did the
    /// most. The killing blow earns <see cref="GameRules.KillingBlowXp"/> more, or <see cref="GameRules.KillingBlowPercent"/>%
    /// of the monster's XP if that's more. Killed without a scratch from anyone (shoved into the dark), it's all the killer's.
    /// </summary>
    private void ShareKillXp(Hero? killer, Monster monster, List<GameEvent> events)
    {
        int total = monster.Definition.Xp;
        bool Sharing(Hero hero) => hero.Status != HeroStatus.Dead && !hero.Summoned;
        List<(Hero Hero, int Damage)> shares = [.. monster.DamageBy
            .Select(entry => (Hero: State.Heroes.FirstOrDefault(hero => hero.Id == entry.Key), Damage: entry.Value))
            .Where(entry => entry.Hero is not null && Sharing(entry.Hero))
            .Select(entry => (entry.Hero!, entry.Damage))
            .OrderByDescending(entry => entry.Damage).ThenBy(entry => entry.Item1 == killer ? 0 : 1).ThenBy(entry => entry.Item1.Seat)];
        var xp = new Dictionary<Hero, int>();
        if (shares.Count == 0)
        {
            if (killer is not null && Sharing(killer))
            {
                xp[killer] = total;
            }
        }
        else
        {
            int damage = shares.Sum(share => share.Damage);
            foreach ((Hero hero, int dealt) in shares)
            {
                xp[hero] = total * dealt / damage;
            }

            xp[shares[0].Hero] += total - xp.Values.Sum();
        }

        if (killer is not null && Sharing(killer) && total > 0)
        {
            xp[killer] = xp.GetValueOrDefault(killer) + Math.Max(GameRules.KillingBlowXp, (total * GameRules.KillingBlowPercent + 99) / 100);
        }

        foreach ((Hero hero, int amount) in xp.OrderBy(entry => entry.Key.Seat))
        {
            GrantXp(hero, amount, events);
        }
    }
}
