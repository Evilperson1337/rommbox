# PS4 Platform Configuration Window Redesign

## Goal

Redesign the PS4 platform configuration window so it feels purpose-built for PlayStation 4 instead of inheriting a generic ROM/emulator form.

This mockup is intended for image generation, UI review, or future implementation planning.

## Design Intent

The PS4 configuration screen should communicate four things immediately:

1. This is a **PS4-specific** setup screen.
2. The primary emulator is **ShadPS4**.
3. **Direct PKG workflows** may require an external extractor.
4. Users should only see settings that actually matter for PS4.

The screen should preserve the application’s current visual language:

- dark navy background
- blue primary action button
- boxed card layout
- compact form spacing
- simple section headers
- browse buttons aligned to path inputs

## High-Level Layout

The window is a centered dark panel inside the existing application shell.

### Left Sidebar

Keep the existing app sidebar unchanged:

- RomM logo and header
- Connection
- Platforms
- Games
- Test

The **Platforms** item remains selected with the current blue-highlighted state.

### Main Content Container

The main content is a single polished card, visually cleaner than the current two-column generic form.

Structure:

1. Header bar
2. PS4 status summary strip
3. Two main configuration sections
4. Optional advanced section
5. Sticky action row

## Visual Composition

### 1. Header Bar

Top-left:

- **Title:** `PlayStation 4 Configuration`
- **Subtitle:** `Configure ShadPS4 launch settings and direct PKG install tooling.`

Top-right:

- secondary button: `Back`
- primary button: `Save`

The header has more breathing room than the current version and should visually anchor the screen.

### 2. PS4 Status Summary Strip

Directly under the header is a slim horizontal info panel with three compact status pills:

- **Platform:** `PlayStation 4`
- **Plugin:** `ps4`
- **Emulator:** `ShadPS4`

If ShadPS4 executable path is configured, show a green-ready indicator:

- `ShadPS4 Path Configured`

If extractor path is empty, show a neutral warning badge:

- `Direct PKG extractor not configured`

This strip helps orient the user before they even read the form.

## Primary Window Sections

The redesign should not use the current broad “Global Options” and “Basic Options” structure.

Instead, PS4 gets these sections:

---

### Section A: Install Location

Card title:

- `Install Location`

Description:

- `Choose where PS4 content should be stored on disk.`

Fields:

1. **Games Directory**
   - full-width path input
   - `Browse` button on the right
   - helper text:
     - `Default PS4 install location used by this LaunchBox platform.`

2. **PS4 Games Directory Override**
   - full-width path input
   - `Browse` button
   - helper text:
     - `Optional serial-based install root used specifically for PS4 content.`

Why this section exists:

- these are install-target decisions
- they belong together
- they are distinct from emulator/tooling settings

---

### Section B: Emulator Setup

Card title:

- `ShadPS4 Setup`

Description:

- `Configure how LaunchBox starts installed PS4 titles.`

Fields:

1. **Associated Emulator**
   - dropdown
   - preselected to `ShadPS4` if available
   - helper text:
     - `Uses the LaunchBox emulator association for this platform.`

2. **ShadPS4 Executable**
   - full-width path input
   - `Browse` button
   - helper text:
     - `Optional executable override. If empty, the LaunchBox emulator entry is used.`

Important note:

- No generic emulator core fields.
- No emulator core id.
- No emulator core path.
- No emulator launch arguments field in the primary layout.

Those are irrelevant noise for PS4 and should be absent.

---

### Section C: Direct PKG Support

Card title:

- `Direct PKG Support`

Description:

- `Direct PKG downloads may require an external extractor before content can be used by ShadPS4.`

Fields:

1. **External PKG Extractor**
   - path input
   - `Browse` button
   - helper text:
     - `Used only when installing direct .pkg content.`

2. **Fail if direct PKG extractor is missing**
   - checkbox
   - helper text:
     - `When enabled, direct PKG installs stop immediately if extractor configuration is missing.`

3. **Informational callout box**
   - small bordered info panel under the fields
   - text:
     - `PKG extractor configuration is not required for all PS4 installs. It is only needed for workflows involving direct .pkg content.`

This section should be visually distinct with a slightly tinted dark-blue inset panel so it reads like tooling/preflight configuration.

---

### Section D: Advanced

Collapsed by default.

Header:

- `Advanced`
- chevron icon on the right

When expanded, it may contain:

- raw plugin key display (`ps4`) as read-only text
- future diagnostics/readiness text
- advanced helper note for troubleshooting

But it should **not** reintroduce irrelevant generic fields.

## Interaction Behavior

### Default PS4 Experience

When the user opens Configure for PS4, the window should feel minimal:

- installation type radio buttons are gone
- basic/enhanced distinction is gone from the visible PS4 screen
- only PS4-relevant install and tooling settings appear

### Save State

When the screen is clean and valid:

- `Save` button is enabled

When the games directory is missing:

- inline validation appears under the games directory field in red
- `Save` button is disabled

### Tooling Feedback

If ShadPS4 path exists on disk:

- show a small green `Detected` badge beside the label or helper text

If extractor path is blank:

- do **not** show an error by default
- only show a neutral warning/help state

If extractor path is set but file is missing:

- show a yellow warning line below the field:
  - `Configured extractor path was not found on disk.`

## Exact Mockup Description for Image Generation

Use this prompt for image generation:

---

**Prompt:**

Create a polished desktop application settings screen in a dark modern game-library style UI. The app uses a navy and charcoal color palette with blue accent buttons, similar to a premium emulator or launcher configuration tool.

The window is 16:9 and shows a left navigation sidebar and a main content area.

### Sidebar
- very dark navy background
- top text: “RomM” and small subtitle “Server Configuration”
- menu items stacked vertically: Connection, Platforms, Games, Test
- Platforms is selected with a soft blue highlight bar

### Main Content
- centered large dark card with rounded corners
- header title: “PlayStation 4 Configuration”
- subtitle: “Configure ShadPS4 launch settings and direct PKG install tooling.”
- top-right buttons: Back (dark secondary) and Save (bright blue primary)

Below the header is a slim status row with three capsule badges:
- Platform: PlayStation 4
- Plugin: ps4
- Emulator: ShadPS4

Also show a green badge that says “ShadPS4 Path Configured” and a muted warning badge that says “Direct PKG extractor not configured”

Then show three stacked settings sections, each inside subtle bordered dark panels:

#### Section 1: Install Location
- title: Install Location
- short description text
- field 1: Games Directory, wide path box, Browse button
- field 2: PS4 Games Directory Override, wide path box, Browse button

#### Section 2: ShadPS4 Setup
- title: ShadPS4 Setup
- short description text
- field 1: Associated Emulator dropdown set to ShadPS4
- field 2: ShadPS4 Executable path field with Browse button

#### Section 3: Direct PKG Support
- title: Direct PKG Support
- short description text
- field 1: External PKG Extractor path field with Browse button
- field 2: checkbox labeled “Fail if direct PKG extractor is missing”
- below it, an informational inset callout explaining the extractor is only needed for direct .pkg workflows

At the bottom, show a collapsed “Advanced” row with a chevron.

The layout should be clean, minimal, and purpose-built for PS4. Do not show generic emulator core fields, launch argument fields, ROM archive policy, or installation type radio buttons. The overall feel should be elegant, technical, and consistent with an existing desktop launcher app.

---

## What Should Explicitly Be Absent

To keep the PS4 screen clean, these should not appear:

- Emulator Core
- Emulator Core Id
- Emulator Core Path
- Emulator Launch Args
- ROM Install Root
- ROM Archive Policy
- generic archive extraction toggle
- basic vs enhanced installation type switch
- self-contained toggle
- target file(s)

## Why This Redesign Is Better

- It matches the actual PS4 plugin behavior.
- It reduces cognitive load.
- It makes the screen feel premium and intentional.
- It highlights the exact tooling PS4 users need.
- It avoids making PS4 users mentally parse settings meant for generic ROM platforms.

## Recommended Next Implementation Step

Implement a PS4-specific layout branch in [`PlatformInstallConfigDialog.xaml`](src/RomM.LaunchBoxPlugin/UI/Views/PlatformInstallConfigDialog.xaml) driven by [`PluginKey == "ps4"`](src/RomM.LaunchBoxPlugin/UI/ViewModels/PlatformInstallConfigViewModel.cs:431), rather than continuing to adapt the legacy generic form for this platform.
