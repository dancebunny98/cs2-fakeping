# FakePing for CS2

FakePing changes the ping shown for a player in the CS2 scoreboard. Administrators can assign a fixed ping or a random value within a configured range. Assignments persist across reconnects.

## Requirements and installation

Requires CounterStrikeSharp API 1.0.376 and .NET 10. Build `src/FakePing.csproj` in Release mode. Copy `FakePing.dll` and the `lang` directory from `src/bin/Release/net10.0/` to `addons/counterstrikesharp/plugins/FakePing/`. Reload the plugin or restart the server.

## Commands

Commands require `@css/root`.

| Command | Effect |
| --- | --- |
| `css_fakeping <player> <ping>` | Set a fixed ping from 0 to 4095. |
| `css_fakeping <player> <min-max> <interval>` | Change ping within the range every `interval` seconds. |
| `css_fakeping_remove <player>` | Remove a temporary or saved assignment. |

Player lookup accepts a `#UserID`, full name, or name fragment. A permanent entry in `FakePingConfig.json` must be removed from that file before `css_fakeping_remove` can remove it.

## Configuration and translations

On first load the plugin creates `configs/plugins/FakePing/FakePingLocalization.json` with `{"Language":"ru"}`. Set `Language` to `ru` or `en` and reload the plugin. An unsupported value selects Russian. Edit `lang/ru.json` or `lang/en.json` to customize command messages. The selected file falls back to English when missing.

`FakePingConfig.json` holds permanent assignments. `FakePingData.json` holds saved command assignments. Translation settings use a separate file so the existing data formats stay compatible.

## Implementation notes

The plugin restores configured or saved assignments when a player connects. Dynamic entries track their next update time and select a value within the configured range. On each tick the current value is applied to the player resource so the scoreboard reflects it. Data is saved after command changes and when the plugin unloads.
