# Platform Configuration Improvements

## 1. Executive Summary

The current platform configuration UI is only partially plugin-aware. It already receives a plugin descriptor from the selected installer, but the dialog is still built as one large static form with a small number of conditional visibility checks. That creates three core problems:

1. The screen is organized around persisted mapping properties rather than around actual plugin capabilities.
2. Several plugin-specific settings exist in platform installers but are not surfaced in the current UI or are not fully wired through the settings mapper.
3. Global emulator/core fields remain visible even when the selected plugin hardcodes its emulator behavior or expects a different override model.

The codebase already contains the beginnings of a better architecture:

- installers expose plugin metadata through `GetConfigDescriptor()`
- the registry resolves the active config plugin key
- the configuration dialog already conditionally shows some fields by descriptor key

The redesign should evolve that into a plugin-aware / capability-aware form system where:

- the selected plugin declares the sections and fields it owns
- shared platform concepts such as install directory, emulator selection, archive handling, and tool-path selection are reused through common field descriptors
- emulator-specific overlays are layered on top only when relevant
- plugins with minimal needs stay minimal
- plugins with explicit tooling requirements surface those tools directly

The recommended direction is not a visual rewrite. It is a structural refactor of the existing configuration dialog so it keeps the current look and interaction patterns while becoming data-driven and scalable.

## 2. Plugin Inventory and Configuration Requirements

This section summarizes what each real installer in the repository actually needs, based on installer metadata, install logic, mappers, and related tests.

### Windows

- **Plugin / platform:** `windows`
- **Primary workflow:** native Windows installer/content pipeline
- **Associated emulator/tooling:** no emulator required
- **Required settings:** none exposed by plugin descriptor
- **Optional settings used today:** global enhanced-install settings such as installer mode, silent args, OST/bonus/pre-req locations
- **Supported formats / behavior:** installer-oriented workflow, supports DLC and updates, uses staging and executable discovery
- **Validation / consumption notes:** behavior is driven by enhanced install settings rather than plugin descriptor fields
- **UI implications:** should primarily show enhanced-install workflow fields; emulator/core fields are irrelevant

### Arcade

- **Plugin / platform:** `arcade`
- **Primary workflow:** move a `.zip` ROM set into the arcade directory and launch it through MAME-style or RetroArch-style arguments
- **Associated emulator/tooling:** inferred from emulator selection / ROM settings; supports MAME or RetroArch + FBNeo style launch behavior
- **Required settings:** none exposed by plugin descriptor
- **Optional settings used today:** global games directory; associated emulator matters; archive-preserve behavior matters indirectly
- **Supported formats / behavior:** `.zip` only; archive-centric; extraction is intentionally skipped in the pipeline for arcade flows
- **Validation / consumption notes:** installer resolves emulator mode from ROM/emulator settings, not from plugin-specific config fields
- **Hidden assumptions:** because `GetConfigDescriptor()` returns null, the UI gives no platform-specific guidance even though archive-preserve and emulator mode are important behavioral assumptions
- **UI implications:** should be a minimal form with install directory, emulator selection, and concise archive guidance; no generic clutter

### Nintendo 64

- **Plugin / platform:** `n64`
- **Primary workflow:** ROM-base installer
- **Associated emulator/tooling:** hardcoded profile targets RetroArch with `mupen64plus-next`
- **Required settings:** none exposed by plugin descriptor
- **Optional settings used today:** global ROM install root and associated emulator/core metadata can still be stored in mappings, but the installer profile itself already defines a RetroArch core expectation
- **Supported formats / behavior:** `.z64`, `.n64`, `.v64`, `.zip`; archive policy is preserve
- **Validation / consumption notes:** inherits generic ROM-base behavior; no plugin-specific validation surface
- **Hidden assumptions:** plugin behavior implies a RetroArch/core concept, but the plugin does not declare config fields for it
- **UI implications:** should remain very small; only show shared ROM/emulator fields if they are actually used by downstream mapping logic for launch behavior

### Super Nintendo Entertainment System

- **Plugin / platform:** `snes`
- **Primary workflow:** ROM-base installer
- **Associated emulator/tooling:** hardcoded profile targets RetroArch with `snes9x`
- **Required settings:** none exposed by plugin descriptor
- **Optional settings used today:** same pattern as N64
- **Supported formats / behavior:** `.zip`, `.sfc`, `.smc`; archive policy is preserve
- **Validation / consumption notes:** no descriptor, no plugin-specific validation
- **Hidden assumptions:** RetroArch core expectation exists in installer profile but is not represented as declared UI metadata
- **UI implications:** should be minimal, not the full general-purpose form

### PlayStation 1

- **Plugin / platform:** `ps1`
- **Primary workflow:** inspection-driven install supporting multi-disc and companion-file formats
- **Associated emulator/tooling:** driven by ROM/emulator settings; tests indicate RetroArch core or DuckStation-style arguments can matter
- **Required settings:** none exposed by plugin descriptor
- **Optional settings used today:** shared emulator/core fields may matter more than plugin-specific fields here
- **Supported formats / behavior:** `.chd`, `.iso`, `.pbp`, `.cue`, `.ccd`, `.m3u`
- **Validation / consumption notes:** no plugin descriptor despite meaningful launch-format behavior
- **Hidden assumptions:** multi-disc launch artifact selection and emulator argument behavior are important, but the UI does not express that distinction
- **UI implications:** a shared “disc-based ROM platform” section is more appropriate than plugin-specific hardcoding

### PlayStation 2

- **Plugin / platform:** `ps2`
- **Associated emulator/tooling:** PCSX2
- **Required settings:** none
- **Optional settings:** `Pcsx2ExecutablePath`
- **Supported formats / behavior:** `.iso`, `.chd`, `.cso`, `.zso`, `.bin`; inspector-led artifact selection
- **Validation / consumption notes:** the installer declares `Pcsx2ExecutablePath`, but the current shared install settings model does not contain that property and the current config view model does not expose it
- **Hidden assumptions / mismatch:** plugin metadata says there is a PCSX2 override, but that override is not currently represented in the main mapping model or mapper path the same way PS3/PS4/PSP/Vita are
- **UI implications:** PS2 should show an emulator executable override only if the plumbing is completed; current UI appears incomplete relative to plugin metadata

### PlayStation 3

- **Plugin / platform:** `ps3`
- **Associated emulator/tooling:** RPCS3
- **Required settings:** none strictly required, but RPCS3 access becomes important for PKG/update/DLC/RAP workflows
- **Optional settings:**
  - `Rpcs3ExecutablePath`
  - `Rpcs3LicenseDirectory`
  - `SkipRegionMismatchedDlc`
  - `SkipUnmatchedRapFiles`
  - `PreferMetadataBasedPackageMatching`
- **Supported formats / behavior:** JB folder, decrypted ISO, optional `.pkg` updates/DLC, optional `.rap` licenses
- **Validation / consumption notes:** installer consumes these settings directly during optional content installation and RPCS3 resolution
- **Install-time vs runtime:** these are mostly install-time settings; launch still resolves to installed artifact or RPCS3-driven content
- **Hidden assumptions:** package install behavior depends heavily on RPCS3 readiness and package/license matching rules
- **UI implications:** PS3 needs a dedicated “RPCS3 / Optional Content” section; current screen partly supports this and is the clearest example of plugin-aware rendering already in place

### PlayStation 4

- **Plugin / platform:** `ps4`
- **Associated emulator/tooling:** ShadPS4 plus optional external PKG extractor
- **Required settings:** none always required
- **Optional settings:**
  - `ShadPs4ExecutablePath`
  - `Ps4GamesDirectory`
  - `Ps4ExternalPkgExtractorPath`
  - `Ps4FailIfDirectPkgExtractorMissing`
- **Supported formats / behavior:** direct files and archives; DLC and updates supported; direct `.pkg` workflow can depend on external extraction tooling
- **Validation / consumption notes:** install logic explicitly checks for direct PKG source, configured extractor path, and the fail-fast toggle
- **Install-time vs runtime:** PKG extractor is install-time only; ShadPS4 executable is runtime/readiness oriented; games directory affects install layout
- **Hidden assumptions:** direct PKG support is conditional, not universal; the external extractor is only relevant when the selected content source is a direct PKG workflow
- **UI implications:** PS4 must surface the PKG extractor and fail-fast toggle, but those belong in a conditional tooling subsection rather than a generic global group

### PlayStation Portable

- **Plugin / platform:** `psp`
- **Associated emulator/tooling:** standalone PPSSPP or RetroArch PPSSPP core
- **Required settings:** none always required
- **Optional settings:**
  - `PspEmulatorMode`
  - `PpssppExecutablePath`
  - `RetroArchExecutablePath`
  - `RetroArchPpssppCorePath`
  - `ValidateRetroArchPpssppAssets`
  - `FailInstallIfEmulatorNotReady`
- **Supported formats / behavior:** `.iso`, `.cso`, `.chd`; inspection-led install; launch args differ by emulator mode
- **Validation / consumption notes:** installer resolves mode, validates executable/core/assets readiness, and can fail install if configured to do so
- **Install-time vs runtime:** emulator mode and readiness are both launch- and install-readiness concerns; RetroArch core asset validation is effectively preflight validation
- **Hidden assumptions / mismatch:** the plugin descriptor is rich, but the main UI does not render these fields and the mapper currently infers PSP mode from generic mapping fields instead of honoring a dedicated editable config surface
- **UI implications:** PSP is the strongest case for emulator-dependent conditional fields inside a plugin section

### PlayStation Vita

- **Plugin / platform:** `psvita`
- **Associated emulator/tooling:** Vita3K
- **Required settings:** none always required
- **Optional settings:**
  - `Vita3kExecutablePath`
  - `VitaInstallUpdatesAutomatically`
  - `VitaInstallDlcAutomatically`
  - `VitaFailIfEmulatorNotReady`
- **Supported formats / behavior:** Vita package/install-token workflow with DLC/update options
- **Validation / consumption notes:** installer consumes Vita settings directly; update/DLC auto-install and emulator readiness toggles are real behavior
- **Install-time vs runtime:** update/DLC toggles are install-time; Vita3K path and readiness affect preflight/runtime behavior
- **Hidden assumptions / mismatch:** the mapper currently hardcodes empty/default Vita values instead of mapping dedicated platform fields from the platform mapping model, and the main UI does not expose the Vita descriptor
- **UI implications:** Vita needs a dedicated emulator/tooling section, but this is missing today

### Nintendo Switch

- **Plugin / platform:** `switch`
- **Associated emulator/tooling:** Eden
- **Required settings:** none
- **Optional settings:** `SwitchEdenExecutablePath`
- **Supported formats / behavior:** `.nsp`, `.xci`; inspection-led install; DLC and updates supported
- **Validation / consumption notes:** plugin descriptor declares an executable override, but the shared mapping/mapping UI does not currently expose or map it
- **Hidden assumptions / mismatch:** plugin has a real executable override concept with no current UI path
- **UI implications:** should show a simple “Eden executable” tool-path field when Switch is selected

### Nintendo 3DS

- **Plugin / platform:** `3ds`
- **Associated emulator/tooling:** Azahar or AzaharPlus
- **Required settings:** none
- **Optional settings:**
  - `AzaharExecutablePath`
  - `AzaharPlusExecutablePath`
- **Supported formats / behavior:** `.3ds`, `.cci`, `.cxi`; ROM-base installer with inspection
- **Validation / consumption notes:** descriptor exists, but current main mapping/mappers do not expose or persist these fields through a dedicated UI path
- **Hidden assumptions / mismatch:** dual-emulator override support exists only in metadata, not in the visible platform configuration experience
- **UI implications:** should show an emulator choice/override section rather than a generic core form

### Nintendo Wii

- **Plugin / platform:** `wii`
- **Associated emulator/tooling:** Dolphin
- **Required settings:** none
- **Optional settings:** `DolphinExecutablePath`
- **Supported formats / behavior:** staging inspection; DLC/updates supported
- **Validation / consumption notes:** descriptor exists, but current UI does not surface it
- **UI implications:** should show a simple Dolphin executable override field plus shared install directory options

### Nintendo GameCube

- **Plugin / platform:** `gamecube`
- **Associated emulator/tooling:** Dolphin
- **Required settings:** none
- **Optional settings:** `DolphinExecutablePath`
- **Supported formats / behavior:** staging inspection; no DLC/updates
- **Validation / consumption notes:** same metadata pattern as Wii
- **UI implications:** should reuse the same Dolphin tool-path section as Wii

### Nintendo Wii U

- **Plugin / platform:** `wiiu`
- **Associated emulator/tooling:** Cemu
- **Required settings:** none
- **Optional settings:** `CemuExecutablePath`
- **Supported formats / behavior:** staging inspection; DLC/updates supported
- **Validation / consumption notes:** descriptor exists; current UI does not surface it
- **UI implications:** should show a simple Cemu executable override field

### Microsoft Xbox

- **Plugin / platform:** `xbox`
- **Associated emulator/tooling:** no plugin-specific config descriptor
- **Required settings:** none
- **Optional settings used today:** mostly shared install/emulator settings
- **Supported formats / behavior:** dedicated installer exists; staging inspection and DLC/update support are present
- **Validation / consumption notes:** no plugin-specific config surface
- **UI implications:** should stay minimal unless future plugin metadata adds tool fields

### Microsoft Xbox 360

- **Plugin / platform:** `xbox360`
- **Associated emulator/tooling:** no plugin-specific config descriptor
- **Required settings:** none
- **Optional settings used today:** shared install/emulator settings only
- **Supported formats / behavior:** dedicated installer; staging inspection and DLC/update support are present
- **Validation / consumption notes:** no descriptor
- **UI implications:** should stay minimal; current form is likely noisier than necessary

### General Fallback Plugin

- **Plugin / platform:** `general`
- **Associated emulator/tooling:** generic ROM/emulator fallback
- **Required settings:** `SupportedFileTypes`
- **Optional settings:**
  - `ExtractArchives`
  - `ArchiveHandlingMode`
  - `PreferredLaunchExtensions`
  - `InstallFromArchiveDirectly`
  - `InstallLayoutMode`
  - `ArtifactSelectionMode`
  - `UseGameSubdirectory`
  - `InstallAllMatchingFiles`
  - `UseGeneralFallbackInstaller`
- **Supported formats / behavior:** generic file-oriented ROM install with configurable archive/layout/selection behavior
- **Validation / consumption notes:** this is the only plugin implementing `IPlatformConfigurationProvider`; it also exposes default values and validation, and its options are consumed deeply by the install pipeline
- **Install-time vs runtime:** mostly install-time selection/layout/archive rules
- **Hidden assumptions:** current UI shows most of these fields when the descriptor is present, but still mixes them into unrelated global sections
- **UI implications:** this plugin should own a dedicated “Generic ROM Rules” section instead of borrowing the same layout as specialized platforms

### Cross-plugin duplication and shared concepts

Across the inventory, the repeated configuration concepts are:

- **Install location override** — games directory, ROM root, platform-specific install root overrides
- **Emulator selection** — associated LaunchBox emulator, or plugin-specific executable override
- **Emulator core selection** — especially relevant to RetroArch-driven flows
- **Archive handling** — preserve vs extract vs inspect-first behavior
- **External tooling paths** — RPCS3, ShadPS4, external PKG extractor, Dolphin, Cemu, Vita3K, PPSSPP, RetroArch, etc.
- **Readiness policy** — fail install if emulator/tooling is not ready
- **Optional content policy** — auto-install DLC, updates, RAPs, bonus content, OST, pre-reqs

### Plugin-specific options currently missing from the main configuration UI

Based on installer descriptors and the current configuration dialog implementation, the following declared plugin fields are missing from the main platform configuration screen:

- PS2: `Pcsx2ExecutablePath`
- PSP: all six PSP fields
- Vita: all four Vita fields
- Switch: `SwitchEdenExecutablePath`
- 3DS: `AzaharExecutablePath`, `AzaharPlusExecutablePath`
- Wii / GameCube: `DolphinExecutablePath`
- Wii U: `CemuExecutablePath`

### UI settings that exist but are weakly aligned to real plugin behavior

- Generic emulator core fields are shown broadly even when the active plugin does not declare or consume them directly.
- `PluginSettings` exists in the mapping model as a serialized payload field, but the current dialog does not appear to use it as the primary storage model for dynamic plugin-defined settings.
- PSP and Vita dedicated settings exist in `PlatformInstallSettings`, but the current mapper mostly infers or hardcodes them instead of sourcing them from a dedicated user-edited section.

## 3. Current Platform Configuration Screen Analysis

### How the current UI works

The current configuration flow is:

1. The platform list resolves a plugin key from the mapping or RomM platform id.
2. The installer registry returns the plugin descriptor for that key.
3. The dialog view model stores that descriptor.
4. The XAML renders one large static form and selectively shows only a handful of plugin fields with `HasConfigField(...)` checks.

This is a good starting point because the system already resolves plugin metadata before rendering the screen.

### What works well

- The UI already matches the existing app style and should be preserved visually.
- Browse/select interactions are consistent for known path fields.
- The view model already supports inline editing and saving back into `PlatformMapping`.
- The descriptor-based visibility checks prove that plugin-aware rendering is feasible in the current architecture.
- The current split between basic and enhanced installation workflows is useful and should survive, especially for Windows.

### What is confusing or cluttered

- The form begins with a generic global stack that is too broad for many platforms.
- Emulator core fields are always shown even though many platforms do not need user-managed core selection.
- The same large surface is used for Windows installers, simple ROM platforms, and specialized console plugins.
- Plugin-specific fields are mixed into the left column without clear platform/tool grouping.
- The right column is driven only by Basic vs Enhanced install type, not by plugin capability or emulator mode.

### Where irrelevant settings are shown

- Minimal ROM platforms see generic emulator core name/id/path and launch-args fields even when the plugin itself hardcodes or infers emulator behavior.
- Platforms without plugin-specific tool paths still inherit the same broad global configuration area.
- Enhanced-install content options are structurally part of the same screen even when they are only meaningful for Windows installer workflows.

### Where required or useful settings are missing

- PS4 is supported reasonably well, but its PKG extractor is still presented as a generic field rather than a contextual tooling requirement.
- PS3 fields are present, but the UI does not clearly explain that they are mainly relevant for package/DLC/license workflows.
- PSP, Vita, Switch, PS2, 3DS, Wii, GameCube, and Wii U descriptor-declared fields are missing from the current screen.

### Architectural limitations in the current implementation

1. **Static form with selective visibility:**
   The dialog is not truly descriptor-driven. It is a hardcoded form with many hand-authored properties such as `ShowPs4GamesDirectoryField`.

2. **Per-key boolean explosion:**
   Every supported dynamic field needs a dedicated view-model property and XAML block. This does not scale as plugins are added.

3. **No field grouping model:**
   `PlatformConfigDescriptor` currently exposes only a flat list of fields. There is no section, capability, visibility condition, or dependency metadata.

4. **Weak separation of global vs plugin-specific vs emulator-specific concerns:**
   The screen stores everything in one `PlatformMapping` object and renders it together, even though many settings conceptually belong to different layers.

5. **Mapping model drift:**
   Several installers expose fields that are not fully represented in the main mapping / mapper / UI pipeline, which means plugin metadata alone is not sufficient to guarantee usable configuration.

## 4. Proposed UI Architecture

### Design goal

Move from a static form with scattered plugin visibility checks to a **layered configuration composition model**:

- **Shared platform shell** for truly common settings
- **Plugin-defined sections** for plugin-specific install/tool behavior
- **Emulator overlays** for emulator-dependent settings
- **Capability-driven visibility** for optional groups like DLC/update handling or archive behavior

### Recommended architecture

#### A. Introduce a richer UI schema, not just a flat field list

Evolve the current plugin metadata concept from:

- flat fields with key/label/type/advanced/default

to something conceptually like:

- sections
- field groups
- field visibility rules
- capability tags
- storage binding metadata
- optional browse behavior / picker type
- optional enum options

This does **not** need to become a giant generic form engine immediately. The first step can be an internal schema object consumed only by the platform configuration dialog.

#### B. Define three rendering layers

##### 1. Shared platform shell

Always available when relevant:

- Games Directory
- Installation Type (Basic / Enhanced) where supported
- Shared emulator selection only when the platform/plugin uses LaunchBox emulator association
- Shared ROM root / archive behavior only for ROM-style installers or fallback plugin scenarios

This keeps the top of the screen familiar and visually consistent.

##### 2. Plugin section(s)

Owned by the selected plugin. Examples:

- **PS4 Tools**
  - ShadPS4 executable
  - PS4 games directory
  - PKG extractor
  - fail-if-missing toggle

- **PS3 RPCS3 Options**
  - RPCS3 executable
  - license directory
  - DLC/update/rap matching toggles

- **General ROM Rules**
  - supported file types
  - archive handling
  - layout
  - artifact selection

##### 3. Emulator overlay section

Rendered only when the plugin or selected emulator mode requires it.

Examples:

- **RetroArch overlay**
  - core selector or core path
  - optional validation toggle
  - launch arguments if actually needed

- **Standalone PPSSPP overlay**
  - PPSSPP executable path

- **Dolphin overlay**
  - Dolphin executable path

This solves the current problem where emulator-core fields are shown too broadly.

### How the UI should determine which fields to show

The effective visible form should be composed from:

1. **Installer identity / plugin key**
2. **Installer capabilities**
3. **Plugin-declared sections/fields**
4. **Current mapping values** (for mode-dependent visibility, such as PSP emulator mode)
5. **Install scenario** (Basic vs Enhanced)

In practice:

- shared shell fields are included if capability says they matter
- plugin fields are included from the active plugin schema
- emulator overlay fields are included if selected plugin mode or selected emulator implies them

### How shared settings should be reused

Create reusable field definitions for repeated concepts:

- executable override path
- install directory override path
- boolean readiness/fail-fast toggle
- DLC/update auto-install toggle pair
- RetroArch core selector/path
- archive handling mode selector
- artifact selection mode selector

Plugins should reference these shared definitions instead of redefining similar browse/path/boolean widgets independently.

### How plugin-specific settings should be declared

Recommended rule:

- **Common concepts** use shared field definitions with plugin-provided labels/help text.
- **Truly unique behavior** uses plugin-owned field descriptors.

Examples:

- PS4 PKG extractor is plugin-specific.
- Dolphin executable is a shared “emulator executable override” pattern specialized for Wii/GameCube.
- RPCS3 license directory is a shared path field pattern but still plugin-owned in meaning.

### How emulator-specific settings should be layered in

Use a two-step decision:

1. Determine whether the selected plugin supports multiple emulator modes or relies on a LaunchBox-associated emulator.
2. Resolve an emulator capability profile:
   - standalone emulator override
   - RetroArch core-based flow
   - emulator asset validation supported
   - readiness failure policy supported

For PSP specifically:

- show `PspEmulatorMode`
- if standalone selected: show PPSSPP executable
- if RetroArch selected: show RetroArch executable, PPSSPP core, validation toggle
- in either mode: optionally show fail-if-not-ready

This is much cleaner than always displaying generic emulator core fields to everyone.

### How the design scales

This design scales because adding a new plugin becomes:

1. declare installer metadata
2. optionally declare a config schema section using shared field types
3. wire storage mapping for those declared fields
4. renderer automatically picks them up

That avoids:

- more hardcoded `ShowXyzField` booleans
- more hardcoded XAML blocks for every new plugin
- more global fields that appear for unrelated platforms

## 5. Proposed Field Visibility Matrix

The following matrix is the recommended high-level visibility model.

| Field category | Show for | Hide for | Notes |
|---|---|---|---|
| General install settings | All platforms | None | Includes Games Directory and only the truly universal shell settings |
| Installation Type / Enhanced options | Windows-style installer workflows | Simple ROM platforms where enhanced flow is irrelevant | Preserve current behavior but gate it by capability/platform type |
| Associated emulator selection | Platforms whose launch behavior uses LaunchBox emulator association | Platforms fully driven by plugin-specific executable/tool overrides only | Many ROM platforms still benefit from this, but not every platform needs core subfields |
| RetroArch core selection | Platforms/modes that actually run through RetroArch | Standalone emulator flows | PSP RetroArch mode is the clearest explicit case; N64/SNES may optionally expose this only if user override is supported |
| Extractor/tool path selection | Platforms with external tool dependencies | Platforms without tool dependencies | Reuse the same browse pattern for RPCS3, ShadPS4, Cemu, Dolphin, Vita3K, etc. |
| PS4 PKG extractor | PS4 only, ideally only in direct-PKG capable workflows | All other platforms | Explicitly tied to direct PKG handling |
| Archive handling | General fallback plugin and any plugin that genuinely exposes archive policies | Dedicated plugins whose archive behavior is fixed | Prevent clutter on platforms that do not let users tune this |
| Platform-specific advanced options | Only when declared by the active plugin | All other platforms | Advanced toggles should remain collapsed/grouped visually |
| DLC/update auto-install | Plugins that actually support install-time DLC/update actions, such as PS3/Vita | Platforms with no optional-content automation | Should be grouped with the plugin/tooling section |
| Emulator readiness / fail-fast | Plugins that validate emulator assets or executable presence | Platforms with no readiness preflight | PSP and Vita are strong examples |

## 6. Concrete Platform Examples

### PS4 example

**Should show:**

- Games Directory
- Associated emulator or ShadPS4 executable override section
- PS4 Games Directory
- ShadPS4 Executable
- External PKG Extractor
- Fail if direct PKG encountered but extractor missing

**Should hide:**

- RetroArch core fields
- Generic general-plugin archive tuning fields
- Windows enhanced install content options
- PS3 RPCS3 license/DLC fields

**Why this matches behavior:**

PS4 install logic explicitly checks `Ps4ExternalPkgExtractorPath` and `Ps4FailIfDirectPkgExtractorMissing` when a direct PKG source is encountered. Those settings are real and install-time relevant. Generic core fields are not.

### PSP example (RetroArch-based mode)

**Should show:**

- Games Directory
- PSP Emulator Mode
- RetroArch Executable
- RetroArch PPSSPP Core
- Validate RetroArch PPSSPP Assets
- Fail If Emulator Not Ready

**Should hide:**

- PPSSPP executable path
- PS4 extractor settings
- PS3 RPCS3 settings
- general fallback archive tuning unless the plugin explicitly delegates to it

**Why this matches behavior:**

The PSP installer chooses launch arguments and readiness checks based on `PspEmulatorMode`. In RetroArch mode it validates the RetroArch executable/core/assets path and builds `-L <core> <rom>` arguments. The standalone PPSSPP field is irrelevant in this mode.

### PSP example (Standalone PPSSPP mode)

**Should show:**

- Games Directory
- PSP Emulator Mode
- PPSSPP Executable
- Fail If Emulator Not Ready

**Should hide:**

- RetroArch executable
- RetroArch PPSSPP core
- Validate RetroArch assets

**Why this matches behavior:**

Standalone PPSSPP mode validates PPSSPP readiness, not RetroArch assets. This is the clearest current example of emulator-choice-driven field visibility.

### N64 example (minimal platform)

**Should show:**

- Games Directory
- optional associated emulator selection only if the mapping layer still uses it
- minimal ROM-location fields if retained as shared shell concepts

**Should hide:**

- tool-path overrides for unrelated platforms
- RetroArch core selection if the installer does not truly support overriding it
- enhanced installer workflow blocks
- archive tuning fields not used by the dedicated N64 installer

**Why this matches behavior:**

N64 is a simple ROM-base install with a fixed RetroArch core profile. The current screen shows far more than the plugin really needs.

### General fallback plugin example (special tooling not required, but archive policy required)

**Should show:**

- Games Directory
- Associated emulator
- Supported File Types
- Archive Handling
- Preferred Launch Extensions
- Install Layout
- Artifact Selection
- Install All Matching Files
- Install From Archive Directly
- Enable General Fallback Installer

**Should hide:**

- PS3/PS4/Vita/PSP-specific tool fields
- Windows enhanced-only options when not in a Windows install workflow

**Why this matches behavior:**

The general plugin is the one place where the archive/layout/artifact fields are real first-class configuration inputs, are validated, and are deeply consumed by install logic.

### Vita example (special tooling + optional content)

**Should show:**

- Games Directory
- Vita3K Executable
- Install Updates Automatically
- Install DLC Automatically
- Fail If Emulator Not Ready

**Should hide:**

- RetroArch core fields
- PS4 PKG extractor
- generic fallback archive tuning unless using the general plugin

**Why this matches behavior:**

Vita has genuine emulator-readiness and optional-content automation behavior. Those belong in a dedicated Vita3K section, not mixed with unrelated core/path fields.

## 7. Recommended Implementation Plan

### Step 1: Stabilize the metadata-to-storage pipeline

Before redesigning rendering, align plugin descriptors, mapping storage, and install settings.

High-priority files/modules likely involved:

- `src/RomM.Platforms.Abstractions/Models/Metadata/*`
- `src/RomM.Platforms.Abstractions/Models/Install/PlatformInstallSettings.cs`
- `src/RomM.LaunchBoxPlugin/Models/PlatformMapping/PlatformMapping.cs`
- `src/RomM.LaunchBoxPlugin/Services/PlatformInstallers/PlatformInstallSettingsMapper.cs`
- `src/RomM.LaunchBoxPlugin/Services/PlatformMappingStore.cs`
- `src/RomM.LaunchBoxPlugin/Services/InstallStateService.cs`

Goal:

- every declared plugin field should have a clear persistence and consumption path
- remove metadata/UI drift before renderer refactoring

### Step 2: Introduce an internal UI schema layer

Add a UI-oriented composition model that can merge:

- shared shell sections
- plugin descriptor sections
- emulator overlay sections

This can initially be adapter code on top of existing `PlatformConfigDescriptor` rather than a breaking abstraction change.

### Step 3: Replace hardcoded `ShowXxxField` booleans with descriptor-driven collections

Instead of adding one property per field, move to:

- `ObservableCollection<ConfigSectionViewModel>`
- each section contains field view models
- XAML renders section templates by field type

This is the key scalability improvement.

### Step 4: Preserve existing visuals while templating repeated controls

Keep:

- current dark theme
- section headers
- browse buttons
- spacing and helper text patterns

Refactor repeated browse/textbox/checkbox rows into reusable data templates or user controls.

### Step 5: Add capability-aware section selection

Use installer capabilities and plugin metadata to decide whether to show:

- enhanced install section
- archive behavior section
- optional content section
- tooling section
- emulator overlay section

### Step 6: Migrate plugins incrementally

Recommended incremental order:

1. General fallback plugin
2. PS3 and PS4
3. PSP and Vita
4. Wii / GameCube / Wii U / Switch / 3DS / PS2
5. cleanup of minimal ROM platforms

This order delivers visible value early and fixes the biggest metadata/UI mismatches first.

## 8. High-Priority Improvements

### 1. Make plugin descriptor fields actually drive rendering

- **Impact:** Very high
- **Implementation complexity:** Medium
- **Reason to prioritize:** This is the foundation that removes the current hardcoded field-by-field growth pattern.

### 2. Fix metadata / mapper / UI drift for PSP, Vita, Switch, 3DS, PS2, Wii-family

- **Impact:** Very high
- **Implementation complexity:** Medium to high
- **Reason to prioritize:** These plugins already declare real settings that the current screen does not reliably expose or map.

### 3. Split shared shell, plugin sections, and emulator overlays

- **Impact:** High
- **Implementation complexity:** Medium
- **Reason to prioritize:** This directly reduces UI clutter and makes field visibility understandable.

### 4. Restrict emulator core fields to platforms/modes that truly use them

- **Impact:** High
- **Implementation complexity:** Low to medium
- **Reason to prioritize:** It removes some of the most visible irrelevant UI without changing the visual design.

### 5. Add explicit tooling sections for PS4, PS3, PSP, Vita, and emulator-override platforms

- **Impact:** High
- **Implementation complexity:** Medium
- **Reason to prioritize:** These are the platforms where install success most directly depends on correct tool configuration.

### 6. Keep minimal platforms minimal

- **Impact:** Medium to high
- **Implementation complexity:** Low
- **Reason to prioritize:** N64, SNES, Arcade, Xbox, and Xbox 360 should not pay the usability cost of features they do not use.

### 7. Preserve the current visual language while templating repeated controls

- **Impact:** Medium
- **Implementation complexity:** Medium
- **Reason to prioritize:** This allows architectural improvement without creating a parallel design system.

## Recommended End State

The ideal end state for this screen is:

- a familiar RomMbox configuration surface
- a small shared shell for universal settings
- one plugin-aware content area driven by real installer metadata
- optional emulator overlays driven by actual emulator mode/capability
- no irrelevant fields shown “just in case”
- no need to add another hand-coded boolean and XAML block each time a new plugin is introduced

That end state is strongly aligned with the current codebase. The repository already contains plugin descriptors, capability metadata, and a plugin-resolution step before opening the configuration screen. The redesign should build on those assets rather than replacing the current UI wholesale.
