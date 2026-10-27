# Woad

A turn-based roguelite dungeon-crawler board game for 1–8 local players, built with Godot 4.7 and C# on .NET 10.

The design is in [docs/MVP-Design.md](docs/MVP-Design.md).

## Requirements

| Tool | Version | Notes |
| --- | --- | --- |
| Godot Engine, **.NET edition** | 4.7.2 | The standard (non-.NET) build can't run C#. |
| .NET SDK | 10.0.x | Pinned by `global.json`. |
| Git | any recent | |

## Setting up a new Windows machine

1. Install Git if it isn't there yet (`winget install --id Git.Git -e`), then clone the repo.
2. From the repo root, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\setup-dev.ps1
   ```

   The script uses winget to install or upgrade Git, the .NET 10 SDK and the Godot .NET editor. It
   installs the Godot version that `Woad.csproj` names, so every machine matches the project. It is safe
   to re-run.
3. Open a new terminal so PATH changes apply, then check the build:

   ```powershell
   dotnet test Woad.sln
   ```

4. Open the project in Godot: launch the Godot .NET editor, choose **Import**, and select `project.godot`.

On macOS or Linux, install the .NET 10 SDK and the Godot 4.7.2 .NET build from
[godotengine.org/download](https://godotengine.org/download), then follow steps 3 and 4.

**Editor support:** JetBrains Rider has built-in Godot support. VS Code works with the *C# Dev Kit* and
*godot-tools* extensions. Visual Studio works too.

## Upgrading Godot

Godot rewrites the `Godot.NET.Sdk/x.y.z` line in `Woad.csproj` to match whichever editor opens the
project. Everyone working on the project should use the same editor version. To move to a new version:

1. Upgrade with `winget upgrade --id GodotEngine.GodotEngine.Mono -e`.
2. Open the project once so Godot updates `Woad.csproj`.
3. Commit that change. `tools/setup-dev.ps1` then installs the new version on other machines.

## Project layout

```
project.godot, Woad.csproj   Godot game project (repo root): scenes, UI, input, presentation
scenes/main/                 Main.tscn, the entry scene: owns content and saves, swaps screens
scenes/menu/                 Main menu, character list, character editor, party setup
scenes/game/                 The game screen: board, tokens, dice bar, hit effects, panels, event playback
scenes/ui/                   Shared UI pieces: palette, theme, looks, crests, card slots, hearts
scenes/debug/                Synthetic mouse/keyboard driver used by the debug UI tests
src/Woad.Core/               Pure C# rules engine: no Godot references allowed
  Rules/GameSession*.cs        Executes commands (Commands/) and returns events (Events/)
  Model/                       Run state the game draws from: heroes, monsters, rooms, cards
  Characters/                  Saved characters, point-buy, levels, JSON character storage
  Content/Data/                Cards, monsters and dungeon themes as JSON (embedded in the DLL)
tests/Woad.Core.Tests/       xUnit tests for Woad.Core
docs/                        Design documents
tools/                       Dev-machine scripts
```

`src/`, `tests/` and `docs/` contain a `.gdignore` file so the Godot editor doesn't import them.

## Common commands

```powershell
dotnet build Woad.sln   # build game + core + tests
dotnet test Woad.sln    # run the rules-engine tests
```

Run the game with F5 in the Godot editor. You can also run it from a terminal with
`Godot_v4.7.2-stable_mono_win64 --path .`. Create a character from **Characters**, then choose
**Play** to seat 1–8 characters and start a run. Each run generates a new three-floor dungeon from
its seed. A character created as a **Companion** joins the player seated before it; the game plays it,
following that player's orders. The party screen's **Friendly fire** switch lets heroes hurt each other.

GitHub runs the build and the rules tests on every push and pull request (`.github/workflows/tests.yml`);
the results show on the repository's **Actions** tab and on each pull request.

### Smoke tests

`tools/smoke-test.ps1` builds everything, runs the rules tests (including bots playing whole
generated dungeons), and then runs the game headless in six debug modes. Pass the mode after `--`
on the Godot command line:

| Flag | What it does |
| --- | --- |
| `--menutest` | Creates two characters and a companion, seats them, starts a run, orders the companion to hold, leaves the run, and deletes a character, using synthetic clicks and typing. Saves go to a throwaway temp folder. |
| `--uitest` | Puts a fixed debug party in a single room and plays two turns by pushing real mouse clicks through the UI (cards, targeting, Luck, moving, equipping), checking each result. |
| `--floortest` | Clicks through a door and down the stairs in a tiny three-room setup, checking that the view follows the hero. |
| `--hazardtest` | Shoves a slimeling into a pit that leads to the room below, then leaps over the pit, checking where the hero comes down. |
| `--keeltest` | Walks a one-heart hero onto hidden spikes so they keel over, rouses them from beside, then rolls their Keel Die until they stand back up or die and leave a tombstone. |
| `--firetest` | Puts a tough hero among Cinder Imps until one sets them alight, then clicks Drop & roll (fire out, prone, turn over) and, next turn, Get up. |
| `--cameratest` | In a 15x13 hall bigger than the view, checks the view starts on the first hero, that a right-drag pans no further than keeps them in view, and that it scrolls across to the second hero on their turn. |
| `--bombtest` | Throws a bomb into a huddle of slimes and checks it lies lit through two turns and goes off at the end of the thrower's next turn. |
| `--rumbletest` | Checks W rolls the Wend dice, clicking a monster no longer attacks, that Space readies Rumble with the monster in reach, that a click off target puts it away, that double-clicking the monster aims Rumble without attacking, and that the Rumble button then a click attacks. |
| `--loottest` | Checks a stack of lockpicks fills one slot, dropping part of a stack asks how many, a bag of loot opens to Take one thing or Take all (the picks rejoin their stack), and an opened chest shows what's inside. |
| `--tradetest` | Checks clicking a companion beside you opens trading, giving part of a stack asks how many, its owner can have it equip a sword, and the Orders panel's Find me button gives the order. |
| `--companiontest` | Checks a companion ordered to Fight from the Orders panel walks over to a slime and attacks it. |
| `--spelltest` | Checks the Frost Wand freezes a slime and the Flame Scroll burns another (and is used up), by selecting each, pressing Use and clicking the slime. |
| `--dietypetest` | Reads eight scrolls of typed dice (ice, lightning, poison, wind, earth, life, mind, shadow) at an ogre, a skeleton, a scorpion and a necromancer, digging out or tending her eyes when she must, and checks every figure shows the tokens the rules give it and that typed dice show in their colors. |
| `--partytest` | Checks the Party button says when there's a point to spend, opens the Party menu, spends Aria's point on a heart and tells Pip to pick up loot. |
| `--bosstest` | In a generated Emerald Keep's boss room, Aria orders Pip to Fight and ends her turns for four rounds, checking Pip attacks the Iron Warden's company. |
| `--monstertest` | Checks clicking a chest that's a Mimic wakes it on the board (and its grab shuts off Wend), then plays three rounds against an Ogre, a Necromancer (checking it raises the dead), a Hexer and a Gelatinous Cube. |
| `--encountertest` | On a fresh Woarld kept in memory: steps onto a toll post (Suss Out, then Turn back, which Cowards note), lets a band catch up and chooses Fight, and Scouts. |
| `--skirmishtest` | A fight under a bridge where the Bridge Troll strikes first, checking it fills four tiles. |
| `--wanderertest` | On a fresh Woarld kept in memory, goes to the day's first wandering merchant, opens their wares with the Trade button and buys one. |
| `--merchanttest` | Frees Silas, a merchant chained beside the hero, with a key; opens his wares and buys one; then checks he follows the hero. |
| `--dentest` | Walks a hero through the door into the Ogre Champ's den, checks its song starts and the Champ waits on its platform, then fights it for a few rounds. |
| `--clicktest` | Checks that double-clicking a far-off monster rolls the Wend dice, clicking it again walks up to it with Rumble aimed, R puts Rumble away and back, and one more click attacks. |
| `--resultstest` | Two saved characters click their way out of the exit; checks that their results are saved and shown. Uses a throwaway save folder. |
| `--autoplay` | The debug party plays a generated dungeon with the rules-engine bot until the run ends, narrating to the console. Add `--fast` for 8× speed, `--party N` for a party of N (up to 8), and `--theme sunken_temple` for the second dungeon. |

Each test exits with code 1 on its first failed check.

```powershell
powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1
```

## Art and sound

Everything in `assets/` is generated by `tools/build-assets.py` (Python 3, standard library only), except the
body text font, Jersey 10 (`assets/fonts/Jersey10-Regular.ttf`, SIL Open Font License in `Jersey10-OFL.txt`):

- **Sprites:** heroes, monsters (the Key Warden too), chests (plain, iron-bound and the vault's) and pots, drawn as
  16×16 text grids.
- **Procedural art:** floor, wall, cracked wall, stairs down and up, exit, spike and trap door, rubble, shallow
  and deep water and gate tiles, and the gold-trimmed UI frames.
- **Icons:** the stat symbols (a fist for Rumble, a boot for Wend, a hamsa for Od Hand, a shield for Defense, a clover for Luck), the Power Dice star and the keys.
- **Heading font:** a serif capitals pixel font, written as a BMFont.
- **Sound effects:** 8-bit style, synthesized from waveforms.
- **Music:** chiptune songs written as short note scores (`assets/audio/music/`): `title` for the
  menus, `dungeon`, `temple` and `boss` during a run, `ogre` in the Ogre Champ's den, and `victory` and `defeat` on the results screen. Real
  tracks can replace them as `.ogg`, `.mp3` or `.wav` under the same names; the game loops them
  itself (except the two results tunes).

Everything is original art in a *Link to the Past*–inspired palette. To change something, edit the
script, re-run it and let Godot import the result (open the editor, or run Godot with
`--headless --import`):

```powershell
python tools/build-assets.py
```

Generation is deterministic, and the smoke test fails if the committed assets don't match the
script. PNGs are written uncompressed, so every machine and Python version produces identical files. To swap a placeholder for real art, remove that entry from the script and commit the new
file under the same path. The game looks files up by name: `assets/themes/<theme>/tiles/`,
`assets/themes/<theme>/monsters/<monster id>.png`, `assets/heroes/`, `assets/ui/`, `assets/audio/`.
Anything missing falls back to simple shapes, or silence.

## Save data

Each character is saved as one JSON file under Godot's user data folder. On Windows that's
`%APPDATA%\Godot\app_userdata\Woad\characters\`. Files that can't be read are skipped and listed on
the Characters screen. Cards that no longer exist in the game are dropped, with a warning.
The music and sound-effect volumes from **Settings** are saved next to that folder, in `settings.cfg`.

## Sharing a build on itch.io

The game is published at [cdyosef.itch.io/woad](https://cdyosef.itch.io/woad) (a restricted page:
share it with a password or download keys from its **Distribute** tab). One-time setup:

- Godot's .NET export templates for this Godot version, in `%APPDATA%\Godot\export_templates\`
  (Editor → Manage Export Templates, or unpack the `.tpz` from Godot's GitHub releases there).
- [butler](https://itch.io/docs/butler/), itch.io's upload tool, unzipped into `tools\butler` (git
  ignores it) or on the PATH, signed in once with `& .\tools\butler\butler.exe login` (it opens your
  browser).

Then build and upload in one go (the build lands in `export/windows`, which git ignores):

```powershell
powershell -ExecutionPolicy Bypass -File tools/publish-itch.ps1 -Target cdyosef/woad
```

`-SkipUpload` only builds. Each upload goes to the page's `windows` channel, labeled with the game's
version (`config/version` in `project.godot`, now "2.0 Open Woarld") and the git commit; the itch app
updates players automatically. The build isn't code-signed, so Windows shows
"Windows protected your PC" the first time: **More info → Run anyway**.
