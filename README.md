# Mouse Double Tap Dash

[![Header](docs/images/header-1280x365.png)](docs/images/header-1280x365.png)

Double-tap a movement key to dash in **MOUSE: P.I. For Hire**.

A small BepInEx 5 plugin that turns a double tap on a movement key into the
game's Dash action. It reads the game's current input binding at runtime, so it
keeps working after a Dash rebind.

## Features

- Double-tap **W / A / S / D** (physical keys; **Z / Q / S / D** on AZERTY) to dash.
- Dash key read from the game's runtime binding, with a profile fallback.
  A Dash rebind is followed within about 2 seconds.
- Toggle in **Settings > Controls**: `Double-tap dash` (Yes / No, localized).
- Configurable double-tap window and cooldown.
- Single-player only. No anti-cheat is involved.

## Requirements

- **MOUSE: P.I. For Hire** (tested with the 14/10/2026 build).
- **BepInEx 5.4.23.5 x64**, using the game-specific **unstripped** setup for
  MOUSE: P.I. For Hire.
  - Nexus: **Unstripped BepInEx for M.P.I.**, or the equivalent package on ModDB.
  - Do **not** use a stock BepInEx package: the game ships stripped BCL
    assemblies and the preloader will fail.

## Installation

1. Install the unstripped BepInEx package into the game root (the folder that
   contains `MOUSE.exe`).
2. Launch the game once, then check that `BepInEx/LogOutput.log` exists.
3. Extract `MouseDoubleTapDash-1.2.2.zip` into the game root. The result must be:

   ```
   <game>/BepInEx/plugins/MouseDoubleTapDash.dll
   ```

4. Launch the game.
5. Open **Settings > Controls** and set **Double-tap dash** to **Yes**.

There is no keyboard shortcut for enabling the mod.

## Configuration

File: `BepInEx/config/com.cono.mousedoubletapdash.cfg`

```ini
[General]
Enabled = true
DashKeyOverride =

[Timing]
DoubleTapWindowMs = 250
CooldownMs = 350

[Debug]
Verbose = false
```

| Option | Description |
| --- | --- |
| `Enabled` | Enables or disables the mod. Can also be changed from the in-game settings. |
| `DashKeyOverride` | Forces the Dash key, for example `LeftShift` or `LeftAlt`. Leave empty for automatic detection. Keyboard keys only. |
| `DoubleTapWindowMs` | Maximum delay between the two taps, in milliseconds. |
| `CooldownMs` | Minimum delay between two mod-triggered dashes, in milliseconds. |
| `Verbose` | Writes a log line for every dash triggered. |

Change the values in the file and restart the game. The in-game toggle is saved
immediately.

## Build from source

Requirements:

- .NET SDK (tested with .NET 9).
- A local copy of **MOUSE: P.I. For Hire** with the unstripped BepInEx package
  installed, so the references in the `.csproj` can be resolved.

```bash
git clone git@github.com:Drasenix/mouse-pi-double-tap-dash.git
cd mouse-pi-double-tap-dash

MOUSE_GAME_DIR="/path/to/MOUSE" \
  dotnet build src/MouseDoubleTapDash.csproj -c Release -o out
```

Then copy `out/MouseDoubleTapDash.dll` to `<game>/BepInEx/plugins/`.

You can also pass the game path directly:

```bash
dotnet build src/MouseDoubleTapDash.csproj -c Release -o out \
  -p:GameDir="/path/to/MOUSE"
```

## Known limitations

- The double tap is detected on the keyboard only. If Dash is bound to a mouse
  button or a gamepad, automatic detection falls back to a keyboard key.
  `DashKeyOverride` only accepts keyboard keys.
- The mod synthesizes a Dash key press; it does not bypass the game's own input
  handling or cooldowns.
- If the settings entry is missing after a game update, set `Enabled` in the
  config file and check `BepInEx/LogOutput.log` for `MouseDoubleTapDash`.

## Compatibility

- Tested with the 14/10/2026 game build and BepInEx 5.4.23.5 (unstripped).
- Linux / Steam Proton / Lutris: install the Windows BepInEx package in the game
  folder. No special launch options are required.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## Credits

- BepInEx Team and HarmonyX.
- KZekai and the authors of the unstripped BepInEx setup for
  MOUSE: P.I. For Hire.
- Fumi Games for the game.

## License

Plugin code is released under the MIT license. See [LICENSE](LICENSE).

Do not redistribute the game's assemblies or the BepInEx unstripped package
without following their respective licenses.
