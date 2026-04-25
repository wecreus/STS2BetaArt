# Beta Art Toggle

A Slay the Spire 2 mod that adds a **Beta Art** checkbox to the card inspect screen. Just like Slay the Spire 1. For cards that shipped with original beta portraits, the checkbox lets you toggle between the current and beta art on a per-card basis. Your choices persist between game restarts.

## Features

- **Beta Art checkbox** appears next to the existing "View Upgrade" checkbox
- **Per-card toggle** — enabling beta art for one card doesn't affect others
- **Immediate refresh** — hand, compendium, and all other views update instantly when toggled
- **Persistent** — saved to `user://betaart_enabled.txt`, independent of game save files; removing the mod leaves saves untouched

## Installation

1. Copy `BetaArt.dll` and `BetaArt.json` into your mods folder:
   - **Windows/Linux:** `<game>/mods/`
   - **macOS:** `SlayTheSpire2.app/Contents/MacOS/mods/`
2. Enable the mod in-game from the mods menu.

## Building from Source

Requires .NET 9 SDK and a copy of `sts2.dll` from the game installation.

```
dotnet build -c Release
```


## Compatibility

- Does not affect gameplay 
- Does not modify or read game save files
