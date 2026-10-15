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
scenes/game/                 The game screen: board, tokens, dice tray, panels, event playback
scenes/ui/                   Shared UI pieces: palette, theme, portraits, card slots, hearts
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
| `--resultstest` | Two saved characters click their way out of the exit; checks that their results are saved and shown. Uses a throwaway save folder. |
| `--autoplay` | The debug party plays a generated dungeon with the rules-engine bot until the run ends, narrating to the console. Add `--fast` for 8× speed, and `--party N` for a party of N (up to 8). |

Each test exits with code 1 on its first failed check.

```powershell
powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1
```

## Art and sound

Everything in `assets/` is generated by `tools/build-assets.py` (Python 3, standard library only):

- **Sprites:** heroes, monsters (the Key Warden too), chests (plain, iron-bound and the vault's) and pots, drawn as
  16×16 text grids.
- **Procedural art:** floor, wall, cracked wall, stairs down and up, exit, spike and trap door, rubble, shallow
  and deep water and gate tiles, and the gold-trimmed UI frames.
- **Icons:** the stat symbols (a fist for Rumble, a boot for Wend, a hamsa for Od Hand), the Power Dice star and the keys.
- **Heading font:** a serif capitals pixel font, written as a BMFont.
- **Sound effects:** 8-bit style, synthesized from waveforms.
- **Music:** chiptune songs written as short note scores (`assets/audio/music/`): `title` for the
  menus, `dungeon` and `boss` during a run, and `victory` and `defeat` on the results screen. Real
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
