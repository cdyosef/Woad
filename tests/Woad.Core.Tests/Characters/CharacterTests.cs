using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Characters;

public class CharacterTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static Character NewCharacter(string name = "Morgan", StatAllocation? allocation = null) =>
        Character.Create(name, "ember", allocation ?? StatAllocation.None, Content, Now);

    [Fact]
    public void NewCharactersStartAtLevelOneWithTheStarterKit()
    {
        Character character = NewCharacter(allocation: new StatAllocation { Hearts = 1, AttackDice = 1 });

        Assert.Equal(1, character.Level);
        Assert.Equal(2, character.UnspentPoints);
        Assert.Equal(["wooden_sword", "buckler"], character.Equipped);
        Assert.Equal(["health_potion"], character.Belt);
        Assert.Empty(character.Backpack);
        Assert.Equal(5, character.BaseStats.Hearts);
        Assert.Equal(2, character.BaseStats.AttackDice);
    }

    [Theory]
    [InlineData("", "a name")]
    [InlineData("   ", "a name")]
    [InlineData("Seventeen letters", "at most 16")]
    [InlineData("Bad<Name>", "letters, numbers")]
    public void NamesAreChecked(string name, string problem)
    {
        var error = Assert.Throws<ArgumentException>(() => NewCharacter(name));

        Assert.Contains(problem, error.Message);
    }

    [Fact]
    public void NamesAreTrimmedAndMayUseApostrophesAndHyphens()
    {
        Assert.Equal("Ny'la Ash-Born", NewCharacter("  Ny'la Ash-Born ").Name);
    }

    [Fact]
    public void CreationRejectsOverspending()
    {
        Assert.Throws<ArgumentException>(() => NewCharacter(allocation: new StatAllocation { Hearts = 7 }));
    }

    [Fact]
    public void LeftoverPointsCanBeSpentLaterButNotRefunded()
    {
        Character character = NewCharacter(allocation: new StatAllocation { Hearts = 2 });

        character.Allocate(new StatAllocation { Hearts = 2, Luck = 2 });

        Assert.Equal(0, character.UnspentPoints);
        Assert.Throws<ArgumentException>(() => character.Allocate(new StatAllocation { Hearts = 1, Luck = 2 }));
        Assert.Throws<ArgumentException>(() => character.Allocate(new StatAllocation { Hearts = 3, Luck = 2 }));
    }

    [Fact]
    public void GainingLevelsGrantsPoints()
    {
        Character character = NewCharacter(allocation: new StatAllocation { Hearts = 6 });

        Assert.Equal(0, character.GainXp(9));
        Assert.Equal(2, character.GainXp(21));

        Assert.Equal(3, character.Level);
        Assert.Equal(2, character.UnspentPoints);
    }

    [Fact]
    public void CharactersBecomeHeroesWithTheirStatsAndGear()
    {
        Character character = NewCharacter(allocation: new StatAllocation { Hearts = 2, Luck = 1 });
        character.ChangeAppearance(new Appearance("dwarf", Hair: "white"));
        var setup = new RunSetup
        {
            Seed = 1,
            ThemeId = "emerald_keep",
            Heroes = [character.ToHeroSetup()],
            Rooms = [new RoomSetup("hall", RoomTemplate.Parse(string.Join('\n', Enumerable.Repeat(".........", 9)), Content))],
            StartRoomId = "hall",
        };

        Hero hero = new GameSession(Content, setup).State.Heroes[0];

        Assert.Equal(character.Id, hero.CharacterId);
        Assert.Equal("ember", hero.Portrait);
        Assert.Equal(character.Appearance, hero.Appearance);
        Assert.Equal(6, hero.MaxHearts);
        Assert.Equal(1, hero.LuckRemaining);
        Assert.Equal("wooden_sword", hero.Equipment[EquipSlot.MainHand].Definition.Id);
        Assert.Equal(["health_potion"], hero.Belt.Select(card => card.Definition.Id));
        Assert.All(hero.AllCards, card => Assert.Equal(hero.Id, card.BroughtInByHeroId));
    }
}
