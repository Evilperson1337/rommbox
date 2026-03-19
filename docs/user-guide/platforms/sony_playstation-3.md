# Sony PlayStation 3

## Supported Installation Types

You can store your PS3 roms in RomM in the following manner.

## Supported File Formats

1. **Decrypted ISO**: Storing the game as a Decrypted ISO file in RomM.
    ```
    /roms/ps3/Uncharted.iso
    ```
2. **JB Folder Format**: Storing the game in a JB Folder Format, provided it is in an archive.
    ```
    /roms/ps3/Angry Birds Trilogy.zip
      ...
      |_ /PS3_GAME/
      |_ /PS3_DISC.SFB
    ```

## Updates & DLC
The system can install the base games as well as the Updates and DLCs if provided in the correct folder names.

- DLC/: A collection of pkg/rap files that the system will install.
- UPDATE/: A collection of pkg/rap files that the system will install.

## Examples

> The Base Game can be placed either in directly in the root of the archive, or in a subfolder with the games name.

**Base Game (Decrypted ISO)**
```
The Sly Collection.rar/
  |_ The Sly Collection [BCUS98246].iso
```

**Base Game (JB Folder)**
```
Angry Birds Star Wars.zip/
  |_PS3_GAME/
  |_PS3_DISC.SFB
```

**Base Game + Update + DLC (Decrypted ISO)**
```
Warhawk [BCUS98117].rar/
  |_ DLC/
    |_ Warhawk Broken Mirror Pack.pkg <- Content
    |_ UP9000-BCUS98117_00-BROKENMIRRORRC03.rap <- License
  |_ UPDATE/
    |_ UP9000-BCUS98117_00-WARHAWKBDPATCH15-A0150-V0100-PE.pkg
  |_ Warhawk [BCUS98117].iso
```

**Base Game + Update + DLC (JB Folder)**
```
Angry Birds Trilogy [BLUS31054].zip/
  |_PS3_GAME/
  |_PS3_DISC.SFB
  ...
  |_ DLC/
    |_ Angry Birds - Anger Management Pack.pkg <- Content
    |_ UP0002-BLUS31054_00-ANGRYBIRDSUEDLC1.rap <- License
  |_ UPDATE/
    |_ UP0002-BLUS31054_00-ANGRYBIRDSUEPAT2-A0102-V0100-PE.pkg
```