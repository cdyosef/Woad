using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// Casting spells (see <see cref="SpellDefinition"/>). A cast spends the spell's Power from the bank
/// (each extra Power, up to the Magic rank, adds a die) and the turn's Rumble dice, and rolls the spell's
/// typed dice plus the caster's Magic rank as an attack on everyone it reaches: one creature in range, a
/// straight line of them, or every monster beside the caster. Some do more as they land: Acid Ball
/// splashes, Elektrobolt jumps, Squall Wave shoves and Swirly Eyes moves its target.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Why the active hero can't cast <paramref name="spellId"/> at <paramref name="target"/> now, or null.
    /// With no target, whether they could cast it at all (they know it, have the Power and a Rumble die).
    /// </summary>
    public string? CheckCastSpell(string spellId, GridPosition? target = null, int extraPower = 0, GridPosition? moveTo = null) =>
        CheckCast(spellId, extraPower) ?? (target is null && Content.FindSpell(spellId)?.Shape != SpellShape.Around ? null : CheckAim(Content.GetSpell(spellId), target, moveTo, out _));

    /// <summary>The tiles the active hero could aim <paramref name="spellId"/> at now: where it would reach someone.</summary>
    public IReadOnlyList<GridPosition> GetSpellTargets(string spellId)
    {
        if (CheckCast(spellId, 0) is not null || Content.GetSpell(spellId) is not { Shape: not SpellShape.Around } spell)
        {
            return [];
        }

        return [.. ActiveRoom.Positions.Where(tile => CheckAim(spell, tile, null, out _) is null)];
    }

    /// <summary>The tiles a spell could reach from where the active hero stands, someone there or not (for the screens' range shading).</summary>
    public IReadOnlyList<GridPosition> GetSpellRange(string spellId)
    {
        if (State.Turn is null || Content.FindSpell(spellId) is not { } spell)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return spell.Shape switch
        {
            SpellShape.Around => [.. room.Positions.Where(tile => tile.ChebyshevDistanceTo(hero.Position) == 1)],
            SpellShape.Line => [.. LineTargets(room, hero.Position, null, spell.Range)],
            _ => [.. room.Positions.Where(tile => tile != hero.Position && InSpellRange(spell, room, hero, tile))],
        };
    }

    private string? Handle(CastSpell command, List<GameEvent> events)
    {
        if (CheckCast(command.SpellId, command.ExtraPower) is { } error)
        {
            return error;
        }

        SpellDefinition spell = Content.GetSpell(command.SpellId);
        if (CheckAim(spell, command.Target, command.MoveTo, out List<int> targets) is { } aimError)
        {
            return aimError;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        int power = spell.Cost + command.ExtraPower;
        hero.Power.Banked -= power;
        events.Add(new PowerDiceSpent(hero.Id, power, hero.Power.Banked));
        int rumble = AttackDiceLeft;
        Turn.AttackDiceUsed += rumble;

        int dice = spell.Dice + command.ExtraPower;
        if (spell.DrawsPoison && hero.Conditions[Condition.Poison] is > 0 and var poison)
        {
            dice += poison;
            AddCondition(hero.Conditions, hero.Id, Condition.Poison, -poison, hero.Id, events);
        }

        // Acid Ball splashes one random creature beside the one it's aimed at, friend or foe.
        if (spell.Splash && targets.Count > 0 && PositionOf(room, targets[0]) is { } struck)
        {
            List<int> beside = [.. room.Monsters.Where(monster => !targets.Contains(monster.Id) && monster.Tiles.Any(tile => tile.ChebyshevDistanceTo(struck) == 1)).Select(monster => monster.Id),
                .. State.ActiveHeroesIn(room).Where(other => other != hero && !targets.Contains(other.Id) && other.Position.ChebyshevDistanceTo(struck) == 1).Select(other => other.Id)];
            if (beside.Count > 0)
            {
                targets.Add(_random.Behavior.Pick(beside));
            }
        }

        events.Add(new SpellCast(hero.Id, spell.Id, power, targets));

        // Quake's extra die, against those on rubble only.
        Dictionary<int, (int Total, int Sixes)> extra = [];
        List<int> onRubble = spell.RubbleDie ? [.. targets.Where(id => room.FindMonster(id) is { } monster && monster.Tiles.Any(tile => room.Layout[tile] == TileKind.Rubble))] : [];
        if (onRubble.Count > 0)
        {
            (int[] faces, DieType[] types) = RollTypedDice([(spell.DieType, 1)]);
            var roll = new DiceRoll(faces);
            events.Add(new ExtraDiceRolled(hero.Id, roll, types, onRubble));
            foreach (int id in onRubble)
            {
                extra[id] = (roll.Total, Sixes(faces));
            }
        }

        var casting = new SpellCasting(spell, command.Target, extra, spell.Shove ? rumble : 0, spell.Swirl ? command.MoveTo : null);
        RollHeroAttack(hero, 0, hero.EffectiveStats.Magic, targets, DamageSource.Spell, events, typed: [(spell.DieType, dice)], casting: casting);
        return null;
    }

    /// <summary>Whether the active hero could cast the spell at all now: they know it, it's their move, and they have the Power and a Rumble die.</summary>
    private string? CheckCast(string spellId, int extraPower)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (hero.Spells.FirstOrDefault(known => known.Id == spellId) is not { } spell)
        {
            return "You don't know that spell.";
        }

        if (AttackDiceLeft < 1)
        {
            return "Casting takes your Rumble dice, and you've none left this turn.";
        }

        int magic = Math.Max(0, hero.EffectiveStats.Magic);
        if (extraPower < 0 || extraPower > magic)
        {
            return magic == 0 ? "You need a rank of Magic to put more Power into a spell." : $"You can put at most {magic} more Power into a spell (your Magic).";
        }

        int power = spell.Cost + extraPower;
        return hero.Power.Banked < power
            ? $"{spell.Name} takes {power} Power; you have {hero.Power.Banked} banked."
            : null;
    }

    /// <summary>Who the spell would hit, aimed at <paramref name="target"/>, or why it can't be aimed there.</summary>
    private string? CheckAim(SpellDefinition spell, GridPosition? target, GridPosition? moveTo, out List<int> targets)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        targets = [];
        if (spell.Shape == SpellShape.Around)
        {
            targets = [.. room.Monsters.Where(monster => monster.Tiles.Any(tile => tile.ChebyshevDistanceTo(hero.Position) == 1) && SpellReaches(hero, room, monster)).Select(monster => monster.Id)];
            return targets.Count > 0 ? null : "There are no monsters beside you.";
        }

        if (target is not { } aim || !room.Layout.Contains(aim) || aim == hero.Position)
        {
            return "Choose a target.";
        }

        if (spell.Shape == SpellShape.Line)
        {
            if (!InLine(hero.Position, aim) || aim.ChebyshevDistanceTo(hero.Position) > spell.Range)
            {
                return $"Aim it in a straight line, up to {spell.Range} tiles.";
            }

            targets = [.. LineTargets(room, hero.Position, aim, spell.Range).SelectMany(tile => WhoIsOn(room, hero, tile))];
            return targets.Count > 0 ? null : "There's no one in that line.";
        }

        if (!InSpellRange(spell, room, hero, aim))
        {
            return "That's out of range.";
        }

        if (WhoIsOn(room, hero, aim).Select(id => (int?)id).FirstOrDefault() is not int struck)
        {
            return "There's no one there.";
        }

        targets = [struck];
        if (moveTo is { } to && (room.FindMonster(struck) is not { } swirled || !CanSwirlTo(room, swirled, to)))
        {
            return "Pick a free tile beside it to move it to.";
        }

        return null;
    }

    /// <summary>Whether Swirly Eyes can move the monster to <paramref name="to"/>: a free tile beside it, open ground or a pit or deep water to drop it in.</summary>
    private bool CanSwirlTo(Room room, Monster monster, GridPosition to) =>
        monster.Tiles.Count() == 1 && to.ChebyshevDistanceTo(monster.Position) == 1 && room.Layout.Contains(to) && !IsOccupied(room, to)
        && (room.Layout[to].IsWalkable(monster.Flies) || room.Layout[to] is TileKind.Pit or TileKind.DeepWater);

    /// <summary>Whether a spell aimed from where the hero stands reaches the tile: in range, in sight unless it ignores sight, and within a blind hero's reach.</summary>
    private static bool InSpellRange(SpellDefinition spell, Room room, Hero hero, GridPosition tile) =>
        room.Layout.Contains(tile) && room.Layout[tile] != TileKind.Wall
        && (spell.IgnoresSight ? tile.ManhattanDistanceTo(hero.Position) <= spell.Range : Targeting.InRangeAndSight(room.Layout, hero.Position, tile, spell.Range))
        && (!IsBlind(hero.Conditions) || tile.ChebyshevDistanceTo(hero.Position) <= 1);

    /// <summary>The ids of who a spell aimed at a tile would strike there: a monster it can reach, or (with friendly fire on) another hero.</summary>
    private IEnumerable<int> WhoIsOn(Room room, Hero caster, GridPosition tile)
    {
        if (room.MonsterAt(tile) is { } monster && SpellReaches(caster, room, monster))
        {
            yield return monster.Id;
        }
        else if (State.FriendlyFire && State.ActiveHeroesIn(room).FirstOrDefault(other => other != caster && other.Position == tile) is { } ally)
        {
            yield return ally.Id;
        }
    }

    private static bool InLine(GridPosition from, GridPosition to)
    {
        int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
        return dx == 0 || dy == 0 || dx == dy;
    }

    /// <summary>
    /// The tiles of a straight line out from <paramref name="from"/> toward <paramref name="toward"/>, up to
    /// <paramref name="range"/>, stopping at a wall; every direction's line when <paramref name="toward"/> is null.
    /// </summary>
    private static IEnumerable<GridPosition> LineTargets(Room room, GridPosition from, GridPosition? toward, int range)
    {
        IEnumerable<(int X, int Y)> directions = toward is { } aim
            ? [(Math.Sign(aim.X - from.X), Math.Sign(aim.Y - from.Y))]
            : [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];
        foreach ((int dx, int dy) in directions)
        {
            for (int step = 1; step <= range; step++)
            {
                var tile = new GridPosition(from.X + dx * step, from.Y + dy * step);
                if (!room.Layout.Contains(tile) || room.Layout[tile].BlocksSight())
                {
                    break;
                }

                yield return tile;
            }
        }
    }

    private GridPosition? PositionOf(Room room, int id) =>
        room.FindMonster(id)?.Position ?? State.ActiveHeroesIn(room).FirstOrDefault(other => other.Id == id)?.Position;

    /// <summary>
    /// What a spell does as it lands, beyond the roll: Elektrobolt's sixes jump to more monsters beside its
    /// target, Swirly Eyes moves a target it hit, and Squall Wave's Rumble dice try to shove its target.
    /// </summary>
    private void SpellAftermath(Hero hero, Room room, PendingRoll attack, IReadOnlyList<int> hit, List<GameEvent> events)
    {
        if (attack.Casting is not { } casting || attack.TargetIds.Count == 0)
        {
            return;
        }

        int first = attack.TargetIds[0];
        int sixes = Sixes(attack.Roll.Faces);
        if (casting.Spell.Cleave && sixes > 0 && casting.Aim is { } aim)
        {
            List<Monster> beside = room.Monsters
                .Where(monster => !attack.TargetIds.Contains(monster.Id) && monster.Tiles.Any(tile => tile.ChebyshevDistanceTo(aim) <= 1))
                .OrderBy(monster => monster.Hearts)
                .ThenBy(monster => monster.Id)
                .Take(sixes)
                .ToList();
            foreach (Monster monster in beside.Where(monster => room.FindMonster(monster.Id) is not null))
            {
                events.Add(new Cleaved(hero.Id, monster.Id));
                if (DamageMonster(hero, room, monster, attack.Total, DamageSource.Spell, events, ranged: true) > 0)
                {
                    LayTypedDice(room, monster, new Dictionary<DieType, int> { [casting.Spell.DieType] = sixes }, hero.Id, hero.Position, hero, events);
                }
            }
        }

        if (casting.MoveTo is { } to && hit.Contains(first) && room.FindMonster(first) is { } swirled && CanSwirlTo(room, swirled, to))
        {
            PushMonster(hero, room, swirled, to, events);
        }

        if (casting.ShoveDice > 0 && room.FindMonster(first) is not null)
        {
            RollHeroAttack(hero, casting.ShoveDice, 0, [first], DamageSource.Shove, events);
        }
    }
}
