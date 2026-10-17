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

    /// <summary>Whether a block by this figure counts as a shield block: a hero with a shield in hand, or an armored monster.</summary>
    private bool Shielded(Figure figure) =>
        figure.IsHero
            ? _session.State.Heroes.FirstOrDefault(hero => hero.Id == figure.Id)?.EquippedCards.Any(card => card.Definition.Type == CardType.Shield) == true
            : figure.Monster is { Armor: > 0 };

    /// <summary>The sparks' color: the attacker's seat color or monster color; white for blasts and fire.</summary>
    private Color AttackerColor() =>
        _attackerId != 0 && _figures.TryGetValue(_attackerId, out Figure? attacker) ? attacker.Fill.Lightened(0.2f) : Palette.FireCore;
}
