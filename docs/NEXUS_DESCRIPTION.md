# Mouse Double Tap Dash

Double-tap a movement key to dash in MOUSE: P.I. For Hire.

- ZQSD on AZERTY / WASD on QWERTY (physical keys).
- The dash key is read from the game's runtime input binding, with a profile fallback.
  Rebinding Dash is followed within about 2 seconds.
- Toggle in **Settings > Controls**: `Double-tap dash` (No/Yes).

## Requirements

- **BepInEx 5.4.23.5 x64**, using the game-specific **unstripped** setup for MOUSE: P.I. For Hire:
  - Nexus: **Unstripped BepInEx for M.P.I.**
  - or the equivalent BepInEx package for MOUSE on ModDB.

## Installation

1. Install the unstripped BepInEx package into the game root (where `MOUSE.exe` is).
2. Extract this archive into the game root:
   ```
   <game>/BepInEx/plugins/MouseDoubleTapDash.dll
   ```
3. Launch the game.
4. Open **Settings > Controls** and set **Double-tap dash** to **Yes**.


## Uninstall

Delete:

```
BepInEx/plugins/MouseDoubleTapDash.dll
```

## Credits

- BepInEx Team and HarmonyX.
- KZekai and the authors of the unstripped BepInEx setup for MOUSE: P.I. For Hire.
- Fumi Games for the game.

## Permissions

Plugin code is released under the MIT license. Do not redistribute the game's assemblies or the BepInEx unstripped package without following their respective licenses.
