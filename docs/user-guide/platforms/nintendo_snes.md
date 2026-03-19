# 🎮 Super Nintendo Entertainment System (SNES)

The **Super Nintendo Entertainment System (SNES)** is a 16-bit home console developed by Nintendo.

---

# 📋 Summary

| Attribute | Value |
|----------|------|
| Platform | SNES |
| Manufacturer | Nintendo |
| Release Year | 1990 |
| Plugin Platform ID | snes |
| Status | ✅ Supported |

---

# 🕹️ Emulator(s) Targeted

| Emulator | Type | Supported | Notes |
|---------|------|-----------|------|
| RetroArch | Snes9x Libretro Core | ✅ Yes | Verified |

---

### Configuration

Document any required configuration:

- **Emulator**: Allows you to specify the Emulator you want to launch the game with.
- **Games Directory**: Allows you to specify where you want the games to be stored when installed through the plugin.
- **Game Layout**: Allows you to specify how you want the games to be stored:
  - **Install in Root**: Put the games in the root of the Games Directory itself.  *e.g. LaunchBox/Games/SNES/Aladdin.smc*
  - **Install in Subdirectory**: Put the games in a subdirectory of the Games Directory.  *e.g. LaunchBox/Games/SNES/Aladdin/Aladdin.smc*

------------------------------------------------------------------------

## 📦 Supported File Formats

These ROM / game formats are recognized by the plugin.

  Extension   Description      Supported
  ----------- ---------------- ------------------------
  `.smc`      SNES ROM image   ✅
  `.sfc`      SNES ROM image   ✅
  `.zip`      Compressed ROM   ✅

### Notes

-   Retroarch's SNES core allows for direct archive loading, so the system will not perform other steps aside from downloading and placement.

------------------------------------------------------------------------

## 🚀 Deployment Overview

Describe how this platform is deployed within the plugin environment.

### Typical Workflow

1.  Platform detected
2.  ROM files indexed
3.  Metadata retrieved
4.  Emulator mapped
5.  Launch command constructed

### Example Launch Flow

    User selects game
     → Plugin resolves platform
     → Emulator selected
     → Launch command generated
     → Emulator started

### Launch Command Example

``` bash
retroarch -L cores/snes9x_libretro.so "Super Mario World.smc"
```

------------------------------------------------------------------------

## 🖼️ RomM Asset Overview

Describe how the platform interacts with **RomM assets and metadata**.

  Asset Type    Supported   Notes
  ------------- ----------- -----------------------------
  Box Art       ✅          Downloaded via RomM API
  Screenshots   ✅          
  Videos        ⚠️          May require external source
  Manuals       ⚠️          Optional

### Example Endpoint

    https://romm.domain.com/rom/<id>/<platform>

### Asset Mapping

  Asset        Usage
  ------------ -------------------------
  Cover        Display in game library
  Screenshot   Preview
  Video        Gameplay preview

------------------------------------------------------------------------

## ⚠️ Special Considerations

Document anything unusual or platform-specific.

### Compatibility Notes

-   Certain ROM revisions may not work
-   Some games require BIOS files
-   Region differences may affect emulator compatibility

### BIOS Requirements (if applicable)

  BIOS File       Required   Notes
  --------------- ---------- -----------------
  `<bios file>`   ✅         `<description>`

### Known Issues

  Issue       Status         Notes
  ----------- -------------- -------------
  `<issue>`   Open / Fixed   `<details>`

------------------------------------------------------------------------

## 🔧 Developer Notes

Optional section for plugin developers.

Topics may include:

-   Endpoint differences
-   Special platform detection logic
-   Launch command quirks
-   Metadata overrides

------------------------------------------------------------------------

## 📚 References

-   `<Emulator Documentation>`
-   `<Platform Wiki>`
-   `<Relevant Plugin Code>`

------------------------------------------------------------------------

## 📝 Change Log

  Date       Change                  Author
  ---------- ----------------------- ----------
  `<date>`   Initial documentation   `<name>`
