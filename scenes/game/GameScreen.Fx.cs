using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
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

    /// <summary>A Luck die came up 6 on the attack being resolved: LUCKY goes up over its HIT! or MISS.</summary>
    private bool _lucky;

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
        _lucky = false;
    }

    /// <summary>
    /// Which roll to ask "Try your Luck?" on, if any: a waiting roll the hero's Luck menu asks on (about to
    /// fail, or a search) or any roll while the clover glows; a fresh Wend roll while Wend is switched on
    /// (or the clover glows). A waiting roll that isn't asked on is let stand at once. Also whether it's
    /// about to fail (5 seconds to decide, rather than 3).
    /// </summary>
    private (object? Roll, bool Failing) LuckToAsk(Hero? hero, TurnState? turn, bool idle, bool pending)
    {
        if (!idle || hero is null || turn is null)
        {
            return (null, false);
        }

        // A Focus check (learning a spell) always asks while there's Luck or an Od Hand die to add.
        if (pending && turn.PendingRoll is { Check.Kind: OdHandCheckKind.Learn } focus && (hero.LuckLeft > 0 || _session.OdHandDiceLeft > 0 || hero.Power.Banked > 0))
        {
            return (focus, false);
        }

        if (hero.LuckLeft < 1)
        {
            return (null, false);
        }

        bool primed = _luckPrimed.Contains(hero.Id);
        if (pending && turn.PendingRoll is { } roll)
        {
            bool failing = _session.PendingRollFails;
            bool ask = primed || (_session.PendingLuckKind is { } kind && hero.LuckAsks.HasFlag(kind) && (failing || kind == LuckRoll.Search));
            if (ask)
            {
                return (roll, failing);
            }

            // Not asked on: it stands, once (tried again next time if the screen was busy).
            if (_letStand != roll)
            {
                _letStand = roll;
                int heroId = hero.Id;
                Callable.From(async () =>
                {
                    if (!await ExecuteAsync(new ConfirmAttack(heroId)))
                    {
                        _letStand = null;
                    }
                }).CallDeferred();
            }

            return (null, false);
        }

        return _trayShowsMovement && turn.MovementRoll is { } wend && !turn.HasMoved && wend != _wendLuckDone && (primed || hero.LuckAsks.HasFlag(LuckRoll.Wend))
            ? (wend, false)
            : (null, false);
    }

    /// <summary>
    /// Opens (or keeps) "Try your Luck?" for <paramref name="roll"/>, or puts it away: a fresh countdown for
    /// each new roll and after each try, 5 seconds on a roll about to fail and 3 otherwise. Asking uses up the
    /// clover's glow. When the time's up (or No), a waiting roll stands as it is, and a Wend roll's dice
    /// can't be rerolled any more.
    /// </summary>
    private void OfferLuck(object? roll, int luck, bool pending, bool failing)
    {
        (object, int)? offer = roll is null ? null : (roll, luck);
        if (Equals(offer, _luckOffer))
        {
            return;
        }

        _luckOffer = offer;
        if (roll is null)
        {
            _luckPrompt.Close();
            SlowForLuck(false);
            return;
        }

        // While Luck is asked, everything slows down, and on a roll that waits (an attack, a check) the view
        // zooms in on the action. A Wend roll keeps the whole room in view, to click where to go.
        if (ActiveHero is { } hero)
        {
            if (pending)
            {
                ZoomOnAction(hero.Id, _session.State.Turn?.PendingRoll?.TargetIds ?? []);
            }

            _luckPrimed.Remove(hero.Id);
            _heroPanel.LuckClover.Glowing = false;
        }

        SlowForLuck(true);
        bool focusCheck = roll is PendingRoll { Check.Kind: OdHandCheckKind.Learn };
        _luckPrompt.Offer(failing ? GameRules.LuckSeconds : GameRules.WendLuckSeconds, () =>
        {
            _luckOffer = null;
            SlowForLuck(false);
            if (pending)
            {
                ExecuteForActiveHero(heroId => new ConfirmAttack(heroId));
            }
            else
            {
                _wendLuckDone = roll;
                RefreshPanels();
            }
        }, focusCheck ? "Focus: more?" : "Try your Luck?", focusCheck ? () => ExecuteForActiveHero(heroId => new AddOdHandDie(heroId)) : null, focusCheck && _session.OdHandDiceLeft > 0,
            focusCheck ? () => ExecuteForActiveHero(heroId => new AddOdHandDie(heroId, Power: true)) : null, focusCheck && ActiveHero?.Power.Banked > 0);
    }

    /// <summary>
    /// A save after a fall waiting on a hero (one of the players'): "Keep your light?" with 3 seconds, zoomed in on
    /// them and slowed down, to add unspent Od Hand dice (+Od Hand) or try their Luck on a die; No, or the time
    /// running out, lets it stand. Null puts it away.
    /// </summary>
    private void OfferSave(Hero? saver)
    {
        if (saver?.PendingSave is not { } save)
        {
            if (_saveOffer is not null)
            {
                _saveOffer = null;
                _luckPrompt.Close();
                SlowForLuck(false);
            }

            return;
        }

        (object, int)? offer = (save, save.Total + 100 * saver.LuckLeft);
        _luckPrompt.CanAdd(_session.OdHandUnspent(saver) > 0, saver.Power.Banked > 0);
        if (Equals(offer, _saveOffer))
        {
            return;
        }

        _saveOffer = offer;
        _luckOffer = null;
        ZoomOnAction(saver.Id, []);
        SlowForLuck(true);
        int saverId = saver.Id;
        _luckPrompt.Offer(GameRules.WendLuckSeconds, () =>
        {
            _saveOffer = null;
            SlowForLuck(false);
            Execute(new LetSaveStand(saverId));
        }, $"Keep the light? {save.Total}/{save.Difficulty}", () => Execute(new BoostFallSave(saverId)), _session.OdHandUnspent(saver) > 0,
            () => Execute(new BoostFallSave(saverId, Power: true)), saver.Power.Banked > 0);
    }

    /// <summary>The save after a fall on offer, and its total and Luck then (a change starts the countdown afresh).</summary>
    private (object Save, int Total)? _saveOffer;

    /// <summary>A player's hero with a save after a fall waiting on them, if any.</summary>
    private Hero? Saver => _busy ? null : _session.State.Heroes.FirstOrDefault(hero => hero.PendingSave is not null && !hero.IsCompanion);

    /// <summary>How fast everything plays while Luck is asked: half speed.</summary>
    private const double LuckTimeScale = 0.5;

    /// <summary>Slows everything down while Luck is asked (the countdown runs on real seconds), and back up after, the view zooming back out.</summary>
    private void SlowForLuck(bool slow)
    {
        Engine.TimeScale = slow ? LuckTimeScale : 1;
        if (!slow && !_busy)
        {
            _board.ZoomOut();
        }
    }

    /// <summary>
    /// Zooms the view in on an attack, afresh: zoomed in on the last one still (a monster's turn plays all
    /// its attacks in a row), it first eases back out, so each attack gets its own zoom in.
    /// </summary>
    private async Task ZoomOnActionAfresh(int actorId, IEnumerable<int> targetIds)
    {
        if (_board.ActionZoomLevel > 1.01f)
        {
            _board.ZoomOut();
            for (int frame = 0; frame < 40 && _board.ActionZoomLevel > 1.08f; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        ZoomOnAction(actorId, targetIds);
    }

    /// <summary>Zooms the view in on an attack (or a roll): the attacker and its targets, those in the room on show.</summary>
    private void ZoomOnAction(int actorId, IEnumerable<int> targetIds)
    {
        List<GridPosition> tiles = [.. new[] { actorId }.Concat(targetIds)
            .Select(id => _figures.TryGetValue(id, out Figure? figure) && figure.RoomId == _shownRoomId ? figure.Position : (GridPosition?)null)
            .OfType<GridPosition>()];
        _board.ZoomOn(tiles);
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

            CalloutLucky(figure);
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
        CalloutLucky(figure);
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

    /// <summary>LUCKY in green over the HIT! or MISS, when a lucky 6 went into the attack.</summary>
    private void CalloutLucky(Figure figure)
    {
        if (_lucky)
        {
            _board.Callout(figure.Position, "LUCKY", Palette.Heal, 1, lift: 16);
        }
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
