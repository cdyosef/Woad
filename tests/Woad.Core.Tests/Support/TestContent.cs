using Woad.Core.Content;

namespace Woad.Core.Tests.Support;

/// <summary>
/// Small, stable content for rules tests, so tuning the real game's cards and monsters
/// never breaks them. Each monster isolates one behavior.
/// </summary>
internal static class TestContent
{
    public const string ThemeId = "test_theme";

    public const string CardsJson = """
        [
          { "id": "sword", "name": "Sword", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 1 } },
          { "id": "big_sword", "name": "Big Sword", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 2 } },
          { "id": "bow", "name": "Bow", "type": "weapon", "slot": "mainHand", "twoHanded": true, "modifiers": { "weapon": 1 }, "attackRange": 4 },
          { "id": "shield", "name": "Shield", "type": "shield", "slot": "offHand", "modifiers": { "armor": 1 } },
          { "id": "cap", "name": "Cap", "type": "armor", "slot": "head", "modifiers": { "hearts": 1 } },
          { "id": "boots", "name": "Boots", "type": "boots", "slot": "feet", "modifiers": { "moveDice": 1 } },
          { "id": "potion", "name": "Potion", "type": "potion", "usage": "singleUse", "effect": { "kind": "heal", "amount": 3 } },
          { "id": "bomb", "name": "Bomb", "type": "bomb", "usage": "singleUse", "effect": { "kind": "attack", "amount": 4, "range": 3, "areaRadius": 1 } },
          { "id": "scroll", "name": "Scroll", "type": "spell", "usage": "singleUse", "effect": { "kind": "attack", "amount": 3, "range": 3 } },
          { "id": "firebolt", "name": "Firebolt", "type": "spell", "usage": "singleUse", "effect": { "kind": "attack", "amount": 3, "range": 3, "fire": true } },
          { "id": "wand", "name": "Wand", "type": "spell", "slot": "offHand", "usage": "charges", "charges": 2, "effect": { "kind": "freeze", "amount": 1, "range": 3 } },
          { "id": "icicle", "name": "Icicle", "type": "tool", "slot": "offHand", "effect": { "kind": "freeze", "amount": 1, "range": 4 } },
          { "id": "boomerang", "name": "Boomerang", "type": "weapon", "slot": "offHand", "altSlot": "mainHand", "attackRange": 4, "attackBonus": 1, "stunOnSix": true },
          { "id": "picks", "name": "Picks", "type": "tool", "slot": "offHand", "modifiers": { "tools": 2 } },
          { "id": "coin", "name": "Coin", "type": "treasure", "value": 5 },
          { "id": "purse", "name": "Purse", "type": "treasure", "coins": true, "value": 3 },
          { "id": "gem", "name": "Gem", "type": "treasure", "value": 15 }
        ]
        """;

    public const string MonstersJson = """
        [
          { "id": "grunt", "name": "Grunt", "glyph": "m", "hearts": 1, "move": 2, "attackDice": 1, "defenseDice": 1 },
          { "id": "brute", "name": "Brute", "glyph": "B", "hearts": 2, "move": 2, "attackDice": 2, "defenseDice": 1 },
          { "id": "dummy", "name": "Dummy", "glyph": "d", "hearts": 3, "move": 0, "attackDice": 0, "defenseDice": 0 },
          { "id": "splitter", "name": "Splitter", "glyph": "s", "hearts": 1, "move": 2, "attackDice": 1, "defenseDice": 1, "splitsInto": "piece", "splitCount": 2 },
          { "id": "piece", "name": "Piece", "glyph": "p", "hearts": 1, "move": 2, "attackDice": 1, "defenseDice": 0 },
          { "id": "spearman", "name": "Spearman", "glyph": "g", "hearts": 2, "move": 3, "attackDice": 2, "defenseDice": 1, "attackPattern": "reach", "attackRange": 2 },
          { "id": "archer", "name": "Archer", "glyph": "a", "hearts": 2, "move": 2, "attackDice": 2, "defenseDice": 1, "attackPattern": "ranged", "attackRange": 5, "keepsDistance": true },
          { "id": "flyer", "name": "Flyer", "glyph": "f", "hearts": 1, "move": 4, "attackDice": 1, "defenseDice": 0, "flying": true },
          { "id": "bat", "name": "Bat", "glyph": "b", "hearts": 1, "move": 4, "attackDice": 1, "defenseDice": 0, "flying": true, "erratic": true },
          { "id": "bones", "name": "Bones", "glyph": "k", "hearts": 2, "move": 2, "attackDice": 1, "defenseDice": 0, "reassembles": true },
          { "id": "boss", "name": "Boss", "glyph": "W", "hearts": 10, "move": 0, "attackDice": 1, "defenseDice": 0, "sweep": { "radius": 2, "every": 2 }, "stunImmune": true, "boss": true, "xp": 25 },
          { "id": "hoarder", "name": "Hoarder", "glyph": "h", "hearts": 1, "move": 0, "attackDice": 0, "defenseDice": 0, "dropChance": 100, "drops": 2, "xp": 7 },
          { "id": "knight", "name": "Knight", "glyph": "K", "hearts": 1, "move": 0, "attackDice": 0, "defenseDice": 1, "armor": 2 },
          { "id": "duelist", "name": "Duelist", "glyph": "D", "hearts": 1, "move": 0, "attackDice": 2, "defenseDice": 0, "weapon": 2 },
          { "id": "torch", "name": "Torch", "glyph": "t", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "fireAttack": true, "fireImmune": true },
          { "id": "sniper", "name": "Sniper", "glyph": "n", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "attackPattern": "ranged", "attackRange": 5 },
          { "id": "pickpocket", "name": "Pickpocket", "glyph": "T", "hearts": 2, "move": 4, "attackDice": 1, "defenseDice": 0, "odHandDice": 3, "thief": true },
          { "id": "mugger", "name": "Mugger", "glyph": "Y", "hearts": 1, "move": 0, "attackDice": 2, "defenseDice": 0, "odHandDice": 1, "mugger": true, "goldOnly": true, "separateAttacks": true }
        ]
        """;

    /// <summary>
    /// Gems only from floor 2. Pots: rolls 0-49 give a coin, 50-79 a heart, 80-99 nothing. Keys never
    /// turn up by chance, so scripted loot rolls stay as they are; tests place keys themselves.
    /// </summary>
    public const string ThemeJson = """
        {
          "id": "test_theme",
          "name": "Test Theme",
          "lootDeck": [ { "card": "coin", "copies": 2 }, { "card": "gem", "minFloor": 2 } ],
          "pots": { "treasure": "coin", "treasureChance": 50, "heartChance": 30 },
          "keys": { "monster": 0, "pot": 0, "roomCleared": 0, "steal": 50 }
        }
        """;

    public static ContentCatalog Catalog { get; } = ContentCatalog.Parse(CardsJson, MonstersJson, [ThemeJson]);
}
