using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Scenes.Game;

/// <summary>
/// A simple player for the <c>--autoplay</c> debug mode: smoke-tests event playback and lets
/// screenshots be captured without clicking. Heals when hurt, bombs groups, closes in and attacks.
/// </summary>
public static class AutoplayBot
{
    public static GameCommand NextCommand(GameSession session)
    {
        Hero hero = session.State.ActiveHero!;
        TurnState turn = session.State.Turn!;
        Room room = session.State.RoomOf(hero);

        if (turn.PendingAttack is not null)
        {
            return new ConfirmAttack(hero.Id);
        }

        if (hero.Hearts <= hero.MaxHearts - 2 && BeltCard(session, hero, CardEffectKind.Heal) is { } potion)
        {
            return new UseCard(hero.Id, potion.Id);
        }

        if (BeltCard(session, hero, CardEffectKind.Attack) is { } bomb)
        {
            return new UseCard(hero.Id, bomb.Id, session.GetCardTargets(bomb.Id)[0]);
        }

        if (!turn.HasActed && session.GetAttackableMonsters().FirstOrDefault() is { } target)
        {
            return new AttackMonster(hero.Id, target.Id);
        }

        if (turn.MovementRoll is null && !turn.MovementLocked)
        {
            return new RollMovement(hero.Id);
        }

        IReadOnlyDictionary<GridPosition, int> reachable = session.GetReachableTiles();
        if (reachable.Count > 0 && room.Monsters.Count > 0)
        {
            int DistanceToMonsters(GridPosition tile) => room.Monsters.Min(monster => monster.Position.ManhattanDistanceTo(tile));

            GridPosition best = reachable.Keys
                .OrderBy(DistanceToMonsters)
                .ThenBy(tile => reachable[tile])
                .First();
            if (DistanceToMonsters(best) < DistanceToMonsters(hero.Position))
            {
                return new MoveHero(hero.Id, best);
            }
        }

        return new EndTurn(hero.Id);
    }

    /// <summary>A belt card with the given effect that can be used right now.</summary>
    private static CardInstance? BeltCard(GameSession session, Hero hero, CardEffectKind kind) =>
        hero.Belt.FirstOrDefault(card =>
            card.Definition.Effect?.Kind == kind && session.GetCardTargets(card.Id).Count > 0);
}
