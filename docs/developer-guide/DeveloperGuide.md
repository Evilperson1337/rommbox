# RomM LaunchBox Plugin Developer Guide

This guide documents the current implementation of the RomM LaunchBox plugin: a WPF-based, MVVM-style .NET 9 plugin that integrates with LaunchBox and RomM.

## Table of Contents

- [Architecture Overview](ArchitectureOverview.md)
- [Plugin Lifecycle](PluginLifecycle.md)
- [Project Structure](ProjectStructure.md)
- [MVVM Implementation](MVVMImplementation.md)
- [Dependency Graph](DependencyGraph.md)
- [Data Flow](DataFlow.md)
- [Concurrency Model](ConcurrencyModel.md)
- [Error Handling](ErrorHandling.md)
- [Extension Guide](ExtensionGuide.md)
- [Performance Considerations](PerformanceConsiderations.md)
- [Security Model](SecurityModel.md)
- [LaunchBox Integration](LaunchBoxIntegration.md)
- [Glossary](Glossary.md)
- [Troubleshooting](Troubleshooting.md)

## Quick Orientation

If this is your first time in the codebase, start with:

1. [Architecture Overview](ArchitectureOverview.md)
2. [Project Structure](ProjectStructure.md)
3. [Plugin Lifecycle](PluginLifecycle.md)

Then follow the workflow-focused docs:

- [Data Flow](DataFlow.md)
- [Concurrency Model](ConcurrencyModel.md)
- [Error Handling](ErrorHandling.md)

## Key Entry Points

- Plugin bootstrap and shared services: `RomMbox.Plugin.PluginEntry`
- LaunchBox Tools menu integration: `RomMbox.Plugin.Adapters.ToolsMenu.RomMToolsMenuItem`
- LaunchBox game context menu integration: `RomMbox.Plugin.Adapters.GameMenu.RommMultiMenuItem`
- WPF shell window + navigation: `RomMbox.UI.MainWindow` / `RomMbox.UI.ViewModels.MainWindowViewModel`

For a deep dive, continue with the detailed documents linked above.
