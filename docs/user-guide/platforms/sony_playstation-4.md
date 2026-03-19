# Sony PlayStation 4

## Supported Installation Types

You can store your PS4 roms in RomM in the following manner.

## Supported File Formats

1. **PKG**: Storing the game as a PKG file in RomM.
    ```
    /roms/ps4/Bloodborne - Game of the Year Edition.pkg
    ```

    <img src="../../_assets/images/ps4_pkg_extractor.png " alt="PS4 Platform Screen" width="50%">

2. **Decrypted Folder Format**: Storing the game in a Decrypted Folder Format, provided it is in an archive and a sub-directory with the TITLE ID of the game.
    ```
    /roms/ps4/Tearaway_ Unfolded.rar
      ...
      |_ /CUSA01607/
    ```

## Updates, DLC, & Bonus Content

The system can install the base games as well as the Updates and DLCs if provided in the correct folder names.

- BONUS/: A collection of pkg/rap or files that the system will install.
- DLC/: A collection of pkg/rap files that the system will install.
- UPDATE/: A collection of pkg/rap files that the system will install.

## Examples

> The Base Game can be placed either in directly in the root of the archive, or in a subfolder with the Title ID.
> Installing a PKG file format will require you specify an external pkg extractor in the PS4 platform configuration.

**Base Game (PKG)**

```
Bloodborne - Game of the Year Edition [CUSA03173].rar/
  |_ Bloodborne - Game of the Year Edition.pkg
```

**Base Game (Decrypted Folder)**

```
Tearaway Unfolded [CUSA01607].rar/
  |_CUSA01067/<files>
```

**Base Game + Update + DLC (PKG)**

```
DriveClub [CUSA00003].rar/
  |_ DLC/
    |_ EP9000-CUSA00003_00-DCXTOURXXXXXX001-A0000-V0100.pkg
  |_ UPDATE/
    |_ PS4_Driveclub_CUSA00003_Patch_v1.28.pkg
  |_ PS4_Driveclub_CUSA00003.pkg
```

**Base Game + Update + DLC (Decrypted Folder)**

The base game files would be stored in the TITLE ID folder most importantly the `eboot.bin`.

```
Tearaway Unfolded [CUSA01607].rar/
  |_ CUSA01607/
  ...
  |_ DLC/
    |_ CUSA01607/
      |_ HOLIDAYPACK00000/
      |_ POPUPPACK0000000/
      |_ TORNAWAYPACK0000/
  |_ UPDATE/
    |_ CUSA01607-patch/
  |_ Bonus/
    |_ CUSA03007/
```