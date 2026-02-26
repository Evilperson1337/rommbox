# Understanding the Windows Games Integration

Windows Games are inherently a different beast from a typically emulator/ROM setup, but are supported to an extent.  In order to prevent trying to account for every possibility the games would need to be standardized so it can be programmatically managed.

## Supported Types
The 2 types that are supported will be for Installer-based games, and portable games.  The type of game (how the system will deploy it) can be determined either by the file/folder sturcture inside of the root of the archive, or by the file name tags.

 - **Portable Example**: *NightmareKart (rev 1.0) (Portable).zip*
 - **Installer Example**: *Bridge Constructor (rev 1.12) (Installer).zip* 

### Portable Games
Portable games are games that do not require an installer to deploy, and are typically self contained applications - or games that contain all the files they need inside a single folder.  Games in this category are typically indie games (e.g. *NightmareKart* on itch.io).

#### Folder Structure
The file/folder structure it relies on for a portable installation will look for a folder in the root of the archive that does not meet one of the special enhancements (see section below) - an example is as follows:
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
Installer games are games that run an installer to deploy the game, and will most likely be the majority of ones you run.  Currently only the INNO-type setups are supported, as these are the installer system used by the GOG offline backup installers.  Other types of installers may be added in the future if I find one that I can test with.

#### Folder Structure
The file/folder structure it relies on for an installer installation will look for an INNO setup executable in the root of the archive - an example is as follows:
```
archive
│   manifest.json *optional   
│
|   setup.exe <- Base Game setup.exe and files
|   additional files...
│   
└───Pre-Reqs *optional
└───DLC *optional
└───OST *optional
└───Bonus *optional
```

### Enhancements
The system can also deploy enhancements if the archive has them in the root of the archive - the following are supported:

#### Files
- manifest.json
    - This file specifies certain things to the system, either for functionality now - or future planned functionality.  See the example [manifest.json](../../manifest.json) for a template, or below:
        ```
        {
        "Name": "The Long Dark", <- Name of the Game
        "Version": "2.51.173364.173361", <- Version of the game
        "IDs": {
            "IGDB": "8347", <- IGDB ID
            "TGDB": "", <- TheGamesDB ID
            "SGDB": "305620", <- Steam Game App ID
            "Moby": "82832", <- MobyGames ID
            "LBID": "22959", <- LaunchBox ID
            "HLTB": "21471" <- HowLongToBeat ID
        },
        "Executable": "%GAME_DIR%/tld.exe", <- Executable to run the game, 
                                            %GAME_DIR% resolves to where the game is installed
        "Arguments": [], <- Any arguments you want to pass to the executable when launching
        "SaveLocation": "%LOCALAPPDATA%/Hinterland/TheLongDark/", <- Location to the save files
        "ConfigLocation": "%LOCALAPPDATA%/Hinterland/TheLongDark/" <- Location to the config files
        }
        ```

#### Folders
**DLC**: This is where you would put your DLC INNO setup installers and their needed files, not in subfolders.  It will install into the same directory as the base game was installed.

```
DLC/
    |_ setup-dlc.exe
    |_ setup-dlc-1.bin
    |_ setup-dlc_2.exe
    |_ setup-dlc_2-1.bin
    ...
```

**UPDATE**: Similar to the DLC enhancement folder, this would be where you put patch/update files to have the system run to update the base game.

**Bonus**: This is where you would place your bonus content like behind-the-scenes, concept art, etc.  Since this content varies wildly, I haven't yet worked out how to standardize it, so it just copies this as is to whereever you have it configured in the Windows platform configuration.

**OST**: This is where you would put the soundtrack files, either the raw audio files themselves or stored in an archive.  They would be deployed wherever you have it configured in the Windows platform configuration.

**Pre-Reqs**: This is where you would put any pre-requisites to run necessary for the game to launch (e.g. *vc_redist_x64.exe*).  The system does not run them, but will put them whereever you have them configured in the Windows platform configuration so you can run them if needed.