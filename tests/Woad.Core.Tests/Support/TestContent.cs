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
          { "id": "ksword", "name": "K-Sword", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 2 }, "set": { "with": "kshield", "modifiers": { "weapon": 1, "block": 1 } } },
          { "id": "kshield", "name": "K-Shield", "type": "shield", "slot": "offHand", "modifiers": { "armor": 2, "defenseDice": 1 }, "set": { "with": "ksword", "modifiers": { "guardDice": 1 } } },
          { "id": "t_dagger", "name": "T-Dagger", "type": "weapon", "slot": "offHand", "altSlot": "mainHand", "stacks": true, "attackBonus": 1, "offHandStrike": true, "thrownRange": 3, "ambush": true, "light": true },
          { "id": "t_rapier", "name": "T-Rapier", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 2, "block": 1 }, "twinStrike": true },
          { "id": "t_axe", "name": "T-Axe", "type": "weapon", "slot": "mainHand", "twoHanded": true, "modifiers": { "weapon": 2, "weaponDice": 1 }, "cleave": true },
          { "id": "t_maul", "name": "T-Maul", "type": "weapon", "slot": "mainHand", "twoHanded": true, "modifiers": { "weapon": 3, "weaponDice": 1 }, "shoveOnSix": true },
          { "id": "t_hammer", "name": "T-Hammer", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 2 }, "crush": true },
          { "id": "t_spear", "name": "T-Spear", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 2 }, "attackRange": 2, "reach": true },
          { "id": "t_xbow", "name": "T-Crossbow", "type": "weapon", "slot": "mainHand", "twoHanded": true, "modifiers": { "weapon": 3 }, "attackRange": 8, "reload": { "odHand": 1 } },
          { "id": "t_heavy", "name": "T-Heavy", "type": "weapon", "slot": "mainHand", "twoHanded": true, "modifiers": { "weapon": 4 }, "attackRange": 9, "reload": { "odHand": 1, "wend": 1 } },
          { "id": "guard", "name": "Guard", "type": "shield", "slot": "offHand", "modifiers": { "block": 2 } },
          { "id": "lucky", "name": "Lucky", "type": "armor", "slot": "head", "lastChanceDie": true },
          { "id": "t_wings", "name": "T-Wings", "type": "accessory", "slot": "accessory", "wings": true },
          { "id": "t_hood", "name": "T-Hood", "type": "armor", "slot": "head", "altSlot": "accessory" },
          { "id": "t_cufflinks", "name": "T-Cufflinks", "type": "accessory", "slot": "accessory", "modifiers": { "armor": 1, "block": 1 } },
          { "id": "cap", "name": "Cap", "type": "armor", "slot": "head", "modifiers": { "hearts": 1 } },
          { "id": "boots", "name": "Boots", "type": "boots", "slot": "feet", "modifiers": { "moveDice": 1 } },
          { "id": "potion", "name": "Potion", "type": "potion", "usage": "singleUse", "effect": { "kind": "heal", "amount": 3 } },
          { "id": "bomb", "name": "Bomb", "type": "bomb", "usage": "singleUse", "effect": { "kind": "attack", "amount": 4, "range": 3, "areaRadius": 1 } },
          { "id": "scroll", "name": "Scroll", "type": "spell", "usage": "singleUse", "effect": { "kind": "attack", "amount": 3, "range": 3 } },
          { "id": "t_scroll", "name": "T-Scroll", "type": "spell", "usage": "singleUse", "stacks": true, "teaches": "flame_blast", "effect": { "kind": "attack", "amount": 3, "range": 3, "dieType": "fire" } },
          { "id": "firebolt", "name": "Firebolt", "type": "spell", "usage": "singleUse", "effect": { "kind": "attack", "amount": 3, "range": 3, "dieType": "fire" } },
          { "id": "wand", "name": "Wand", "type": "spell", "slot": "offHand", "usage": "charges", "charges": 2, "effect": { "kind": "freeze", "amount": 1, "range": 3 } },
          { "id": "icicle", "name": "Icicle", "type": "tool", "slot": "offHand", "effect": { "kind": "freeze", "amount": 1, "range": 4 } },
          { "id": "spear", "name": "Spear", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 2 }, "attackRange": 2, "reach": true },
          { "id": "boomerang", "name": "Boomerang", "type": "weapon", "slot": "offHand", "altSlot": "mainHand", "attackRange": 4, "attackBonus": 1, "stunOnSix": true },
          { "id": "trident", "name": "Trident", "type": "weapon", "slot": "mainHand", "twoHanded": true, "modifiers": { "weapon": 1 }, "attackRange": 2, "swimmerBonus": 1 },
          { "id": "helm", "name": "Helm", "type": "armor", "slot": "head", "swims": true },
          { "id": "picks", "name": "Picks", "type": "tool", "slot": "offHand", "stacks": true, "belts": true, "tool": { "for": "pickLock", "bonus": 1, "dice": 1, "snaps": true } },
          { "id": "t_torch", "name": "T-Torch", "type": "tool", "slot": "offHand", "altSlot": "mainHand", "stacks": true, "torch": true },
          { "id": "longsword", "name": "Longsword", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 1, "weaponDice": 1 }, "versatile": 1 },
          { "id": "tonic", "name": "Tonic", "type": "potion", "usage": "singleUse", "stacks": true, "effect": { "kind": "heal", "amount": 1 } },
          { "id": "coin", "name": "Coin", "type": "treasure", "value": 5 },
          { "id": "purse", "name": "Purse", "type": "treasure", "coins": true, "value": 3 },
          { "id": "gem", "name": "Gem", "type": "treasure", "value": 15 },
          { "id": "bolt_star", "name": "Star Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "star" } },
          { "id": "bolt_poison", "name": "Poison Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "poison" } },
          { "id": "bolt_ice", "name": "Ice Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "ice" } },
          { "id": "bolt_acid", "name": "Acid Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "acid" } },
          { "id": "bolt_necro", "name": "Necro Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "necro" } },
          { "id": "bolt_squall", "name": "Squall Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "squall" } },
          { "id": "bolt_radiate", "name": "Radiate Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "radiate" } },
          { "id": "bolt_life", "name": "Life Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "life" } },
          { "id": "bolt_earth", "name": "Earth Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "earth" } },
          { "id": "bolt_elektrik", "name": "Elektrik Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "elektrik" } },
          { "id": "bolt_psycho", "name": "Psycho Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "psycho" } },
          { "id": "bolt_shadow", "name": "Shadow Bolt", "type": "spell", "usage": "singleUse", "stacks": true, "effect": { "kind": "attack", "amount": 1, "range": 6, "dieType": "shadow" } },
          { "id": "t_frost", "name": "T-Frost", "type": "spell", "slot": "offHand", "usage": "charges", "charges": 2, "effect": { "kind": "attack", "amount": 2, "range": 3, "dieType": "ice", "chill": 1 } },
          { "id": "mender", "name": "Mender", "type": "tool", "usage": "singleUse", "stacks": true, "effect": { "kind": "repair" } },
          { "id": "drops", "name": "Drops", "type": "potion", "usage": "singleUse", "effect": { "kind": "cure", "cures": "blind" } },
          { "id": "holy", "name": "Holy", "type": "potion", "usage": "singleUse", "effect": { "kind": "cure", "cures": "blackHeart" } },
          { "id": "t_holy", "name": "T-Holy", "type": "potion", "usage": "singleUse", "stacks": true, "effect": { "kind": "holy", "cures": "blackHeart", "range": 3, "amount": 5 } },
          { "id": "t_burgle", "name": "T-Burgle", "type": "tool", "slot": "offHand", "tool": { "for": "pickLock, disarm, steal", "bonus": 1, "dice": 1, "snaps": true } },
          { "id": "t_bag", "name": "T-Bag", "type": "tool", "slot": "offHand", "stacks": true, "belts": true, "medicineBag": true },
          { "id": "t_bandage", "name": "T-Bandage", "type": "tool", "usage": "singleUse", "stacks": true, "bandage": true },
          { "id": "t_shovel", "name": "T-Shovel", "type": "weapon", "slot": "mainHand", "twoHanded": true, "shovel": true, "modifiers": { "weapon": 2 } },
          { "id": "t_doll", "name": "T-Doll", "type": "tool", "usage": "singleUse", "sinEater": true, "effect": { "kind": "sinEater", "range": 3, "areaRadius": 1 } },
          { "id": "storm", "name": "Storm", "type": "armor", "slot": "body", "rumbleType": "elektrik" },
          { "id": "brand", "name": "Brand", "type": "weapon", "slot": "mainHand", "modifiers": { "weapon": 1, "weaponDice": 1 }, "dieType": "ice" }
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
          { "id": "bones", "name": "Bones", "glyph": "k", "hearts": 2, "move": 2, "attackDice": 1, "defenseDice": 0, "reassembles": true, "undead": "lesser" },
          { "id": "t_ghoul", "name": "T-Ghoul", "glyph": "c", "hearts": 9, "move": 0, "attackDice": 0, "defenseDice": 0, "undead": "greater", "miasma": true },
          { "id": "boss", "name": "Boss", "glyph": "W", "hearts": 10, "move": 0, "attackDice": 1, "defenseDice": 0, "sweep": { "radius": 2, "every": 2 }, "stunImmune": true, "boss": true, "xp": 25 },
          { "id": "hoarder", "name": "Hoarder", "glyph": "h", "hearts": 1, "move": 0, "attackDice": 0, "defenseDice": 0, "dropChance": 100, "drops": 2, "xp": 7 },
          { "id": "knight", "name": "Knight", "glyph": "K", "hearts": 1, "move": 0, "attackDice": 0, "defenseDice": 1, "armor": 2 },
          { "id": "duelist", "name": "Duelist", "glyph": "D", "hearts": 1, "move": 0, "attackDice": 2, "defenseDice": 0, "weapon": 2 },
          { "id": "torch", "name": "Torch", "glyph": "t", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "dieType": "fire", "fireImmune": true },
          { "id": "sniper", "name": "Sniper", "glyph": "n", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "attackPattern": "ranged", "attackRange": 5 },
          { "id": "pickpocket", "name": "Pickpocket", "glyph": "T", "hearts": 2, "move": 4, "attackDice": 1, "defenseDice": 0, "odHandDice": 3, "thief": true },
          { "id": "mugger", "name": "Mugger", "glyph": "Y", "hearts": 1, "move": 0, "attackDice": 2, "defenseDice": 0, "odHandDice": 1, "mugger": true, "goldOnly": true, "separateAttacks": true },
          { "id": "eel", "name": "Eel", "glyph": "E", "hearts": 1, "move": 4, "attackDice": 1, "defenseDice": 0, "swimmer": true, "waterOnly": true },
          { "id": "fish", "name": "Fish", "glyph": "F", "hearts": 3, "move": 0, "attackDice": 0, "defenseDice": 0, "swimmer": true },
          { "id": "crab", "name": "Crab", "glyph": "Q", "hearts": 3, "move": 0, "attackDice": 0, "defenseDice": 0, "armor": 2, "shell": true },
          { "id": "frog", "name": "Frog", "glyph": "R", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "attackPattern": "reach", "attackRange": 3, "pulls": true },
          { "id": "jelly", "name": "Jelly", "glyph": "J", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "flying": true, "swimmer": true, "stunOnSix": true },
          { "id": "lurker", "name": "Lurker", "glyph": "x", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "grabs": true, "xp": 4 },
          { "id": "slugger", "name": "Slugger", "glyph": "U", "hearts": 3, "move": 0, "attackDice": 1, "weapon": 4, "defenseDice": 0 },
          { "id": "warden", "name": "Warden", "glyph": "V", "hearts": 1, "move": 0, "attackDice": 0, "defenseDice": 0, "rareDrop": { "card": "shield", "chance": 100 } },
          { "id": "champ", "name": "Champ", "glyph": "Z", "hearts": 3, "move": 0, "attackDice": 2, "defenseDice": 0, "knocksBack": true, "knockbackOnSix": true },
          { "id": "basher", "name": "Basher", "glyph": "o", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "knocksBack": true },
          { "id": "hexer", "name": "Hexer", "glyph": "H", "hearts": 1, "move": 0, "attackDice": 1, "defenseDice": 0, "attackPattern": "ranged", "attackRange": 4, "hexes": true },
          { "id": "raiser", "name": "Raiser", "glyph": "N", "hearts": 1, "move": 0, "attackDice": 0, "defenseDice": 0, "raises": "bones", "raiseMax": 2, "xp": 5 },
          { "id": "blob", "name": "Blob", "glyph": "u", "hearts": 1, "move": 2, "attackDice": 1, "defenseDice": 0, "engulfs": true },
          { "id": "zombie", "name": "Zombie", "glyph": "z", "hearts": 3, "move": 2, "attackDice": 1, "defenseDice": 1 },
          { "id": "sentient_shrub", "name": "Sentient Shrub", "glyph": "S", "hearts": 1, "move": 1, "attackDice": 1, "defenseDice": 0 },
          { "id": "venom", "name": "Venom", "glyph": "v", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "dieType": "poison" },
          { "id": "sprout", "name": "Sprout", "glyph": "L", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "dieType": "life" },
          { "id": "ghoul", "name": "Ghoul", "glyph": "G", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "dieType": "necro" },
          { "id": "hulk", "name": "Hulk", "glyph": "O", "hearts": 5, "move": 0, "attackDice": 0, "defenseDice": 0, "size": 2 },
          { "id": "spitter", "name": "Spitter", "glyph": "A", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "dieType": "acid" },
          { "id": "gust", "name": "Gust", "glyph": "w", "hearts": 3, "move": 0, "attackDice": 1, "defenseDice": 0, "dieType": "squall" }
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
          "keys": { "monster": 0, "pot": 0, "roomCleared": 0, "steal": 50 },
          "mimics": { "monster": "lurker", "chance": 0 }
        }
        """;

    public static ContentCatalog Catalog { get; } = ContentCatalog.Parse(CardsJson, MonstersJson, [ThemeJson], ContentCatalog.ReadEmbedded("spells.json"));
}
