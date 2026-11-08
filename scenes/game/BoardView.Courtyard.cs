using Godot;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The Emerald Keep's courtyard (see <see cref="KeepCourtyard"/>), drawn outdoors: grass, trees, the road
/// and the drawbridge over the moat, a stone apron at the foot of the keep; and the keep itself rising up
/// off the top of the room (the view can pan up to see it), green-grey stone in courses, corner towers,
/// battlements, banners, and the great door in the middle, iron-bound and shut until the Emerald Key opens it.
/// </summary>
public partial class BoardView
{
    /// <summary>How many tiles of the keep's front wall show above the courtyard.</summary>
    private const int FacadeTiles = 6;

    private bool IsCourtyard => _room?.Id == KeepCourtyard.Id;

    /// <summary>The room shows this far above its top edge (the keep's wall, over the courtyard), in board pixels.</summary>
    private float TopMargin => IsCourtyard ? FacadeTiles * TileSize : 0;

    private static readonly Color GrassDark = new("3d7330"), GrassMid = new("4f8a3a"), GrassLight = new("6aa84a"), GrassTip = new("8cc860");
    private static readonly Color Dirt = new("8a6a44"), DirtDark = new("6a4e30"), DirtLight = new("a88a60");
    private static readonly Color Flag = new("74827a"), FlagDark = new("4c5850"), FlagLight = new("98a69c"), Moss = new("4f7a3e");
    private static readonly Color Plank = new("8a5a30"), PlankDark = new("4a2e18"), PlankLight = new("a8743e");
    private static readonly Color KeepStone = new("55695c"), KeepMortar = new("33443a"), KeepLight = new("7d9384"), KeepShadow = new("26332b");
    private static readonly Color Emerald = new("3fd98a"), EmeraldDark = new("1a6a3c"), Banner = new("2a7a46"), BannerDark = new("174a2a"), Gold = new("e2c47a");

    /// <summary>A steady pseudo-random number in [0, 1) for a tile and a salt: the same every frame.</summary>
    private static float Speck(GridPosition tile, int salt)
    {
        uint h = (uint)(tile.X * 73856093) ^ (uint)(tile.Y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65536f;
    }

    /// <summary>
    /// A body floating belly-up over the water (Leviathan Jr.'s): the water under it, then its figure upside down,
    /// stretched along its tiles, its eyes X'd out.
    /// </summary>
    private void DrawCarcass(Carcass carcass)
    {
        foreach (GridPosition tile in carcass.Tiles)
        {
            DrawTexturedTile(tile, TileKind.DeepWater);
        }

        Rect2 first = TileRect(carcass.Tiles[0]), last = TileRect(carcass.Tiles[^1]);
        Rect2 span = first.Merge(last).Grow(-2);
        if (GameArt.Monster(ThemeId, carcass.MonsterId) is { } sprite)
        {
            // Upside down: drawn flipped top to bottom.
            DrawSetTransform(new Vector2(0, span.Position.Y * 2 + span.Size.Y), 0, new Vector2(1, -1));
            DrawTextureRect(sprite, span, tile: false, modulate: new Color(0.85f, 0.9f, 0.85f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            DrawRect(span, new Color("3a8a6a"));
        }

        // Eyes X'd: two little crosses near its (now lower) head.
        float y = span.End.Y - span.Size.Y * 0.32f;
        foreach (float fraction in new[] { 0.38f, 0.62f })
        {
            var eye = new Vector2(span.Position.X + span.Size.X * fraction, y);
            DrawLine(eye - new Vector2(2.5f, 2.5f), eye + new Vector2(2.5f, 2.5f), Palette.Background, 1.5f);
            DrawLine(eye + new Vector2(-2.5f, 2.5f), eye + new Vector2(2.5f, -2.5f), Palette.Background, 1.5f);
        }
    }

    /// <summary>A wall sconce: an iron bracket on the room's edge over the tile, a torch burning in it (or empty).</summary>
    private void DrawSconce(GridPosition tile, bool lit)
    {
        Rect2 rect = TileRect(tile);
        RoomLayout layout = _room!.Layout;
        Vector2 wall = tile.Y == 0 ? new Vector2(rect.GetCenter().X, rect.Position.Y + 3)
            : tile.Y == layout.Height - 1 ? new Vector2(rect.GetCenter().X, rect.End.Y - 3)
            : tile.X == 0 ? new Vector2(rect.Position.X + 3, rect.GetCenter().Y)
            : new Vector2(rect.End.X - 3, rect.GetCenter().Y);
        Vector2 inward = (rect.GetCenter() - wall).Normalized();
        Color iron = new("3a3a44"), ironLight = new("8a8a96");
        Vector2 cup = wall + inward * 6;
        DrawLine(wall, cup, iron, 2);
        DrawRect(new Rect2(cup - new Vector2(3, 2), new Vector2(6, 4)), iron);
        DrawLine(cup - new Vector2(3, 2), cup + new Vector2(3, -2), ironLight, 1);
        if (!lit)
        {
            return;
        }

        float time = Time.GetTicksMsec() / 1000f;
        DrawCircle(cup, 12, Palette.FireCore with { A = 0.1f + 0.04f * Mathf.Sin(time * 6 + tile.X) }, antialiased: true);
        DrawRect(new Rect2(cup - new Vector2(1, 6), new Vector2(2, 5)), new Color("6a4424"));
        float height = 7 + 2 * Mathf.Sin(time * 9 + tile.Y);
        Vector2 foot = cup - new Vector2(0, 6);
        DrawColoredPolygon([foot + new Vector2(-3, 0), foot + new Vector2(3, 0), foot + new Vector2(0, -height)], Palette.Fire);
        DrawColoredPolygon([foot + new Vector2(-1.5f, 0), foot + new Vector2(1.5f, 0), foot + new Vector2(0, -height * 0.55f)], Palette.FireCore);
    }

    /// <summary>A wooden crate: planks, a cross-brace, iron corners.</summary>
    private void DrawCrate(GridPosition tile)
    {
        Rect2 rect = TileRect(tile).Grow(-4);
        Color wood = new("9a6a3a"), dark = new("5a3a1e"), light = new("c08a50"), iron = new("4a4a52");
        DrawRect(new Rect2(rect.Position + new Vector2(2, 3), rect.Size), new Color(0, 0, 0, 0.3f));
        DrawRect(rect, wood);
        for (float y = rect.Position.Y + 6; y < rect.End.Y; y += 6)
        {
            DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.End.X, y), dark, 1);
        }

        DrawLine(rect.Position + new Vector2(2, 2), rect.End - new Vector2(2, 2), light, 2);
        DrawRect(rect, dark, filled: false, width: 2);
        foreach (Vector2 corner in new[] { rect.Position, new Vector2(rect.End.X - 4, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y - 4), rect.End - new Vector2(4, 4) })
        {
            DrawRect(new Rect2(corner, new Vector2(4, 4)), iron);
        }
    }

    /// <summary>Break marks: a jagged crack for each, spreading across the tile.</summary>
    private void DrawBreakMarks(Rect2 rect, int marks)
    {
        Color crack = new(0.05f, 0.04f, 0.04f, 0.85f);
        Vector2 c = rect.GetCenter();
        for (int i = 0; i < Math.Min(marks, 10); i++)
        {
            float angle = i * 2.39996f;
            Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 side = new(-dir.Y, dir.X);
            float reach = 6 + i % 3 * 3;
            DrawPolyline([c + dir * 2, c + dir * (reach * 0.5f) + side * 2, c + dir * reach - side], crack, 1.5f);
        }
    }

    /// <summary>An unstable portal: a broken ring of stone round a violet swirl, lightning crackling off it.</summary>
    private void DrawRift(GridPosition tile)
    {
        Rect2 rect = TileRect(tile);
        Vector2 center = rect.GetCenter();
        float time = Time.GetTicksMsec() / 1000f;
        Color violet = new("8a5cff"), bolt = new("e8f4ff"), spark = new("6ad8ff");
        DrawCircle(center, 13, new Color(0, 0, 0, 0.35f), antialiased: true);
        DrawCircle(center, 11 + Mathf.Sin(time * 7) * 1.5f, violet with { A = 0.45f }, antialiased: true);
        DrawCircle(center, 7, violet with { A = 0.85f }, antialiased: true);
        for (int arm = 0; arm < 3; arm++)
        {
            float start = -time * 4 + arm * Mathf.Tau / 3;
            DrawArc(center, 5, start, start + 2f, 10, bolt, 1.5f, antialiased: true);
        }

        // The ring, cracked: three stones of it, with gaps.
        for (int stone = 0; stone < 6; stone++)
        {
            float from = stone * Mathf.Tau / 6 + 0.15f, to = from + Mathf.Tau / 6 - 0.45f;
            DrawArc(center, 12, from, to, 6, Palette.Wall, 4, antialiased: true);
        }

        // Lightning: a jagged bolt or two, flickering.
        int flicker = (int)(time * 12) + tile.X * 7 + tile.Y * 3;
        for (int i = 0; i < 2; i++)
        {
            if ((flicker + i) % 3 == 0)
            {
                continue;
            }

            float angle = (flicker * 1.7f + i * 2.4f) % Mathf.Tau;
            Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 side = new(-dir.Y, dir.X);
            Vector2 a = center + dir * 9, b = center + dir * 13 + side * 3, c = center + dir * 17 - side * 2;
            DrawPolyline([a, b, c], i == 0 ? bolt : spark, 1.5f);
        }
    }

    /// <summary>One courtyard tile, by what it looks like.</summary>
    private void DrawCourtyardTile(GridPosition tile)
    {
        Rect2 rect = TileRect(tile);
        CourtyardGround ground = KeepCourtyard.GroundAt(tile);
        switch (ground)
        {
            case CourtyardGround.Moat:
                DrawTexturedTile(tile, TileKind.DeepWater);
                DrawMoatBanks(tile, rect);
                break;

            case CourtyardGround.Bridge:
                DrawTexturedTile(tile, TileKind.DeepWater);
                DrawBridge(tile, rect);
                break;

            case CourtyardGround.Flagstone:
                DrawFlagstones(tile, rect);
                break;

            case CourtyardGround.Road or CourtyardGround.WayOut:
                DrawGrass(tile, rect);
                DrawRoad(tile, rect, ground == CourtyardGround.WayOut);
                break;

            case CourtyardGround.Tree:
                DrawGrass(tile, rect);
                DrawTree(rect.GetCenter(), 1.3f, tile);
                break;

            default:
                DrawGrass(tile, rect);
                break;
        }
    }

    private void DrawGrass(GridPosition tile, Rect2 rect)
    {
        DrawRect(rect, GrassMid);
        // Soft patches of darker and lighter grass, no tile edges.
        DrawRect(new Rect2(rect.Position + new Vector2(Speck(tile, 70) * 16, Speck(tile, 71) * 16), new Vector2(16, 12)), GrassDark with { A = 0.25f });
        DrawRect(new Rect2(rect.Position + new Vector2(Speck(tile, 72) * 18, Speck(tile, 73) * 18), new Vector2(12, 10)), GrassLight with { A = 0.2f });
        for (int i = 0; i < 6; i++)
        {
            var at = rect.Position + new Vector2(2 + Speck(tile, i) * 28, 4 + Speck(tile, i + 10) * 26);
            Color blade = Speck(tile, i + 20) < 0.5f ? GrassDark : GrassLight;
            DrawLine(at, at + new Vector2(-1, -3), blade, 1);
            DrawLine(at + new Vector2(1, 0), at + new Vector2(2, -4), blade, 1);
        }

        if (Speck(tile, 99) < 0.18f)
        {
            // A few flowers.
            var flower = rect.Position + new Vector2(5 + Speck(tile, 98) * 22, 6 + Speck(tile, 97) * 20);
            Color petals = Speck(tile, 96) < 0.5f ? new Color("f0e070") : new Color("f4f0f8");
            DrawCircle(flower, 1.6f, petals);
            DrawCircle(flower + new Vector2(5, 3), 1.3f, petals);
        }
    }

    private void DrawRoad(GridPosition tile, Rect2 rect, bool wayOut)
    {
        // Grass tufts creep in along the road's edges.
        var road = new Rect2(rect.Position + new Vector2(0, 0), rect.Size);
        bool grassLeft = !IsRoad(tile.Step(Direction.West)), grassRight = !IsRoad(tile.Step(Direction.East));
        if (grassLeft)
        {
            road = new Rect2(road.Position + new Vector2(3, 0), road.Size - new Vector2(3, 0));
        }

        if (grassRight)
        {
            road = new Rect2(road.Position, road.Size - new Vector2(3, 0));
        }

        DrawRect(road, Dirt);
        // Cart ruts, and pebbles.
        foreach (float x in new[] { 9f, 23f })
        {
            if (rect.Position.X + x > road.Position.X && rect.Position.X + x < road.End.X)
            {
                DrawLine(rect.Position + new Vector2(x, 0), rect.Position + new Vector2(x, TileSize), DirtDark, 2);
            }
        }

        for (int i = 0; i < 4; i++)
        {
            var at = rect.Position + new Vector2(4 + Speck(tile, i + 30) * 24, 3 + Speck(tile, i + 40) * 26);
            if (road.HasPoint(at))
            {
                DrawRect(new Rect2(at, new Vector2(2, 2)), Speck(tile, i + 50) < 0.5f ? DirtLight : DirtDark);
            }
        }

        if (wayOut)
        {
            // The way out: the road runs on, lit, chevrons pointing off the board.
            DrawRect(road, new Color(1, 0.95f, 0.7f, 0.18f));
            Vector2 c = rect.GetCenter();
            DrawPolyline([c + new Vector2(-5, -1), c + new Vector2(0, 4), c + new Vector2(5, -1)], Gold with { A = 0.8f }, 2);
        }
    }

    private bool IsRoad(GridPosition tile) =>
        _room!.Layout.Contains(tile) && KeepCourtyard.GroundAt(tile) is CourtyardGround.Road or CourtyardGround.WayOut or CourtyardGround.Bridge;

    private void DrawFlagstones(GridPosition tile, Rect2 rect)
    {
        DrawRect(rect, Flag);
        // Two courses of slabs a tile, staggered row by row.
        for (int row = 0; row < 2; row++)
        {
            float y = rect.Position.Y + row * 16;
            DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.End.X, y), FlagDark, 1);
            float offset = (tile.Y * 2 + row) % 2 == 0 ? 0 : 10;
            for (float x = offset; x < TileSize; x += 20)
            {
                DrawLine(new Vector2(rect.Position.X + x, y), new Vector2(rect.Position.X + x, y + 16), FlagDark, 1);
            }

            DrawLine(new Vector2(rect.Position.X, y + 1), new Vector2(rect.End.X, y + 1), FlagLight with { A = 0.5f }, 1);
        }

        if (Speck(tile, 7) < 0.4f)
        {
            var moss = rect.Position + new Vector2(Speck(tile, 8) * 26, Speck(tile, 9) * 26);
            DrawRect(new Rect2(moss, new Vector2(4, 2)), Moss);
            DrawRect(new Rect2(moss + new Vector2(1, 2), new Vector2(2, 1)), Moss.Darkened(0.2f));
        }

        // The grass meets the apron in a ragged edge.
        foreach (Direction side in Sides)
        {
            GridPosition next = tile.Step(side);
            if (_room!.Layout.Contains(next) && KeepCourtyard.GroundAt(next) == CourtyardGround.Grass)
            {
                for (int i = 0; i < 4; i++)
                {
                    float along = 2 + Speck(tile, 60 + i + (int)side * 4) * 28;
                    Vector2 at = side switch
                    {
                        Direction.North => rect.Position + new Vector2(along, 0),
                        Direction.South => rect.Position + new Vector2(along, TileSize - 2),
                        Direction.West => rect.Position + new Vector2(0, along),
                        _ => rect.Position + new Vector2(TileSize - 2, along),
                    };
                    DrawRect(new Rect2(at, new Vector2(2, 2)), GrassDark);
                }
            }
        }
    }

    /// <summary>Where the moat meets dry land: a muddy bank and a lip of grass or stone.</summary>
    private void DrawMoatBanks(GridPosition tile, Rect2 rect)
    {
        foreach (Direction side in new[] { Direction.North, Direction.South })
        {
            GridPosition next = tile.Step(side);
            if (!_room!.Layout.Contains(next) || KeepCourtyard.GroundAt(next) is CourtyardGround.Moat or CourtyardGround.Bridge)
            {
                continue;
            }

            bool stone = KeepCourtyard.GroundAt(next) == CourtyardGround.Flagstone;
            float y = side == Direction.North ? rect.Position.Y : rect.End.Y - 4;
            DrawRect(new Rect2(rect.Position.X, y, TileSize, 4), stone ? FlagDark : DirtDark);
            DrawLine(new Vector2(rect.Position.X, side == Direction.North ? y + 4 : y), new Vector2(rect.End.X, side == Direction.North ? y + 4 : y), new Color(1, 1, 1, 0.25f), 1);
        }
    }

    private void DrawBridge(GridPosition tile, Rect2 rect)
    {
        // Planks across, the bridge's edge beams along its sides, iron at the ends.
        var deck = new Rect2(rect.Position.X, rect.Position.Y, TileSize, TileSize);
        DrawRect(deck, Plank);
        for (float y = 0; y < TileSize; y += 6)
        {
            DrawLine(new Vector2(deck.Position.X, deck.Position.Y + y), new Vector2(deck.End.X, deck.Position.Y + y), PlankDark, 1);
            DrawLine(new Vector2(deck.Position.X, deck.Position.Y + y + 1), new Vector2(deck.End.X, deck.Position.Y + y + 1), PlankLight with { A = 0.4f }, 1);
        }

        if (!IsBridge(tile.Step(Direction.West)))
        {
            DrawRect(new Rect2(rect.Position, new Vector2(4, TileSize)), PlankDark);
            DrawRect(new Rect2(rect.Position + new Vector2(1, 0), new Vector2(1, TileSize)), PlankLight);
        }

        if (!IsBridge(tile.Step(Direction.East)))
        {
            DrawRect(new Rect2(rect.End.X - 4, rect.Position.Y, 4, TileSize), PlankDark);
            DrawRect(new Rect2(rect.End.X - 3, rect.Position.Y, 1, TileSize), PlankLight);
        }

        // Nail heads.
        DrawRect(new Rect2(rect.Position + new Vector2(6, 2), new Vector2(1, 1)), KeepShadow);
        DrawRect(new Rect2(rect.Position + new Vector2(25, 14), new Vector2(1, 1)), KeepShadow);
    }

    private bool IsBridge(GridPosition tile) => _room!.Layout.Contains(tile) && KeepCourtyard.GroundAt(tile) == CourtyardGround.Bridge;

    /// <summary>A round, leafy tree (or a bush, smaller), its shadow on the grass.</summary>
    private void DrawTree(Vector2 at, float size, GridPosition seed)
    {
        DrawCircle(at + new Vector2(4, 10) * size, 11 * size, new Color(0, 0, 0, 0.25f));
        DrawRect(new Rect2(at + new Vector2(-2, 2) * size, new Vector2(4, 10) * size), new Color("5a3a1e"));
        DrawCircle(at + new Vector2(0, -2) * size, 13 * size, new Color("2a5626"));
        DrawCircle(at + new Vector2(-4, -4) * size, 9 * size, new Color("3a7032"));
        DrawCircle(at + new Vector2(4, -6) * size, 8 * size, new Color("3a7032"));
        DrawCircle(at + new Vector2(-2, -8) * size, 6 * size, new Color("57944a"));
        if (Speck(seed, 3) < 0.4f)
        {
            DrawCircle(at + new Vector2(5, -1) * size, 1.6f * size, new Color("d84a4a"));
            DrawCircle(at + new Vector2(-6, 2) * size, 1.6f * size, new Color("d84a4a"));
        }
    }

    /// <summary>
    /// Round the courtyard's other three sides, the countryside runs on: grass and a line of trees just beyond
    /// the edge, and where the moat meets the edge, its water runs on under the trees.
    /// </summary>
    private void DrawCourtyardSurrounds()
    {
        RoomLayout layout = _room!.Layout;
        float width = layout.Width * TileSize, height = layout.Height * TileSize;
        Color far = GrassDark.Darkened(0.2f);
        DrawRect(new Rect2(-3 * TileSize, 0, 3 * TileSize, height + 3 * TileSize), far);
        DrawRect(new Rect2(width, 0, 3 * TileSize, height + 3 * TileSize), far);
        DrawRect(new Rect2(0, height, width, 3 * TileSize), far);
        for (int y = 0; y < layout.Height; y++)
        {
            if (KeepCourtyard.GroundAt(new GridPosition(0, y)) == CourtyardGround.Moat)
            {
                DrawRect(new Rect2(-3 * TileSize, y * TileSize, 3 * TileSize, TileSize), new Color("1e3a5a"));
                DrawRect(new Rect2(width, y * TileSize, 3 * TileSize, TileSize), new Color("1e3a5a"));
                continue;
            }

            if (y > 4 && y % 2 == 0)
            {
                DrawTree(new Vector2(-14, y * TileSize + 12), 1.1f, new GridPosition(-1, y));
                DrawTree(new Vector2(width + 14, y * TileSize + 20), 1.1f, new GridPosition(99, y));
            }
        }

        for (int x = 0; x < layout.Width; x += 2)
        {
            if (KeepCourtyard.GroundAt(new GridPosition(x, layout.Height - 1)) != CourtyardGround.WayOut)
            {
                DrawTree(new Vector2(x * TileSize + 16, height + 16), 1.1f, new GridPosition(x, 99));
            }
        }

        // The road runs on out of sight.
        foreach (GridPosition exit in _room.TilesOfKind(TileKind.Exit))
        {
            DrawRect(new Rect2(exit.X * TileSize, height, TileSize, 3 * TileSize), Dirt.Darkened(0.15f));
        }
    }

    /// <summary>Where the great door is drawn, in board pixels: three tiles wide, its threshold the door tile.</summary>
    private static Rect2 GreatDoorRect
    {
        get
        {
            GridPosition door = KeepCourtyard.Door;
            return new Rect2((door.X - 1) * TileSize + 6, -3 * TileSize - 4, 3 * TileSize - 12, 3 * TileSize + 4);
        }
    }

    /// <summary>
    /// The keep, rising up off the top of the courtyard: its front wall in courses of green-grey stone with a
    /// darker plinth and moss along its foot; a squat tower at each end, standing proud of the wall, with arrow
    /// slits and battlements; a row of arched windows lit green; a banner either side of the great door; and the
    /// great door itself under its arch, shut and iron-bound (an emerald set in it) or, unlocked, open on the dark.
    /// </summary>
    private void DrawKeepFacade()
    {
        RoomLayout layout = _room!.Layout;
        float width = layout.Width * TileSize;
        float top = -FacadeTiles * TileSize;
        float wallTop = top + 1.2f * TileSize;

        // The wall, in courses.
        var wall = new Rect2(-3 * TileSize, wallTop, width + 6 * TileSize, -wallTop);
        DrawRect(wall, KeepStone);
        for (float y = wallTop; y < 0; y += 8)
        {
            DrawLine(new Vector2(wall.Position.X, y), new Vector2(wall.End.X, y), KeepMortar, 1);
            DrawLine(new Vector2(wall.Position.X, y + 1), new Vector2(wall.End.X, y + 1), KeepLight with { A = 0.35f }, 1);
            float offset = (int)((y - wallTop) / 8) % 2 == 0 ? 0 : 8;
            for (float x = wall.Position.X + offset; x < wall.End.X; x += 16)
            {
                DrawLine(new Vector2(x, y), new Vector2(x, y + 8), KeepMortar, 1);
            }
        }

        // Battlements along the wall's top.
        Merlons(wall.Position.X, wall.End.X, wallTop);

        // Arched windows, lit green, along the upper wall (clear of the towers and the door).
        for (float x = 4.5f * TileSize; x < width - 4.5f * TileSize; x += 3 * TileSize)
        {
            if (Mathf.Abs(x + TileSize / 2f - (KeepCourtyard.Door.X * TileSize + TileSize / 2f)) < 3 * TileSize)
            {
                continue;
            }

            Window(new Vector2(x + 8, wallTop + 0.9f * TileSize), 12, 20);
        }

        // The plinth: a darker band of bigger stones along the foot, moss on it.
        DrawRect(new Rect2(wall.Position.X, -0.7f * TileSize, wall.Size.X, 0.7f * TileSize), KeepShadow.Lightened(0.08f));
        DrawLine(new Vector2(wall.Position.X, -0.7f * TileSize), new Vector2(wall.End.X, -0.7f * TileSize), KeepLight with { A = 0.5f }, 1);
        for (float x = wall.Position.X; x < wall.End.X; x += 24)
        {
            DrawLine(new Vector2(x, -0.7f * TileSize), new Vector2(x, 0), KeepMortar, 1);
            if (Speck(new GridPosition((int)x, 0), 5) < 0.5f)
            {
                DrawRect(new Rect2(x + 3, -5, 9, 3), Moss);
                DrawRect(new Rect2(x + 5, -8, 4, 3), Moss.Darkened(0.2f));
            }
        }

        // The towers, at each end, standing up past the wall.
        Tower(-0.6f * TileSize, 3.6f * TileSize, top);
        Tower(width - 3 * TileSize, 3.6f * TileSize, top);

        // Banners either side of the door.
        float doorX = KeepCourtyard.Door.X * TileSize + TileSize / 2f;
        BannerAt(new Vector2(doorX - 2.6f * TileSize, wallTop + 0.4f * TileSize));
        BannerAt(new Vector2(doorX + 2.6f * TileSize, wallTop + 0.4f * TileSize));

        DrawGreatDoor();

        // The keep's shadow along the foot of the wall.
        for (int i = 0; i < 6; i++)
        {
            DrawRect(new Rect2(0, i * 2, width, 2), new Color(0, 0, 0, 0.28f - i * 0.045f));
        }
    }

    private void Merlons(float from, float to, float y)
    {
        for (float x = from; x < to; x += 16)
        {
            var merlon = new Rect2(x, y - 9, 10, 9);
            DrawRect(merlon, KeepStone.Lightened(0.06f));
            DrawLine(merlon.Position, merlon.Position + new Vector2(merlon.Size.X, 0), KeepLight, 1);
            DrawRect(merlon, KeepMortar, filled: false, width: 1);
        }

        DrawRect(new Rect2(from, y - 1, to - from, 3), KeepShadow);
    }

    private void Window(Vector2 at, float width, float height)
    {
        DrawRect(new Rect2(at + new Vector2(-2, -2), new Vector2(width + 4, height + 4)), KeepLight.Darkened(0.2f));
        DrawRect(new Rect2(at, new Vector2(width, height)), new Color("0a120e"));
        DrawCircle(at + new Vector2(width / 2, 0), width / 2, new Color("0a120e"));
        DrawRect(new Rect2(at + new Vector2(2, height * 0.35f), new Vector2(width - 4, height * 0.6f)), Emerald with { A = 0.35f });
        DrawLine(at + new Vector2(width / 2, -width / 2), at + new Vector2(width / 2, height), KeepShadow, 1);
    }

    private void Tower(float x, float width, float top)
    {
        var body = new Rect2(x, top + 0.3f * TileSize, width, -top - 0.3f * TileSize);
        DrawRect(body, KeepStone.Lightened(0.05f));
        for (float y = body.Position.Y; y < 0; y += 8)
        {
            DrawLine(new Vector2(body.Position.X, y), new Vector2(body.End.X, y), KeepMortar, 1);
            float offset = (int)((y - body.Position.Y) / 8) % 2 == 0 ? 2 : 10;
            for (float bx = body.Position.X + offset; bx < body.End.X; bx += 16)
            {
                DrawLine(new Vector2(bx, y), new Vector2(bx, y + 8), KeepMortar, 1);
            }
        }

        // Lit on the left, shadowed on the right: it stands out from the wall.
        DrawRect(new Rect2(body.Position, new Vector2(3, body.Size.Y)), KeepLight with { A = 0.5f });
        DrawRect(new Rect2(body.End.X - 5, body.Position.Y, 5, body.Size.Y), KeepShadow with { A = 0.6f });
        Merlons(body.Position.X - 4, body.End.X + 4, body.Position.Y);

        // Arrow slits, one above another.
        for (float y = body.Position.Y + 0.8f * TileSize; y < -1.4f * TileSize; y += 1.5f * TileSize)
        {
            var slit = new Rect2(body.GetCenter().X - 2, y, 4, 16);
            DrawRect(slit.Grow(1), KeepLight.Darkened(0.25f));
            DrawRect(slit, new Color("080c0a"));
            DrawRect(new Rect2(slit.Position + new Vector2(-3, 6), new Vector2(10, 3)), new Color("080c0a"));
        }
    }

    private void BannerAt(Vector2 top)
    {
        float width = 20, height = 2.4f * TileSize;
        DrawRect(new Rect2(top + new Vector2(-width / 2 - 3, -3), new Vector2(width + 6, 3)), new Color("3a2a18"));
        DrawColoredPolygon([top + new Vector2(-width / 2, 0), top + new Vector2(width / 2, 0), top + new Vector2(width / 2, height),
            top + new Vector2(0, height - 8), top + new Vector2(-width / 2, height)], Banner);
        DrawRect(new Rect2(top + new Vector2(width / 2 - 3, 0), new Vector2(3, height - 2)), BannerDark);
        DrawRect(new Rect2(top + new Vector2(-width / 2, 4), new Vector2(width, 2)), Gold);

        // The keep's emerald on it.
        Vector2 gem = top + new Vector2(0, height * 0.42f);
        DrawColoredPolygon([gem + new Vector2(0, -7), gem + new Vector2(6, 0), gem + new Vector2(0, 7), gem + new Vector2(-6, 0)], Gold);
        DrawColoredPolygon([gem + new Vector2(0, -5), gem + new Vector2(4, 0), gem + new Vector2(0, 5), gem + new Vector2(-4, 0)], Emerald);
        DrawLine(gem + new Vector2(-2, -2), gem + new Vector2(0, -4), new Color(1, 1, 1, 0.6f), 1);
    }

    /// <summary>The great door under its arch: shut and iron-bound while locked, standing open once the key turns.</summary>
    private void DrawGreatDoor()
    {
        Rect2 door = GreatDoorRect;
        float radius = door.Size.X / 2;
        var opening = new Rect2(door.Position.X, door.Position.Y + radius, door.Size.X, door.Size.Y - radius);
        Vector2 archCenter = new(door.GetCenter().X, opening.Position.Y);

        // The arch's voussoirs, then the jambs.
        DrawCircle(archCenter, radius + 9, KeepLight.Darkened(0.1f));
        DrawRect(new Rect2(opening.Position + new Vector2(-9, 0), new Vector2(opening.Size.X + 18, opening.Size.Y)), KeepLight.Darkened(0.1f));
        for (int i = 0; i <= 8; i++)
        {
            float angle = Mathf.Pi + i * Mathf.Pi / 8;
            Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle));
            DrawLine(archCenter + dir * radius, archCenter + dir * (radius + 9), KeepMortar, 1);
        }

        DrawColoredPolygon([archCenter + new Vector2(-5, -radius - 10), archCenter + new Vector2(5, -radius - 10), archCenter + new Vector2(3, -radius + 1), archCenter + new Vector2(-3, -radius + 1)], KeepLight);
        DrawCircle(archCenter + new Vector2(0, -radius - 4), 3, Emerald);

        bool locked = _room!.PassageOn(Direction.North) is { IsLocked: true };
        Color dark = new("060908");
        DrawCircle(archCenter, radius, dark);
        DrawRect(opening, dark);
        if (locked)
        {
            // Two leaves of heavy planks, shut, bound in green iron, the keep's emerald where they meet.
            Color wood = new("5a3a1e"), woodDark = new("3a2410"), iron = new("2f6a46"), ironDark = new("163a24");
            DrawCircle(archCenter, radius - 3, wood);
            DrawRect(new Rect2(opening.Position + new Vector2(3, 0), opening.Size - new Vector2(6, 0)), wood);
            for (float x = opening.Position.X + 9; x < opening.End.X - 3; x += 9)
            {
                DrawLine(new Vector2(x, archCenter.Y - Mathf.Sqrt(Mathf.Max(0, (radius - 3) * (radius - 3) - (x - archCenter.X) * (x - archCenter.X)))), new Vector2(x, opening.End.Y), woodDark, 1);
            }

            DrawLine(new Vector2(archCenter.X, archCenter.Y - radius + 3), new Vector2(archCenter.X, opening.End.Y), ironDark, 3);
            foreach (float y in new[] { opening.Position.Y + 8, opening.Position.Y + opening.Size.Y * 0.5f, opening.End.Y - 12 })
            {
                DrawRect(new Rect2(opening.Position.X + 3, y, opening.Size.X - 6, 4), iron);
                DrawLine(new Vector2(opening.Position.X + 3, y + 4), new Vector2(opening.End.X - 3, y + 4), ironDark, 1);
                for (float x = opening.Position.X + 7; x < opening.End.X - 4; x += 8)
                {
                    DrawRect(new Rect2(x, y + 1, 2, 2), new Color("9adab0"));
                }
            }

            // Ring handles, and the emerald lock.
            DrawArc(new Vector2(archCenter.X - 9, opening.Position.Y + opening.Size.Y * 0.5f + 12), 4, 0, Mathf.Tau, 12, ironDark, 2);
            DrawArc(new Vector2(archCenter.X + 9, opening.Position.Y + opening.Size.Y * 0.5f + 12), 4, 0, Mathf.Tau, 12, ironDark, 2);
            Vector2 gem = new(archCenter.X, opening.Position.Y + opening.Size.Y * 0.5f - 6);
            DrawColoredPolygon([gem + new Vector2(0, -7), gem + new Vector2(6, 0), gem + new Vector2(0, 7), gem + new Vector2(-6, 0)], Gold);
            DrawColoredPolygon([gem + new Vector2(0, -5), gem + new Vector2(4, 0), gem + new Vector2(0, 5), gem + new Vector2(-4, 0)], Emerald);
        }
        else
        {
            // Open: the leaves swung back against the jambs, the hall's light within.
            Color wood = new("5a3a1e"), iron = new("2f6a46");
            DrawRect(new Rect2(opening.Position, new Vector2(9, opening.Size.Y)), wood);
            DrawRect(new Rect2(opening.End.X - 9, opening.Position.Y, 9, opening.Size.Y), wood);
            DrawRect(new Rect2(opening.Position + new Vector2(0, 10), new Vector2(9, 3)), iron);
            DrawRect(new Rect2(opening.End.X - 9, opening.Position.Y + 10, 9, 3), iron);
            DrawRect(new Rect2(opening.Position + new Vector2(12, opening.Size.Y - 18), new Vector2(opening.Size.X - 24, 18)), new Color(0.9f, 0.75f, 0.4f, 0.12f));
        }

        // The threshold step.
        DrawRect(new Rect2(door.Position.X - 6, -4, door.Size.X + 12, 4), FlagLight);
    }
}
