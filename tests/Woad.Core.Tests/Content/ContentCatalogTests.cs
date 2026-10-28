using Woad.Core.Content;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Content;

public class ContentCatalogTests
{
    [Fact]
    public void ShippedContentLoadsAndIsValid()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();

        Assert.NotEmpty(content.Cards);
        Assert.NotEmpty(content.Monsters);
        ThemeDefinition theme = content.GetTheme("emerald_keep");
        Assert.All(theme.LootDeck, entry => Assert.True(content.Cards.ContainsKey(entry.Card)));
    }

    [Fact]
    public void ShippedContentParsesEnumsAndNestedValues()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();

        CardDefinition bow = content.GetCard("short_bow");
        Assert.Equal(EquipSlot.MainHand, bow.Slot);
        Assert.True(bow.TwoHanded);
        Assert.Equal(6, bow.AttackRange);
        Assert.Equal(1, bow.Modifiers.Weapon);

        CardDefinition bomb = content.GetCard("bomb");
        Assert.Equal(CardUsage.SingleUse, bomb.Usage);
        Assert.Equal(new CardEffect { Kind = CardEffectKind.Attack, Amount = 4, Range = 3, AreaRadius = 1 }, bomb.Effect);

        MonsterDefinition boss = content.GetMonster("iron_warden");
        Assert.True(boss.Boss);
        Assert.Equal(new SweepAttack { Radius = 2, Every = 1, Bonus = true }, boss.Sweep);
        Assert.Equal(new ChainAttack { Range = 6, Dice = 3, Lock = 9 }, boss.Chain);
        Assert.True(boss.Arc);
        Assert.Same(boss, content.FindMonsterByGlyph(boss.Glyph));
    }

    [Fact]
    public void ShippedStarterKitCardsExist()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();

        Assert.All(Woad.Core.Rules.GameRules.StarterKit, id => content.GetCard(id));
    }

    [Fact]
    public void TyposInContentFilesAreRejected()
    {
        string cards = """[ { "id": "sword", "name": "Sword", "type": "weapon", "slot": "mainHand", "atackRange": 2 } ]""";

        var error = Assert.Throws<InvalidContentException>(() => ContentCatalog.Parse(cards, TestContent.MonstersJson, [TestContent.ThemeJson]));

        Assert.Contains("atackRange", error.Message);
    }

    [Fact]
    public void BrokenReferencesAreAllReported()
    {
        string monsters = """[ { "id": "ooze", "name": "Ooze", "glyph": "o", "hearts": 1, "move": 1, "attackDice": 1, "defenseDice": 0, "splitsInto": "nothing", "splitCount": 2 } ]""";
        string theme = """{ "id": "t", "name": "T", "lootDeck": [ { "card": "missing" } ] }""";

        var error = Assert.Throws<InvalidContentException>(() => ContentCatalog.Parse(TestContent.CardsJson, monsters, [theme], ContentCatalog.ReadEmbedded("spells.json")));

        Assert.Equal(2, error.Errors.Count);
        Assert.Contains(error.Errors, message => message.Contains("nothing"));
        Assert.Contains(error.Errors, message => message.Contains("missing"));
    }

    [Theory]
    [InlineData("""{ "id": "x", "name": "X", "type": "weapon" }""", "no slot")]
    [InlineData("""{ "id": "x", "name": "X", "type": "potion", "usage": "singleUse" }""", "no effect")]
    [InlineData("""{ "id": "x", "name": "X", "type": "tool", "effect": { "kind": "freeze", "amount": 1, "range": 2 } }""", "needs a slot")]
    [InlineData("""{ "id": "x", "name": "X", "type": "weapon", "slot": "offHand", "twoHanded": true }""", "two-handed")]
    [InlineData("""{ "id": "x", "name": "X", "type": "treasure" }""", "no value")]
    public void CardRulesAreEnforced(string card, string expectedProblem)
    {
        var error = Assert.Throws<InvalidContentException>(() =>
            ContentCatalog.Parse($"[{card}]", TestContent.MonstersJson, ["""{ "id": "t", "name": "T", "lootDeck": [ { "card": "x" } ] }"""]));

        Assert.Contains(error.Errors, message => message.Contains(expectedProblem));
    }

    [Fact]
    public void MonsterGlyphsCantClashWithTemplateSymbols()
    {
        string monsters = """[ { "id": "wall_thing", "name": "Wall Thing", "glyph": "#", "hearts": 1, "move": 1, "attackDice": 1, "defenseDice": 0 } ]""";

        var error = Assert.Throws<InvalidContentException>(() => ContentCatalog.Parse(TestContent.CardsJson, monsters, [TestContent.ThemeJson]));

        Assert.Contains(error.Errors, message => message.Contains("reserved"));
    }
}
