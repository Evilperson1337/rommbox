# RomM Unified WPF MVVM Mockup

A runnable WPF (.NET 8) project that demonstrates the unified "RomM Server Configuration" window using MVVM:
- Left navigation (Connection / Platforms / Import / Test)
- Locking of pages until connected
- Single window with in-place content switching
- Shared theme/styling in App.xaml
- Dummy commands (clickable; behavior is mocked)

## Build & Run (Windows)
```bash
dotnet build
dotnet run
```

Tip: Click **Test** on the Connection page to mark it Connected, which unlocks the other pages.
