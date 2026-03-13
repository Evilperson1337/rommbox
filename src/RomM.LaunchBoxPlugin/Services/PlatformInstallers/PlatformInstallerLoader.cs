using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RomM.Platforms.Abstractions;
using RomMbox.Services.Logging;
using RomMbox.Services.Paths;

namespace RomMbox.Services.PlatformInstallers
{
    internal sealed class PlatformInstallerLoader
    {
        private readonly LoggingService _logger;

        public PlatformInstallerLoader(LoggingService logger)
        {
            _logger = logger;
        }

        public PlatformInstallerRegistry Load()
        {
            var installers = new Dictionary<string, IPlatformInstaller>(StringComparer.OrdinalIgnoreCase);
            var platformsRoot = ResolvePlatformRoot();
            if (string.IsNullOrWhiteSpace(platformsRoot) || !Directory.Exists(platformsRoot))
            {
                _logger?.Warning($"Platform installer folder missing at '{platformsRoot}'.");
                return new PlatformInstallerRegistry(installers);
            }

            var dlls = Directory.EnumerateFiles(platformsRoot, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (dlls.Count == 0)
            {
                _logger?.Warning($"Platform installer folder empty at '{platformsRoot}'.");
                return new PlatformInstallerRegistry(installers);
            }

            foreach (var dllPath in dlls)
            {
                TryLoadInstallerAssembly(dllPath, installers);
            }

            return new PlatformInstallerRegistry(installers);
        }

        private string ResolvePlatformRoot()
        {
            var pluginRoot = PluginPaths.GetPluginRootDirectory();
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                return string.Empty;
            }

            return Path.Combine(pluginRoot, "system", "platforms");
        }

        private void TryLoadInstallerAssembly(string dllPath, Dictionary<string, IPlatformInstaller> installers)
        {
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            {
                return;
            }

            try
            {
                _logger?.Info($"Loading platform installer assembly '{dllPath}'.");
                var assembly = Assembly.LoadFrom(dllPath);
                foreach (var type in SafeGetTypes(assembly))
                {
                    if (type == null || type.IsAbstract || !typeof(IPlatformInstaller).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    try
                    {
                        var installer = Activator.CreateInstance(type) as IPlatformInstaller;
                        if (installer == null)
                        {
                            _logger?.Warning($"Failed to construct platform installer '{type.FullName}' in '{dllPath}'.");
                            continue;
                        }

                        var key = installer.PlatformKey ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            _logger?.Warning($"Platform installer '{type.FullName}' missing PlatformKey; skipping.");
                            continue;
                        }

                        if (installers.ContainsKey(key))
                        {
                            _logger?.Warning($"Duplicate platform installer for '{key}' from '{dllPath}'. Keeping existing.");
                            continue;
                        }

                        installers[key] = installer;
                        _logger?.Info($"Registered platform installer '{installer.DisplayName ?? type.Name}' for '{key}'.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"Failed to load platform installer type '{type.FullName}' from '{dllPath}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Failed to load platform installer assembly '{dllPath}': {ex.Message}");
            }
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                return Array.Empty<Type>();
            }

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types?.Where(type => type != null) ?? Array.Empty<Type>();
            }
        }
    }
}
