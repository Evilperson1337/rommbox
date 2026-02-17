# Installing games

You can install games in two ways:

1. **During import** from the Games page.
2. **From the game context menu** in LaunchBox.

## Install during import

On the **Games** page:

1. Set the action to **Install** for the games you want.
2. Select **Import** to run the import and install process.

This downloads the ROM and applies any platform install options you configured.

## Install from a game’s context menu

1. In LaunchBox, right‑click a RomM‑sourced game.
2. Open the **RomM** submenu.
3. Select **Install Game**.

    <img src="../_assets/images/romm_per-game_menu.png" alt="RomM Per-Game Menu" width="50%">

An install progress window appears while the download runs.

## Where games are installed

Install location depends on platform settings:

- **Game Directory** in the platform’s install options.
- Platform defaults or LaunchBox platform folders if you leave the field empty.

## Optional content (Windows platforms)

If enabled for the platform, these items can be installed:

- Soundtracks (OST)
- Bonus content
- Prerequisites

These options are configured in **Platforms → Configure**.

## Uninstall a game

1. Right‑click a RomM‑sourced game.
2. Open the **RomM** submenu.
3. Select **Uninstall Game**.

This removes local files and marks the game as not installed in LaunchBox.

> **Warning:** Uninstall removes local files. It does not remove the LaunchBox game entry.

## Play on RomM

If the game supports it, the context menu includes **Play on RomM**, which opens the RomM play page in your browser.

## Common install failures

### “Services unavailable”

This can happen if LaunchBox cannot access its data manager or the plugin didn’t initialize. Close and reopen LaunchBox and try again.

### Download or extraction errors

- Check your connection and credentials.
- See [Troubleshooting](./troubleshooting.md) for logs and fixes.
