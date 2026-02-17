# Managing content

After importing games, you can manage them from the LaunchBox library and the RomM context menu.

## View RomM games in LaunchBox

Imported games appear in your LaunchBox library like any other game. The plugin sets the **Source** to “RomM”.

## Re‑import or update metadata

To refresh data from RomM:

1. Open **Tools → RomM → Games**.
2. Select the platform.
3. Choose **Merge** or **Import** for the games you want to refresh.

> **Tip:** Use **Merge** if you want to update an existing LaunchBox entry rather than creating a new one.

## Manage installed content

Use the context menu to manage local content:

1. Right‑click a RomM game in LaunchBox.
2. Open **RomM**.
3. Select **Install Game** or **Uninstall Game**.

## Save data

You can download save data during import using the **Saves** checkbox on the Games page. Save files are stored under:

- `LaunchBox\Saves\<Platform>`

If you need to re‑download saves, run another import with **Saves** enabled.

> **Note:** Uploading saves back to RomM is not exposed in the UI.

## Removing games from LaunchBox

The plugin does not remove LaunchBox entries when you uninstall games. To delete a game from your library:

1. Select the game in LaunchBox.
2. Use the standard LaunchBox delete/remove option.

> **Warning:** Deleting a game in LaunchBox is separate from uninstalling local files through the RomM menu.
