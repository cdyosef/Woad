using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Casting: the Cast button opens the spells the hero knows; picking one aims it (its range shaded, who it
/// can reach in red) and a click on a target casts it. The spell's cost comes out of the bank by itself;
/// each banked Power Die selected goes in on top as an extra die, up to the Magic rank. Swirly Eyes then asks where to move its target.
/// </summary>
public partial class GameScreen
{
    private Button _cast = null!;
    private SpellPicker _spellPicker = null!;

    /// <summary>The spell being aimed, by id, or null.</summary>
    private string? _casting;

    /// <summary>Swirly Eyes' target, while picking where to move it.</summary>
    private GridPosition? _swirling;

    /// <summary>The caption for the next attack roll's dice, when it's a spell's.</summary>
    private string? _spellCaption;

    /// <summary>Laying an Icicle Arrow's ice, a click a patch (until it's all laid, or the player stops).</summary>
    private bool _layingIce;

    private bool LayingIce => _layingIce && !Locked && _session.State.Turn is { IceToLay: > 0 };

    /// <summary>A click while laying ice: on a tile it can go, a patch (or Snowshoes); anywhere else, stop.</summary>
    private void LayIceAt(Hero hero, GridPosition tile)
    {
        if (_session.GetIceTargets().Contains(tile))
        {
            Execute(new LayIce(hero.Id, tile));
        }
        else
        {
            CancelSelection();
        }
    }

    private void BuildCasting(Control right)
    {
        // Casting is the Magic card's (top right of the sheet); this button only keeps its state.
        _cast = UiTheme.Accent(UiTheme.Button(Vector2.Zero, new Vector2(52, 22), "Cast"), new Color("b07ce8"));
        _cast.Name = "Cast";
        _cast.Pressed += OpenSpells;
        _heroPanel.MagicClicked += OpenSpells;
        _spellPicker = new SpellPicker { Position = BoardOrigin + new Vector2((BoardView.Pixels - 248) / 2, 40) };
        _spellPicker.Picked += BeginCasting;
        AddChild(_spellPicker);
    }

    /// <summary>The Magic card on the sheet: lit for a hero who can cast a spell now.</summary>
    private void UpdateCastButton(Hero? hero, bool ready)
    {
        LayoutEndTurnRow();
        List<string?> errors = hero is null ? [] : [.. hero.Spells.Select(spell => _session.CheckCastSpell(spell.Id))];
        string? blocked = errors.Count == 0 ? "" : errors.All(error => error is not null) ? errors[0] : null;
        _cast.Disabled = !ready || blocked is not null || hero is null || hero.Spells.Count == 0 || (_session.State.Turn?.KeelingOver ?? false);
        _cast.TooltipText = blocked ?? "Cast a spell you know: it costs banked Power Dice and your Rumble dice. Select more Power Dice to put more into it (a die each, up to your Magic).";
        _heroPanel.ShowMagic(hero is not null && (hero.EffectiveStats.Magic > 0 || hero.Spells.Count > 0), !_cast.Disabled, _cast.TooltipText);
    }

    /// <summary>The bottom of the sheet: Orders on the left while it shows, End turn filling the rest.</summary>
    private void LayoutEndTurnRow()
    {
        float left = _orders.Visible ? 60 : 6;
        _endTurn.Position = new Vector2(left, 335);
        _endTurn.Size = new Vector2(170 - left, 20);
    }

    private void OpenSpells()
    {
        if (Locked || ActiveHero is not { } hero)
        {
            return;
        }

        CancelSelection();
        _spellPicker.Open("CAST", hero.Spells.Select(spell => (spell, $"{spell.Name} ({spell.Cost})", _session.CheckCastSpell(spell.Id))));
    }

    private void BeginCasting(string spellId)
    {
        if (ActiveHero is not { } hero)
        {
            return;
        }

        SpellDefinition spell = _session.Content.GetSpell(spellId);
        if (spell.Shape == SpellShape.Around)
        {
            Execute(new CastSpell(hero.Id, spellId, ExtraPower: ExtraPower(spell)));
            return;
        }

        _casting = spellId;
        _rumbling = _stealing = _shoving = _leaping = false;
        Log($"{spell.Name}: click a target in red. Select banked Power Dice to put more in (a die each). Right-click to cancel.", DieTypes.Tint(spell.DieType));
        RefreshPanels();
    }

    /// <summary>The Power Dice selected, to go in on top of the spell's cost: a die each, up to the Magic rank and what's banked besides the cost.</summary>
    private int ExtraPower(SpellDefinition spell) =>
        Math.Clamp(SelectedPowerDice, 0, Math.Max(0, Math.Min(ActiveHero?.EffectiveStats.Magic ?? 0, (ActiveHero?.Power.Banked ?? 0) - spell.Cost)));

    /// <summary>A click while aiming a spell: at a target it casts (Swirly Eyes first asks where to move it); anywhere else it stops aiming.</summary>
    private void CastAt(Hero hero, GridPosition tile)
    {
        SpellDefinition spell = _session.Content.GetSpell(_casting!);
        if (_swirling is { } target)
        {
            if (tile == target || SwirlDestinations(spell, target).Contains(tile))
            {
                Cast(hero, spell, target, tile == target ? null : tile);
            }
            else
            {
                CancelSelection();
            }

            return;
        }

        if (!_session.GetSpellTargets(spell.Id).Contains(tile))
        {
            CancelSelection();
            return;
        }

        if (spell.Swirl && ShownRoom.MonsterAt(tile) is { } monster && monster.Tiles.Count() == 1)
        {
            _swirling = tile;
            Log("Now click where it goes if the spell hits (or on it again to leave it be).", DieTypes.Tint(spell.DieType));
            RefreshPanels();
            return;
        }

        Cast(hero, spell, tile, null);
    }

    private void Cast(Hero hero, SpellDefinition spell, GridPosition target, GridPosition? moveTo)
    {
        int extra = ExtraPower(spell);
        _casting = null;
        _swirling = null;
        Execute(new CastSpell(hero.Id, spell.Id, target, extra, moveTo));
    }

    private List<GridPosition> SwirlDestinations(SpellDefinition spell, GridPosition target) =>
        [.. target.SurroundingTiles().Where(tile => _session.CheckCastSpell(spell.Id, target, ExtraPower(spell), tile) is null)];

    /// <summary>While aiming a spell: its range shaded and who it reaches in red, or where Swirly Eyes can move its target.</summary>
    private bool ShowCastingHighlights()
    {
        if (_casting is not { } spellId)
        {
            return false;
        }

        if (_swirling is { } target)
        {
            _board.SetHighlights([], [target], SwirlDestinations(_session.Content.GetSpell(spellId), target), []);
        }
        else
        {
            _board.SetHighlights([], _session.GetSpellTargets(spellId), [], [], range: _session.GetSpellRange(spellId));
        }

        return true;
    }

    private void StopCasting()
    {
        _casting = null;
        _swirling = null;
        _layingIce = false;
        _spellPicker.Visible = false;
    }
}
