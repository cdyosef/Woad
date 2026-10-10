# Woad

A turn-based roguelite dungeon-crawler board game for 1–4 local players, built with Godot 4.7 and C# on .NET 10.

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
scenes/game/                 The game screen: board, tokens, dice tray, panels, event playback
scenes/ui/                   Shared UI pieces: palette, theme, card slots, hearts
src/Woad.Core/               Pure C# rules engine: no Godot references allowed
  Rules/GameSession*.cs        Executes commands (Commands/) and returns events (Events/)
  Model/                       Run state the game draws from: heroes, monsters, rooms, cards
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
`Godot_v4.7.2-stable_mono_win64 --path .`. For now it opens straight into a demo room with
two hot-seat heroes, Aria and Bran.

### Smoke tests

`tools/smoke-test.ps1` builds everything, runs the rules tests, and then runs the game headless
in two debug modes. Pass the mode after `--` on the Godot command line:

| Flag | What it does |
| --- | --- |
| `--uitest` | Plays the first two turns by pushing real mouse clicks through the UI and checks each result. Exits 1 on failure. |
| `--autoplay` | A bot plays the room until it's cleared or everyone is down, narrating to the console. Add `--fast` for 8× speed. |

```powershell
powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1
```

## Save data

Characters are saved as JSON under Godot's user data folder: `%APPDATA%\Godot\app_userdata\Woad\`
on Windows.
