using Godot;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The screen's own picture of every hero and monster. The rules state already shows the end of a
/// command; figures move to it one event at a time, so the board can animate each step.
/// </summary>
public partial class GameScreen
{
    private readonly Dictionary<int, Figure> _figures = [];
    private readonly Dictionary<int, TokenView> _tokens = [];
    private string _shownRoomId = "";

    private Room ShownRoom => _session.State.Rooms[_shownRoomId];

    /// <summary>Rebuilds every figure from the rules state.</summary>
    private void ReloadFigures()
    {
        _figures.Clear();
        foreach (Hero hero in _session.State.Heroes.Where(hero => hero.IsInPlay))
        {
            _names[hero.Id] = hero.Name;
            _figures[hero.Id] = new Figure
            {
                Id = hero.Id,
                IsHero = true,
                RoomId = hero.RoomId,
                Position = hero.Position,
                Hearts = hero.Hearts,
                MaxHearts = hero.MaxHearts,
                Glyph = hero.Name[..1].ToUpperInvariant(),
                Fill = Palette.Seat(hero.Seat),
                Look = hero.Guise is null ? Looks.Resolve(hero.Appearance, hero.Portrait) : null,
                Keeling = hero.IsKeelingOver,
                LifeMarks = hero.LifeMarks,
                DeathMarks = hero.DeathMarks,
                FireDice = hero.FireDice,
                StunMarks = hero.Stun.Stacks + 3 * hero.Stun.PowerfulStacks,
                Prone = hero.Prone,
                Diving = _session.IsDiving(hero),
                Chained = hero.Shackled is not null,
                Torch = hero.TorchesHeld > 0,
                Monster = hero.Guise is { } guise ? Content.GetMonster(guise) : null,
                Conditions = new Dictionary<Condition, int>(hero.Conditions.All),
                Shining = hero.ShiningRounds > 0,
                Shadowbound = hero.ShadowboundRounds > 0,
                Snowshoes = hero.SnowshoesRounds > 0,
                Aloft = hero.Aloft,
            };
        }

        foreach (Room room in _session.State.Rooms.Values)
        {
            foreach (Monster monster in room.Monsters)
            {
                _names[monster.Id] = monster.Definition.Name;
                _figures[monster.Id] = MonsterFigure(monster.Id, monster.Definition, room.Id, monster.Position, monster.Hearts, monster.MaxHearts);
                _figures[monster.Id].Frozen = monster.FrozenTurns > 0;
                _figures[monster.Id].Collapsed = monster.Collapsed;
                _figures[monster.Id].FireDice = monster.FireDice;
                _figures[monster.Id].Prone = monster.Prone;
                _figures[monster.Id].CarriesLoot = monster.HasLoot;
                _figures[monster.Id].StunMarks = monster.Stun.Stacks + 3 * monster.Stun.PowerfulStacks;
                _figures[monster.Id].Conditions = new Dictionary<Condition, int>(monster.Conditions.All);
            }
        }

        LoadMerchantFigures();
        RememberCardNames();
    }

    /// <summary>Points the board at a room and rebuilds its tokens from the figures in it.</summary>
    private void ShowRoom(string roomId)
    {
        bool changed = roomId != _shownRoomId;
        _shownRoomId = roomId;
        _board.ShowRoom(ShownRoom);
        foreach (TokenView token in _tokens.Values)
        {
            token.QueueFree();
        }

        _tokens.Clear();
        foreach (Figure figure in _figures.Values.Where(figure => figure.RoomId == roomId))
        {
            CreateToken(figure);
        }

        FollowActiveHero(snap: changed);

        _minimap.ShowFloor(_session.State, ShownRoom.Floor, roomId);
    }

    private TokenView CreateToken(Figure figure)
    {
        string theme = _session.State.ThemeId;
        MonsterDefinition? monster = figure.Monster;
        var token = new TokenView
        {
            EntityId = figure.Id,
            IsHero = figure.IsHero,
            Glyph = figure.Glyph,
            Fill = figure.Fill,
            Position = BoardView.TileOrigin(figure.Position),
            Body = monster is null ? null : GameArt.Monster(theme, monster.Id),
            Look = figure.Look,
            CollapsedBody = monster is { Reassembles: true } ? GameArt.Monster(theme, "bones") : null,
            Swimmer = monster is { Swimmer: true },
            SpriteScale = monster is null ? 1f : GameArt.MonsterScale(monster),
        };
        if (monster is { Size: > 1 })
        {
            // A huge monster (the Bridge Troll) fills a square of tiles: its token is drawn that much bigger.
            token.Scale = new Vector2(monster.Size, monster.Size);
        }

        _tokens[figure.Id] = token;
        _board.AddChild(token);
        ApplyFigure(figure, token);
        return token;
    }

    private void ApplyFigure(Figure figure, TokenView token)
    {
        token.SetHearts(figure.Hearts, figure.MaxHearts);
        token.Frozen = figure.Frozen;
        token.Collapsed = figure.Collapsed;
        token.Keeling = figure.Keeling ? (figure.LifeMarks, figure.DeathMarks) : null;
        token.FireDice = figure.FireDice;
        token.StunMarks = figure.StunMarks;
        token.CarriesLoot = figure.CarriesLoot;
        token.Torch = figure.Torch;
        token.Chained = figure.Chained;
        token.Prone = figure.Prone;
        token.Diving = figure.Diving;
        token.Conditions = figure.Conditions;
        token.Shining = figure.Shining;
        token.Shadowbound = figure.Shadowbound;
        token.Snowshoes = figure.Snowshoes;
        token.Aloft = figure.Aloft;
        token.Active = figure.Id == TurnOnShow?.Id;
        // The active hero (and the arrow over their head) is drawn over their neighbors.
        token.ZIndex = figure.Id == TurnOnShow?.Id ? 1 : 0;
    }

    private string? FigureRoom(int entityId) => _figures.TryGetValue(entityId, out Figure? figure) ? figure.RoomId : null;

    private async Task Walk(int entityId, IReadOnlyList<GridPosition> path)
    {
        if (path.Count == 0 || !_figures.TryGetValue(entityId, out Figure? figure))
        {
            return;
        }

        figure.Position = path[^1];
        if (!_tokens.TryGetValue(entityId, out TokenView? token))
        {
            return;
        }

        Tween tween = CreateTween();
        foreach (GridPosition step in path)
        {
            tween.TweenCallback(Callable.From(() => Sfx.PlayVaried("step")));
            tween.TweenProperty(token, "position", BoardView.TileOrigin(step), StepSeconds);
        }

        await ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>A hero went through a door or down the stairs: follow them into the new room.</summary>
    private async Task ChangeRoom(int heroId, string roomId, GridPosition position)
    {
        if (_figures.TryGetValue(heroId, out Figure? figure))
        {
            figure.RoomId = roomId;
            figure.Position = position;
        }

        ShowRoom(roomId);
        await Pause(0.2);
    }

    private async Task Lunge(int attackerId, int targetId)
    {
        if (!_tokens.TryGetValue(attackerId, out TokenView? attacker) || !_tokens.TryGetValue(targetId, out TokenView? target))
        {
            return;
        }

        Vector2 home = attacker.Position;
        Vector2 toward = home + (target.Position - home).Normalized() * 7;
        Tween tween = CreateTween();
        tween.TweenProperty(attacker, "position", toward, 0.07);
        tween.TweenProperty(attacker, "position", home, 0.09);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task ShowHealing(HeroHealed healed)
    {
        if (!_figures.TryGetValue(healed.HeroId, out Figure? figure))
        {
            return;
        }

        figure.Hearts = healed.Hearts;
        if (_tokens.TryGetValue(healed.HeroId, out TokenView? token))
        {
            token.SetHearts(healed.Hearts);
            _board.FloatText(figure.Position, $"+{healed.Amount}", Palette.Heal);
            await Pause(0.3);
        }
    }

    /// <summary>
    /// Points the camera at the active hero (when they're in the room on show), and lets the player pan
    /// only as far as keeps them in view on their own turn.
    /// </summary>
    private void FollowActiveHero(bool snap = false)
    {
        Hero? hero = ActiveHero;
        TokenView? token = hero is not null && _tokens.TryGetValue(hero.Id, out TokenView? found) ? found : null;
        _board.Follow(token, snap);
        _board.SetPanAnchor(token is not null && hero is { IsCompanion: false } && !_autoplay ? hero!.Position : null);
    }

    /// <summary>Points the camera at a figure, if it's on the board.</summary>
    private void FocusOn(int entityId)
    {
        if (_tokens.TryGetValue(entityId, out TokenView? token))
        {
            _board.Follow(token);
        }
    }

    /// <summary>A prone hero who crawled into or out of deep water starts or stops diving.</summary>
    private void SyncDiving(int heroId)
    {
        if (_figures.TryGetValue(heroId, out Figure? figure) && figure.Prone && _tokens.TryGetValue(heroId, out TokenView? token))
        {
            figure.Diving = _session.IsDiving(_session.State.GetHero(heroId));
            ApplyFigure(figure, token);
        }
    }

    /// <summary>Floats text over a figure, if it's in the room on show.</summary>
    private void FloatOver(int entityId, string text, Color color)
    {
        if (_figures.TryGetValue(entityId, out Figure? figure) && figure.RoomId == _shownRoomId)
        {
            _board.FloatText(figure.Position, text, color);
        }
    }

    /// <summary>
    /// A Keel roll in the tray: the Keel Die with its Power Die (after a 6) and any banked Power Dice,
    /// then on a second line the Doom Dice: the one a 1 rolls and those allies left.
    /// </summary>
    private async Task ShowKeelRoll(KeelRolled keel)
    {
        _trayShowsMovement = false;
        bool six = keel.Keel == GameRules.PowerDieFace;
        List<int> keelFaces = [keel.Keel, .. six && keel.BonusDie is int power ? [power] : Array.Empty<int>(), .. keel.PowerFaces];
        await _dice.ShowRoll($"{NameOf(keel.HeroId)} rolls the Keel Die", new DiceRoll(keelFaces), 0, newGroup: true, keelFaces.Count - 1);

        List<int> doomFaces = [.. !six && keel.BonusDie is int doom ? [doom] : Array.Empty<int>(), .. keel.DoomFaces];
        if (doomFaces.Count > 0)
        {
            await _dice.ShowRoll("Doom Dice (1s are death)", new DiceRoll(doomFaces), 0, newGroup: false);
        }

        if (keel.LifeGained > 0)
        {
            FloatOver(keel.HeroId, $"+{keel.LifeGained} life", Palette.Heal);
        }
        else if (keel.DeathGained > 0)
        {
            FloatOver(keel.HeroId, $"+{keel.DeathGained} death", Palette.Danger);
        }
        else
        {
            FloatOver(keel.HeroId, "nothing...", Palette.TextDim);
        }

        await UpdateFigure(keel.HeroId, figure => { figure.LifeMarks = keel.LifeMarks; figure.DeathMarks = keel.DeathMarks; }, 0.3);
    }

    private async Task RemoveFigure(int entityId)
    {
        _figures.Remove(entityId);
        if (!_tokens.Remove(entityId, out TokenView? token))
        {
            return;
        }

        Tween tween = CreateTween();
        tween.TweenProperty(token, "modulate:a", 0.0, 0.3);
        await ToSignal(tween, Tween.SignalName.Finished);
        token.QueueFree();
    }

    private async Task UpdateFigure(int entityId, Action<Figure> change, double pause)
    {
        if (!_figures.TryGetValue(entityId, out Figure? figure))
        {
            return;
        }

        change(figure);
        if (_tokens.TryGetValue(entityId, out TokenView? token))
        {
            ApplyFigure(figure, token);
            await Tint(token, Palette.TrimBright);
            await Pause(pause);
        }
    }

    private static void SetCondition(Figure figure, Condition condition, int count)
    {
        if (count == 0)
        {
            figure.Conditions.Remove(condition);
        }
        else
        {
            figure.Conditions[condition] = count;
        }
    }

    /// <summary>A token came or went: called out when it lands, noted when it clears.</summary>
    private async Task ShowCondition(ConditionChanged changed)
    {
        int before = _figures.TryGetValue(changed.EntityId, out Figure? marked) ? marked.Conditions.GetValueOrDefault(changed.Condition) : 0;
        if (changed.Count > before)
        {
            CalloutOver(changed.EntityId, DieTypes.Callout(changed.Condition), DieTypes.Tint(changed.Condition));
        }
        else if (changed.Count == 0)
        {
            FloatOver(changed.EntityId, changed.Condition switch
            {
                Condition.Freeze => "thaws",
                Condition.Buried => "digs free",
                Condition.Blind => "sees again",
                Condition.BlackHeart => "cured",
                Condition.Poison => "poison gone",
                Condition.Shine => "glow fades",
                Condition.Confused => "comes to",
                Condition.Corroded => "armor mended",
                _ => "spent",
            }, Palette.Text);
        }

        await UpdateFigure(changed.EntityId, figure => SetCondition(figure, changed.Condition, changed.Count), changed.Count > before ? 0.25 : 0.1);
        RefreshPanels();
    }

    /// <summary>A zombie or Sentient Shrub joins the party mid-fight: it gets a figure, looking like its monster.</summary>
    private async Task SummonFigure(HeroSummoned summoned)
    {
        Hero hero = _session.State.GetHero(summoned.HeroId);
        _names[hero.Id] = hero.Name;
        MonsterDefinition guise = Content.GetMonster(summoned.Guise);
        var figure = new Figure
        {
            Id = hero.Id,
            IsHero = true,
            Monster = guise,
            RoomId = summoned.RoomId,
            Position = summoned.Position,
            Hearts = hero.MaxHearts,
            MaxHearts = hero.MaxHearts,
            Glyph = char.ToUpperInvariant(guise.Glyph).ToString(),
            Fill = Palette.Seat(hero.Seat),
        };
        _figures[figure.Id] = figure;
        if (summoned.RoomId == _shownRoomId)
        {
            TokenView token = CreateToken(figure);
            token.Modulate = Colors.Transparent;
            CreateTween().TweenProperty(token, "modulate", Colors.White, 0.3);
            CalloutOver(hero.Id, guise.Id == "sentient_shrub" ? "SPROUTS!" : "RISES!", Palette.Heal);
            await Pause(0.4);
        }
    }

    private async Task SpawnFigure(MonsterSpawned spawned)
    {
        var definition = Content.GetMonster(spawned.MonsterDefinitionId);
        Figure figure = MonsterFigure(spawned.MonsterId, definition, spawned.RoomId, spawned.Position, spawned.Hearts, spawned.Hearts);
        _figures[figure.Id] = figure;
        if (spawned.RoomId == _shownRoomId)
        {
            TokenView token = CreateToken(figure);
            token.Modulate = Colors.Transparent;
            CreateTween().TweenProperty(token, "modulate", Colors.White, 0.2);
            await Pause(0.15);
        }
    }

    private async Task Tint(CanvasItem item, Color color)
    {
        Tween tween = CreateTween();
        tween.TweenProperty(item, "modulate", color, 0.06);
        tween.TweenProperty(item, "modulate", Colors.White, 0.2);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private static Figure MonsterFigure(int id, MonsterDefinition definition, string roomId, GridPosition position, int hearts, int maxHearts) => new()
    {
        Id = id,
        IsHero = false,
        Monster = definition,
        RoomId = roomId,
        Position = position,
        Hearts = hearts,
        MaxHearts = maxHearts,
        Glyph = char.ToUpperInvariant(definition.Glyph).ToString(),
        Fill = Palette.Monster(definition.Id),
    };

    /// <summary>A hero or monster as the screen currently shows it.</summary>
    private sealed class Figure
    {
        public int Id { get; init; }

        public bool IsHero { get; init; }

        /// <summary>Null for heroes, but for a summoned one (a zombie, a Sentient Shrub): the monster it looks like.</summary>
        public MonsterDefinition? Monster { get; init; }

        public string RoomId { get; set; } = "";

        public GridPosition Position { get; set; }

        public int Hearts { get; set; }

        public int MaxHearts { get; init; }

        public string Glyph { get; init; } = "";

        public Color Fill { get; init; }

        public LookColors? Look { get; init; }

        public bool Frozen { get; set; }

        public bool Collapsed { get; set; }

        public bool Keeling { get; set; }

        public int LifeMarks { get; set; }

        public int DeathMarks { get; set; }

        public int FireDice { get; set; }

        public int StunMarks { get; set; }

        public bool CarriesLoot { get; set; }

        public bool Torch { get; set; }

        /// <summary>A merchant still chained to the wall.</summary>
        public bool Chained { get; set; }

        public bool Prone { get; set; }

        public bool Diving { get; set; }

        /// <summary>Tokens typed dice left on it (poison, freeze...), and how many of each.</summary>
        public Dictionary<Condition, int> Conditions { get; set; } = [];

        public bool Shining { get; set; }

        public bool Shadowbound { get; set; }

        public bool Snowshoes { get; set; }

        /// <summary>Up on the Icarus Wings.</summary>
        public bool Aloft { get; set; }
    }
}
