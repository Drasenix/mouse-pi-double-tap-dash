# Changelog

## 1.2.2
- Fixed a startup issue where the mod could force the game's `InputManager` to initialize before the Input System was ready. This caused `Input System not yet initialized` exceptions and a `NullReferenceException` in `LoadInputRebindings`.
- The dash key now uses the profile binding until the game's runtime bindings are loaded, avoiding a brief fallback to the default key.
- The synthetic dash press is now released even when the game is paused, the menu is open, or the mod is disabled.
- Hardened the settings menu integration: Harmony patches are applied once, with per-patch guards and a retry backoff.
- Fixed a potential label leak when settings rows are reused for another setting.
- Bool value fallback labels are now Yes/No instead of hardcoded French labels.
- Removed the obsolete `ToggleKey` config entry on startup.

## 1.2.1
- Dash key now prioritizes the game's runtime input binding (InputManager), with the profile as fallback.
- Added the `DashKeyOverride` config option for manual control.
- Added refresh hooks for input rebind load/save events.

## 1.2.0
- Removed the F8 shortcut. The mod is now toggled from Settings > Controls.
- Added a proper settings entry using the game's Stepper style and localized Yes/No labels.
- Dash key detection added and refreshed every 2 seconds.
- Added config entries for enabled state and timing.

## 1.1.0
- Added the Settings > Controls entry.
- Added BepInEx configuration and active input detection.

## 1.0.0
- Initial release: double-tap dash.
