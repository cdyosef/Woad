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
/// splashes, Elektrobolt jumps, Squall Wave shoves and Swirly Eyes moves its target. Some can be cast
/// another way: as a boon on the caster or an ally (Lifeforce, Shining Light, Shadowbind), at bare floor
/// to lay ice (Icicle Arrow), or with fire at an unlit brazier to light it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What a spell does, aimed where it is.</summary>
    private enum SpellMode
    {
        Attack,

        /// <summary>A boon on the caster or an ally.</summary>
        Boon,

        /// <summary>Ice on bare floor.</summary>
        Ice,

        /// <summary>Fire at an unlit brazier.</summary>
        Kindle,
    }

    /// <summary>
    /// Why the active hero can't cast <paramref name="spellId"/> at <paramref name="target"/> now, or null.
    /// With no target, whether they could cast it at all (they know it, have the Power and a Rumble die).
    /// </summary>
    public string? CheckCastSpell(string spellId, GridPosition? target = null, int extraPower = 0, GridPosition? moveTo = null) =>
        CheckCast(spellId, extraPower) ?? (target is null && Content.FindSpell(spellId)?.Shape != SpellShape.Around ? null : CheckAim(Content.GetSpell(spellId), target, moveTo, out _, out _));

    /// <summary>The tiles the active hero could aim <paramref name="spellId"/> at now: where it would reach someone, bless someone, lay ice or light a brazier.</summary>
    public IReadOnlyList<GridPosition> GetSpellTargets(string spellId)
    {
        if (CheckCast(spellId, 0) is not null || Content.GetSpell(spellId) is not { Shape: not SpellShape.Around } spell)
        {
            return [];
        }

        return [.. ActiveRoom.Positions.Where(tile => CheckAim(spell, tile, null, out _, out _) is null)];
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
        if (CheckAim(spell, command.Target, command.MoveTo, out List<int> targets, out SpellMode mode) is { } aimError)
        {
            return aimError;
        }

        if (CheckMagicOdHand(command.OdHandDice) is { } odHandError)
        {
            return odHandError;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        int power = spell.Cost + command.ExtraPower;
        PaySpell(hero, power, events);
        int rumble = AttackDiceLeft;
        Turn.AttackDiceUsed += rumble;

        // Casting gives a Shadowbound hero away (all but Shadowbind itself, on themselves).
        if (!(mode == SpellMode.Boon && spell.Ward == SpellWard.Self))
        {
            Reveal(hero, events);
        }

        int dice = spell.Dice + command.ExtraPower;
        switch (mode)
        {
            case SpellMode.Boon:
                events.Add(new SpellCast(hero.Id, spell.Id, power, targets));
                Bless(hero, room, spell, State.GetHero(targets[0]), dice, events);
                return null;
            case SpellMode.Ice:
                events.Add(new SpellCast(hero.Id, spell.Id, power, []));
                CastIce(hero, room, spell, command.Target!.Value, dice, events);
                return null;
            case SpellMode.Kindle:
                events.Add(new SpellCast(hero.Id, spell.Id, power, []));
                Kindle(room, command.Target!.Value, events);
                return null;
        }

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

        // An extra die against some targets only: Quake's against those on rubble, Shadowbind's against those in darkness.
        Dictionary<int, (int Total, int Sixes)> extra = [];
        List<int> harder = [.. targets.Where(id =>
            (spell.RubbleDie && room.FindMonster(id) is { } monster && monster.Tiles.Any(tile => room.Layout[tile] == TileKind.Rubble))
            || (spell.DarknessDie && PositionOf(room, id) is { } at && InDarkness(room, at)))];
        if (harder.Count > 0)
        {
            (int[] faces, DieType[] types) = RollTypedDice([(spell.DieType, 1)]);
            var roll = new DiceRoll(faces);
            events.Add(new ExtraDiceRolled(hero.Id, roll, types, harder));
            foreach (int id in harder)
            {
                extra[id] = (roll.Total, Sixes(faces));
            }
        }

        // A fiery Blast lights the braziers it covers.
        if (spell.Shape == SpellShape.Burst && spell.DieType == DieType.Fire && command.Target is { } blasted)
        {
            foreach (GridPosition brazier in BurstTiles(room, blasted).Where(tile => UnlitBrazierAt(room, tile)).ToList())
            {
                Kindle(room, brazier, events);
            }
        }

        // An Od Hand die added, and the Focus Bonus.
        Turn.OdHandDiceUsed += command.OdHandDice;
        var casting = new SpellCasting(spell, command.Target, extra, spell.Shove ? rumble : 0, spell.Swirl ? command.MoveTo : null);
        RollHeroAttack(hero, command.OdHandDice, hero.Focus + (spell.Id == HurlFireId ? FireBonus(hero) : 0), targets, DamageSource.Spell, events, typed: [(spell.DieType, dice)], casting: casting);
        return null;
    }

    /// <summary>The spell the flame of Hold Fire comes from, and is hurled as.</summary>
    private const string HurlFireId = "hurl_fire";

    /// <summary>Why a magic attack can't add <paramref name="dice"/> Od Hand dice: at most one, and only while there are some left.</summary>
    private string? CheckMagicOdHand(int dice) =>
        dice < 0 || dice > GameRules.MagicOdHandDice ? $"A magic attack can add at most {GameRules.MagicOdHandDice} Od Hand die."
        : dice > OdHandDiceLeft ? "You've no Od Hand dice left to add."
        : null;

    /// <summary>Why the active hero can't hurl the flame of Hold Fire now (at <paramref name="target"/>, if given), or null.</summary>
    public string? CheckHurlHeldFire(GridPosition? target = null, int dice = 0)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (!hero.Flame)
        {
            return "You hold no fire to hurl.";
        }

        if (CheckMagicOdHand(dice) is { } odHand)
        {
            return odHand;
        }

        if (target is not { } aim)
        {
            return GetHeldFireTargets().Count > 0 ? null : "There's nothing in reach to hurl it at.";
        }

        return GetHeldFireTargets().Contains(aim) || (ActiveRoom.MonsterAt(aim) is { } monster && GetHeldFireTargets().Contains(monster.Position)) ? null : "That's out of reach.";
    }

    /// <summary>Where the flame of Hold Fire could be hurled: monsters within Hurl Fire's range and sight, and unlit braziers there to light.</summary>
    public IReadOnlyList<GridPosition> GetHeldFireTargets()
    {
        if (State.Turn is null || !ActiveHero.Flame || Content.FindSpell(HurlFireId) is not { } spell)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return [.. room.Monsters.Where(monster => SpellReaches(hero, room, monster) && InSpellRange(spell, room, hero, monster.NearestTile(hero.Position))).Select(monster => monster.Position),
            .. room.Positions.Where(tile => UnlitBrazierAt(room, tile) && InSpellRange(spell, room, hero, tile))];
    }

    private string? Handle(HurlHeldFire command, List<GameEvent> events)
    {
        if (CheckHurlHeldFire(command.Target, command.Dice) is { } error)
        {
            return error;
        }

        if (CheckBankedPowerDice(ActiveHero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        SpellDefinition spell = Content.GetSpell(HurlFireId);
        QuenchHeldFire(hero);
        Reveal(hero, events);

        // At an unlit brazier with no one there, it lights it.
        if (room.MonsterAt(command.Target) is not { } monster)
        {
            events.Add(new FlameHurled(hero.Id, 0));
            Kindle(room, command.Target, events);
            return null;
        }

        // At a monster: a Fire die and the Focus Bonus, with an Od Hand die if added; no Power to pay.
        Turn.OdHandDiceUsed += command.Dice;
        events.Add(new FlameHurled(hero.Id, monster.Id));
        SpendBankedPowerDice(hero, command.PowerDice, events);
        var casting = new SpellCasting(spell, monster.Position, new Dictionary<int, (int, int)>(), 0, null);
        RollHeroAttack(hero, command.Dice, hero.Focus + FireBonus(hero), [monster.Id], DamageSource.Spell, events, command.PowerDice, typed: [(spell.DieType, spell.Dice)], casting: casting);
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

        if (spell.Upgrades is not null)
        {
            return $"{spell.Name} isn't cast: it makes {Content.GetSpell(spell.Upgrades).Name} stronger.";
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
        return hero.SpellPower < power
            ? $"{spell.Name} takes {power} Power; you have {(hero.MagicSlots > 0 ? $"{hero.MagicCharged} Magic and " : "")}{hero.Power.Banked} banked."
            : null;
    }

    /// <summary>A spell's Power: Magic Dice first, then banked Power Dice.</summary>
    private static void PaySpell(Hero hero, int power, List<GameEvent> events)
    {
        int magic = Math.Min(hero.MagicCharged, power);
        if (magic > 0)
        {
            hero.MagicCharged -= magic;
            events.Add(new MagicDiceSpent(hero.Id, magic, hero.MagicCharged));
        }

        if (power - magic > 0)
        {
            hero.Power.Banked -= power - magic;
            events.Add(new PowerDiceSpent(hero.Id, power - magic, hero.Power.Banked));
        }
    }

    /// <summary>
    /// The start of a hero's turn: a d6 for each empty Magic Energy slot, and each 6 charges one. The dice
    /// come from the run's seed, the hero and the round, apart from the run's own, so saved runs replay the same.
    /// </summary>
    private void ChargeMagic(Hero hero, List<GameEvent> events)
    {
        int empty = hero.MagicSlots - hero.MagicCharged;
        if (empty <= 0 || hero.IsKeelingOver)
        {
            return;
        }

        var random = new Pcg32(State.Seed ^ ((ulong)(uint)hero.Id << 20) ^ (ulong)(uint)State.Round, sequence: 0x3A61C);
        List<int> faces = [.. Enumerable.Range(0, empty).Select(_ => random.NextInt(1, 7))];
        hero.MagicCharged += faces.Count(face => face == 6);
        events.Add(new MagicCharged(hero.Id, faces, hero.MagicCharged, hero.MagicSlots));
    }

    /// <summary>Who the spell would hit (or bless), aimed at <paramref name="target"/>, and how; or why it can't be aimed there.</summary>
    private string? CheckAim(SpellDefinition spell, GridPosition? target, GridPosition? moveTo, out List<int> targets, out SpellMode mode)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        targets = [];
        mode = SpellMode.Attack;
        if (spell.Shape == SpellShape.Around)
        {
            targets = [.. room.Monsters.Where(monster => monster.Tiles.Any(tile => tile.ChebyshevDistanceTo(hero.Position) == 1) && SpellReaches(hero, room, monster)).Select(monster => monster.Id)];
            return targets.Count > 0 ? null : "There are no monsters beside you.";
        }

        if (target is not { } aim || !room.Layout.Contains(aim))
        {
            return "Choose a target.";
        }

        // On yourself or an ally: its boon, for a spell that has one.
        Hero? blessed = State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == aim);
        if (blessed is not null && spell.Ward != SpellWard.None && (blessed == hero
            || (spell.Ward == SpellWard.Beside && aim.ChebyshevDistanceTo(hero.Position) <= 1)
            || (spell.Ward == SpellWard.Ally && InSpellRange(spell, room, hero, aim))))
        {
            targets = [blessed.Id];
            mode = SpellMode.Boon;
            return null;
        }

        if (aim == hero.Position)
        {
            return spell.Ward == SpellWard.None ? "Choose a target." : $"{spell.Name} can only be cast on {(spell.Ward == SpellWard.Self ? "yourself" : "yourself or an ally")} like that.";
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

        if (spell.Shape == SpellShape.Burst)
        {
            targets = [.. BurstTiles(room, aim).SelectMany(tile => WhoIsOn(room, hero, tile)).Distinct()];
            if (targets.Count == 0 && spell.DieType == DieType.Fire && UnlitBrazierAt(room, aim))
            {
                mode = SpellMode.Kindle;
                return null;
            }

            return targets.Count > 0 ? null : "There's no one there for the blast to reach.";
        }

        if (WhoIsOn(room, hero, aim).Select(id => (int?)id).FirstOrDefault() is not int struck)
        {
            // Nobody there: ice on bare floor, or fire at an unlit brazier.
            if (spell.Floor && CanFreeze(room, aim) && room.MonsterAt(aim) is null)
            {
                mode = SpellMode.Ice;
                return null;
            }

            if (spell.DieType == DieType.Fire && UnlitBrazierAt(room, aim))
            {
                mode = SpellMode.Kindle;
                return null;
            }

            return "There's no one there.";
        }

        targets = [struck];
        if (moveTo is { } to && (room.FindMonster(struck) is not { } swirled || !CanSwirlTo(room, swirled, to)))
        {
            return "Pick a free tile beside it to move it to.";
        }

        return null;
    }

    /// <summary>
    /// A boon: the spell's dice roll for how long it lasts (a round per die) and how strong it is (its 6s).
    /// Lifeforce grows plants under them and heals a heart per 6; Shining Light gives light, +1 Defense
    /// (+1 per 6) and a Search die; Shadowbind hides the caster from monsters.
    /// </summary>
    private void Bless(Hero caster, Room room, SpellDefinition spell, Hero target, int dice, List<GameEvent> events)
    {
        (int[] faces, DieType[] types) = RollTypedDice([(spell.DieType, dice)]);
        events.Add(new SpellRolled(caster.Id, new DiceRoll(faces), types));
        int sixes = Sixes(faces);
        switch (spell.DieType)
        {
            case DieType.Life:
                if (!room.Plants.ContainsKey(target.Position))
                {
                    room.Grow(target.Position, heroes: true);
                    events.Add(new PlantsGrew(room.Id, target.Position, true));
                }

                int healed = Math.Min(sixes, target.HeartsToHeal);
                if (healed > 0)
                {
                    target.Hearts += healed;
                    events.Add(new HeroHealed(target.Id, healed, target.Hearts));
                }

                break;

            case DieType.Radiate:
                target.ShiningRounds = Math.Max(target.ShiningRounds, faces.Length);
                target.ShiningDefense = Math.Max(target.ShiningDefense, 1 + sixes);
                events.Add(new BoonGained(target.Id, Boon.ShiningLight, target.ShiningRounds, target.ShiningDefense));
                break;

            case DieType.Shadow:
                target.ShadowboundRounds = Math.Max(target.ShadowboundRounds, faces.Length);
                events.Add(new BoonGained(target.Id, Boon.Shadowbound, target.ShadowboundRounds, 0));
                break;

            case DieType.Fire:
                if (GrantHeldFire(target, events))
                {
                    events.Add(new BoonGained(target.Id, Boon.Flame, 0, 0));
                }
                break;
        }
    }

    /// <summary>Whether Swirly Eyes can move the monster to <paramref name="to"/>: a free tile beside it, open ground or a pit or deep water to drop it in.</summary>
    private bool CanSwirlTo(Room room, Monster monster, GridPosition to) =>
        monster.Tiles.Count() == 1 && to.ChebyshevDistanceTo(monster.Position) == 1 && room.Layout.Contains(to) && !IsOccupied(room, to)
        && (room.Layout[to].IsWalkable(monster.Flies) || room.Layout[to] is TileKind.Pit or TileKind.DeepWater);

    /// <summary>The tiles a Burst spell aimed at <paramref name="aim"/> covers: that tile and the 8 around it.</summary>
    public static IEnumerable<GridPosition> BurstTiles(Room room, GridPosition aim) =>
        room.Positions.Where(tile => tile.ChebyshevDistanceTo(aim) <= GameRules.BurstRadius);

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
