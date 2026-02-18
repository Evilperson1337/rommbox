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

## Windows Platform

The windows platform is rather unique, in that it supports both INNO setups (e.g. GOG games) as well as portable games (e.g. Nightmare Kart on Itch.io).  The system relies on the game being in a very particular format to properly detect and install, as shown below.

### Enhancements
#### Folders
- **DLC**: This folder is where you would typically put your DLC executables (and required files) to install after the main game is installed.  By it's very nature this is usually only used when running a setup installer type, but you could run any INNO compliant installer with it.
- **OST**: This folder is where the soundtracks/music to your games would be included.  Currently you can specify a single place to put them (e.g. `C:\<USER>\Music`) and can be either the raw files or archive files, but the plan is to offer 3 options:
    1. Install to a central directory (`C:\<USER>\Music\GAME\`)
    2. Install in the game folder (`C:\<USER>\LaunchBox\Games\Windows\GAME\OST`)
    3. Install in the LaunchBox Music folder (`C:\<USER>\LaunchBox\Music\Windows\GAME`)
- **Pre-Reqs**: This is where you could store pre-requisites the game needs to run (e.g. vc_redistx64.exe).  This is primarily for portable games as the INNO installers would typically have/get them at runtime if needed.  It does not run them (at least not yet, still working out whether I want to do that), just places them in a configured location.
- **Bonus**: This folder is where bonus materials (e.g. Artworks, documentaries, etc.) would be placed.  As these vary wildly, I have not determined a good way to categorizing them to have it intelligently ascertain and place them - so it just copies the files to your configured bonus folder as is.

#### Files
- manifest.json: this is a special file that can be placed at the root of the archive that contains additional information.  A lot of it is currently scoped for future functionality (centralize save files for syncthing, pull metadata from LaunchBox DB, etc.) - but the main one that can be used currently is the *Executable* field - useful for situations where multiple game candidates (exes) are detected, you can tell it which one to use.

    ```json
    {
        "Name": "",
        "Version": "",
        "IDs": {
            "IGDB": "",
            "TGDB": "",
            "SGDB": "",
            "Moby": "",
            "LBID": "",
            "HLTB": ""
        },
        "Executable": "",
        "Arguments": [],
        "SaveLocation": "",
        "ConfigLocation": ""
    }
    ```

### Portable Games

Portable Games are extracted and copied to the install directory as is.  They are stored in the following format inside of an archive:
```
archive
│   manifest.json *optional   
│
└───Game Name
│   │   file1.dll
│   │   game.exe
│   │
│   └───subfolder1
│       │   file111.txt
│       │   file112.txt
│       │   ...
│   
└───Pre-Reqs *optional
└───DLC *optional
└───OST *optional
└───Bonus *optional
```
   
### Installer Games

Installer games are extracted from the archive and the setup executable in the root is run to install the game.  Installer games are stored in the following format inside of an archive:
```
archive
│   manifest.json *optional   
│
|   setup.exe
|   additional files...
│   
└───Pre-Reqs *optional
└───DLC *optional
└───OST *optional
└───Bonus *optional
```

## Common install failures

### “Services unavailable”

This can happen if LaunchBox cannot access its data manager or the plugin didn’t initialize. Close and reopen LaunchBox and try again.

### Download or extraction errors

- Check your connection and credentials.
- See [Troubleshooting](./troubleshooting.md) for logs and fixes.
