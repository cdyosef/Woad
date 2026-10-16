using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class WaterTests
{
    /// <summary>A room with the fish at (4, 4) in a pool of deep water (3..5, 3..5), and the hero at (4, 0).</summary>
    private static GameSession FishInAPool(string[] cards, GridPosition? heroAt = null, ScriptedDice? dice = null)
    {
        RoomTemplate room = TestRun.Room("""
            ....1....
            .........
            .........
            .........
            ....F....
            .........
            .........
            .........
            .........
            """);
        foreach (GridPosition tile in room.Layout.Positions.Where(tile => tile.ChebyshevDistanceTo(new GridPosition(4, 4)) <= 1))
        {
            room = room.WithTile(tile, TileKind.DeepWater);
        }

        GameSession session = TestRun.Start([new RoomSetup("A", room)], dice, cards: cards);
        if (heroAt is { } at)
        {
            session.HeroInSeat().Position = at;
        }

        return session;
    }

    [Fact]
    public void ArrowsAndSpellsCantReachASubmergedSwimmer()
    {
        GameSession session = FishInAPool(["bow", "scroll"], heroAt: new GridPosition(4, 1));
        Monster fish = session.OnlyMonster();

        Assert.True(session.IsSubmerged(fish));
        Assert.Empty(session.GetAttackableMonsters());
        CardInstance scroll = session.HeroInSeat().Backpack.Concat(session.HeroInSeat().Belt).Single(card => card.Definition.Id == "scroll");
        CommandResult cast = session.Execute(new UseCard(session.HeroInSeat().Id, scroll.Id, fish.Position));
        Assert.Equal("It's under the water, out of reach.", cast.Error);
    }

    [Fact]
    public void ABlowFromBesideItOrATridentReachesASubmergedSwimmer()
    {
        GameSession beside = FishInAPool(["sword"], heroAt: new GridPosition(4, 3));
        Assert.Equal([beside.OnlyMonster()], beside.GetAttackableMonsters());

        GameSession speared = FishInAPool(["trident"], heroAt: new GridPosition(4, 2));
        Assert.Single(speared.GetAttackableMonsters());
    }

    [Fact]
    public void ATridentAddsItsBonusAgainstSwimmers()
    {
        // 3, plus the trident's weapon 1 and +1 against swimmers: 5.
        GameSession session = FishInAPool(["trident"], heroAt: new GridPosition(4, 2), dice: new ScriptedDice(3));
        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, session.OnlyMonster().Id, UseWeapon: true));

        Assert.Equal(5, events.SingleOf<AttackRolled>().Total);
    }

    [Fact]
    public void AnEelNeverLeavesTheWater()
    {
        RoomTemplate room = TestRun.Room("""
            .........
            .........
            .........
            .........
            .E....1..
            .........
            .........
            .........
            .........
            """);
        foreach (GridPosition tile in new[] { new GridPosition(0, 4), new GridPosition(1, 4), new GridPosition(2, 4), new GridPosition(3, 4) })
        {
            room = room.WithTile(tile, tile.X == 3 ? TileKind.ShallowWater : TileKind.DeepWater);
        }

        GameSession session = TestRun.Start([new RoomSetup("A", room)]);
        session.Do(new EndTurn(session.HeroInSeat().Id));

        // It swims as close as the water lets it: the shallow end, still out of reach.
        Assert.Equal(new GridPosition(3, 4), session.OnlyMonster().Position);
    }

    [Fact]
    public void AShoveThatBeatsAShellFlipsIt_AndFlippedItHasNoArmor()
    {
        // A shove of 5 against the crab's armor 2: flipped (and pushed a tile).
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1Q....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 3, 1));
        Hero hero = session.HeroInSeat();
        Monster crab = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, crab.Id));

        Assert.Contains(new Flipped(crab.Id), events);
        Assert.True(crab.Prone);

        // Then 3 + 1 (an extra die against something lying down) is enough: no armor while it's on its back.
        // (It was shoved a tile away, so step up to it first.)
        hero.Position = new GridPosition(4, 4);
        session.State.Turn!.AttackDiceUsed = 0;
        events = session.Do(new AttackMonster(hero.Id, crab.Id, UseWeapon: false));
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void AFrogsTonguePullsWhoItHitsToItsSide()
    {
        // The frog's 5 against the hero's 1, from 3 tiles away: a hit, and a pull.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            .1..R....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal(new Pulled(session.OnlyMonster().Id, hero.Id, new GridPosition(1, 4), new GridPosition(3, 4)), events.SingleOf<Pulled>());
        Assert.Equal(new GridPosition(3, 4), hero.Position);
    }

    [Fact]
    public void AJellyfishsSixStuns()
    {
        // A 6 (and its Power Die's 2) against the hero's 1: a hit, and one Stun.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1J...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 2, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Single(events.OfType<Stunned>());
        Assert.Equal(1, hero.Stun.Stacks);
    }

    [Fact]
    public void ADivingHelmKeepsYouAfloatForFree()
    {
        RoomTemplate room = TestRun.Room("""
            .........
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            ........d
            """);
        room = room.WithTile(new GridPosition(5, 4), TileKind.DeepWater);
        GameSession session = TestRun.Start([new RoomSetup("A", room)], cards: ["helm"]);
        Hero hero = session.HeroInSeat();
        hero.Position = new GridPosition(5, 4);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Empty(events.OfType<StayedAfloat>());
        Assert.Empty(events.OfType<Drowned>());
        Assert.Equal(new GridPosition(5, 4), hero.Position);
    }

    [Fact]
    public void TheTempleKeepsEveryMonsterWhereItCanStand()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        HeroSetup hero = new("Wren", GameRules.HeroBaseStats, GameRules.StarterKit);
        for (ulong seed = 1; seed <= 40; seed++)
        {
            RunSetup run = DungeonGenerator.CreateRun(content, "sunken_temple", [hero, hero with { Name = "Bran" }], seed);
            foreach (RoomSetup room in run.Rooms)
            {
                foreach (MonsterSpawn spawn in room.Template.Monsters)
                {
                    TileKind tile = room.Template.Layout[spawn.Position];
                    if (spawn.Monster.WaterOnly)
                    {
                        Assert.True(tile is TileKind.ShallowWater or TileKind.DeepWater, $"{spawn.Monster.Id} on {tile} in {room.Id} (seed {seed})");
                    }
                    else if (tile == TileKind.DeepWater)
                    {
                        Assert.True(spawn.Monster.Swimmer || spawn.Monster.Flying, $"{spawn.Monster.Id} in deep water in {room.Id} (seed {seed})");
                    }
                }
            }

            RoomSetup arena = run.Rooms.Single(room => room.Role == RoomRole.Boss);
            MonsterSpawn boss = arena.Template.Monsters.Single(spawn => spawn.Monster.Boss);
            Assert.Equal(TileKind.DeepWater, arena.Template.Layout[boss.Position]);
        }
    }
}
