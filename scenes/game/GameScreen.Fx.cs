using Godot;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The fighting-game feel of a blow landing. A hit freezes the action for a beat (hit-stop), flashes
/// the target, slashes across it in a spray of the attacker's sparks and shakes the view, under a big
/// HIT!, CRITICAL! (when Power Dice decided it) or K.O.!; several hits in a row count up a combo. A
/// miss is a plain MISS, unless a shield (or a monster's armor) made the difference: BLOCKED!, with
/// blue sparks. A hero brought down gets DOWN!, and stuns, fire, flips and pulls get callouts too.
/// </summary>
public partial class GameScreen
{
    /// <summary>The attack being resolved: who, its total, and how much of it came from Power Dice.</summary>
    private int _attackerId;
    private int _attackTotal;
    private int _attackPower;

    /// <summary>The latest defense roll, to tell a block from a miss and a crit from a plain hit.</summary>
    private DefenseRolled? _defense;

    /// <summary>Who's landing the current run of hits, and how many.</summary>
    private int _comboAttacker;
    private int _comboHits;

    /// <summary>Notes a new attack (or blast, or burn: <paramref name="attackerId"/> 0) for the callouts to come.</summary>
    private void BeginStrike(int attackerId, int total, int powerFaces)
    {
        if (attackerId == 0 || attackerId != _comboAttacker)
        {
            _comboAttacker = attackerId;
            _comboHits = 0;
        }

        _attackerId = attackerId;
        _attackTotal = total;
        _attackPower = powerFaces;
        _defense = null;
    }

    private void ResetCombo()
    {
        _comboAttacker = 0;
        _comboHits = 0;
    }

    /// <summary>A callout over a figure, if it's in the room on show.</summary>
    private void CalloutOver(int entityId, string text, Color color, int size = 2, bool below = false)
    {
        if (_figures.TryGetValue(entityId, out Figure? figure) && figure.RoomId == _shownRoomId)
        {
            _board.Callout(figure.Position, text, color, size, below);
        }
    }

    private async Task ShowDamage(Damaged damaged)
    {
        if (!_figures.TryGetValue(damaged.TargetId, out Figure? figure))
        {
            return;
        }

        figure.Hearts = damaged.HeartsLeft;
        bool answered = _defense?.DefenderId == damaged.TargetId;
        if (damaged.Amount == 0)
        {
            bool blocked = answered && Shielded(figure) && _defense!.Armor > 0 && _defense.Total - _defense.Armor < _attackTotal;
            _dice.ShowOutcome(false, blocked ? "BLOCKED" : "MISS");
            Sfx.Play("block", blocked ? 1.35f : 0.75f);
            if (!_tokens.ContainsKey(damaged.TargetId) || figure.RoomId != _shownRoomId)
            {
                return;
            }

            if (blocked)
            {
                _board.Callout(figure.Position, "BLOCKED!", Palette.Block);
                _board.Spark(figure.Position, Palette.Block, slash: false);
                _board.Shake(1.5f);
            }
            else
            {
                _board.Callout(figure.Position, "MISS", Palette.TextDim);
            }

            await Pause(0.35);
            return;
        }

        bool knockedOut = !figure.IsHero && damaged.HeartsLeft == 0;
        bool critical = answered && _attackPower > 0 && _attackTotal - _attackPower <= _defense!.Total;
        _dice.ShowOutcome(true, "HIT");
        _comboHits++;
        if (!_tokens.TryGetValue(damaged.TargetId, out TokenView? token))
        {
            return;
        }

        token.SetHearts(damaged.HeartsLeft);
        if (figure.RoomId != _shownRoomId)
        {
            return;
        }

        (string text, Color color, int size) = knockedOut ? ("K.O.!", Palette.TrimBright, 3)
            : critical ? ("CRITICAL!", Palette.Power, 2)
            : ("HIT!", Palette.Danger, 2);
        _board.Callout(figure.Position, text, color, size);
        if (_comboHits >= 2)
        {
            _board.Callout(figure.Position, $"{_comboHits} HITS!", Palette.Fire, 2, below: true);
        }

        _board.Spark(figure.Position, AttackerColor());
        _board.Shake(knockedOut || critical ? 5 : 2.5f);

        // Hit-stop: the target flashes white and everything holds for a beat, then it reels.
        token.Modulate = new Color(2.6f, 2.6f, 2.6f);
        await Pause(knockedOut || critical ? 0.14 : 0.08);
        await Reel(token);
    }

    /// <summary>The target reels from the blow: shaken side to side as its red flash fades.</summary>
    private async Task Reel(TokenView token)
    {
        Vector2 home = token.Position;
        Tween tween = CreateTween();
        tween.TweenProperty(token, "modulate", new Color(1, 0.3f, 0.3f), 0.04);
        foreach (float dx in new[] { 3f, -3f, 2f, -1f, 0f })
        {
            tween.TweenProperty(token, "position", home + new Vector2(dx, 0), 0.03);
        }

        tween.TweenProperty(token, "modulate", Colors.White, 0.15);
        await ToSignal(tween, Tween.SignalName.Finished);
        token.Position = home;
    }

    /// <summary>The spell being cast, for the color of its bolt; cleared once it's rolled.</summary>
    private SpellDefinition? _castSpell;

    /// <summary>
    /// How an attack looks as it's made: a weapon's swing (a spear's thrust) and a lunge in melee, a
    /// shove's rush, or a shot flying across: an arrow, a crossbow bolt, a spinning boomerang or dagger,
    /// a chain, or a spell's glowing bolt in the color of its dice.
    /// </summary>
    private async Task StrikeFx(AttackRolled attack)
    {
        SpellDefinition? spell = _castSpell;
        _castSpell = null;
        int targetId = attack.TargetIds.FirstOrDefault();
        if (!_tokens.TryGetValue(attack.AttackerId, out TokenView? attacker) || !_tokens.TryGetValue(targetId, out TokenView? target)
            || !_figures.TryGetValue(attack.AttackerId, out Figure? figure) || figure.RoomId != _shownRoomId)
        {
            return;
        }

        var middle = new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f - 2);
        Vector2 from = attacker.Position + middle, to = target.Position + middle;
        bool far = from.DistanceTo(to) > BoardView.TileSize * 1.6f;
        Color color = AttackerColor();
        CardDefinition? weapon = attack.WeaponId is { } id ? Content.FindCard(id) : null;
        DieType typed = attack.DieTypes.FirstOrDefault(type => type != DieType.Plain);

        if (attack.Shove)
        {
            _board.Push(from, to, color);
            await Lunge(attack.AttackerId, targetId);
            return;
        }

        ShotKind? shot = attack.Chain ? ShotKind.Chain
            : spell is not null ? (spell.Shape == SpellShape.Around || !far && spell.Range <= 1 ? null : ShotKind.Orb)
            : !far ? null
            : weapon is { Reload: not null } ? ShotKind.Bolt
            : weapon is { Id: "boomerang" } ? ShotKind.Boomerang
            : weapon is { ThrownRange: > 0 } ? ShotKind.Dagger
            : weapon is { AttackRange: > 1, Reach: false } ? ShotKind.Arrow
            : figure.Monster is { DieType: not DieType.Plain } || typed != DieType.Plain ? ShotKind.Orb
            : weapon is { Reach: true } ? null
            : ShotKind.Arrow;

        if (shot is { } kind)
        {
            Color tint = spell is not null ? DieTypes.Tint(spell.DieType)
                : kind == ShotKind.Orb ? DieTypes.Tint(figure.Monster?.DieType is { } own && own != DieType.Plain ? own : typed)
                : color;
            Sfx.Play("block", kind == ShotKind.Orb ? 1.8f : 2.2f);
            await Pause(_board.Shot(from, to, kind, tint));
            return;
        }

        if (spell is not null)
        {
            // A spell all around the caster (Quake): a ring bursting out from them.
            _board.Spark(figure.Position, DieTypes.Tint(spell.DieType), slash: false);
            await Lunge(attack.AttackerId, targetId);
            return;
        }

        _board.Swing(from, to, color, thrust: weapon is { Reach: true });
        await Lunge(attack.AttackerId, targetId);
    }

    /// <summary>A beat of the Icarus Wings: up they go (the music soaring), feathers puffing off.</summary>
    private async Task TakeWing(int heroId)
    {
        if (_figures.TryGetValue(heroId, out Figure? figure) && !figure.Aloft)
        {
            figure.Aloft = true;
            if (_tokens.TryGetValue(heroId, out TokenView? rising))
            {
                ApplyFigure(figure, rising);
            }
        }

        Music.Play("icarus");
        if (_tokens.TryGetValue(heroId, out TokenView? token) && figure?.RoomId == _shownRoomId)
        {
            _board.Feathers(token.Position + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f), burst: false);
            Sfx.Play("flap");
            await Pause(0.15);
        }
    }

    /// <summary>The wings break: a burst of feathers, the music cut dead, a whistling fall and a thud.</summary>
    private async Task BreakWings(int heroId)
    {
        Music.Cut();
        if (!_figures.TryGetValue(heroId, out Figure? figure) || !_tokens.TryGetValue(heroId, out TokenView? token))
        {
            return;
        }

        figure.Aloft = false;
        token.Crash();
        if (figure.RoomId == _shownRoomId)
        {
            _board.Feathers(token.Position + new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f - token.Lift), burst: true);
            _board.Callout(figure.Position, "WINGS BROKE!", Palette.Danger);
        }

        Sfx.Play("fall");
        await Pause(0.45);
        _board.Shake(4);
        Sfx.Play("block", 0.5f);
        await Pause(0.5);
    }

    /// <summary>Whether a block by this figure counts as a shield block: a hero with a shield in hand, or an armored monster.</summary>
    private bool Shielded(Figure figure) =>
        figure.IsHero
            ? _session.State.Heroes.FirstOrDefault(hero => hero.Id == figure.Id)?.EquippedCards.Any(card => card.Definition.Type == CardType.Shield) == true
            : figure.Monster is { Armor: > 0 };

    /// <summary>The sparks' color: the attacker's seat color or monster color; white for blasts and fire.</summary>
    private Color AttackerColor() =>
        _attackerId != 0 && _figures.TryGetValue(_attackerId, out Figure? attacker) ? attacker.Fill.Lightened(0.2f) : Palette.FireCore;
}
