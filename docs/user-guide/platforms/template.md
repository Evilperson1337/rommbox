# 🎮 `<Platform Name>`{=html}

> Short description of the platform and how it is supported within the
> plugin.

------------------------------------------------------------------------

## 📋 Summary

  Attribute            Value
  -------------------- ---------------------------------------------------
  Platform             `<Platform Name>`
  Manufacturer         `<Company>`
  Release Year         `<Year>`
  Plugin Platform ID   `<internal id if applicable>`
  Status               ✅ Supported / ⚠️ Experimental / ❌ Not Supported
  Default Emulator     `<primary emulator>`

**Overview**

Provide a short overview describing:

-   What the platform is
-   Why it is supported in the plugin
-   Any high-level information about how it works within the system
-   Any major limitations

------------------------------------------------------------------------

## 🕹️ Emulator(s) Targeted

The plugin supports the following emulator implementations for this
platform.

  ------------------------------------------------------------------------
  Emulator            Type          Supported                Notes
  ------------------- ------------- ------------------------ -------------
  `<Emulator Name>`   Standalone /  ✅ Yes / ⚠️ Partial      `<Notes>`
                      Libretro                               

  `<Emulator Name>`   Standalone /  ✅ Yes / ⚠️ Partial      `<Notes>`
                      Libretro                               
  ------------------------------------------------------------------------

### Default Emulator

`<emulator name>`

Explain why this emulator is the default (accuracy, compatibility,
performance, etc).

### Configuration

Document any required configuration:

    Example configuration
    --fullscreen
    --core snes9x

------------------------------------------------------------------------

## 📦 Supported File Formats

These ROM / game formats are recognized by the plugin.

  Extension   Description      Supported
  ----------- ---------------- ------------------------
  `.smc`      SNES ROM image   ✅
  `.sfc`      SNES ROM image   ✅
  `.zip`      Compressed ROM   ⚠️ Depends on emulator

### Notes

-   Some emulators support **compressed ROMs**
-   Multi-file formats may require special handling
-   Disc based systems may require cue/bin structures

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
