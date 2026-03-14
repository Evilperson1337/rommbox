using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using RomMbox.Services.Paths;

namespace RomMbox.Services.PlatformInstallers
{
    /// <summary>
    /// Extends runtime probing so shared platform assemblies can live under system/platforms.
    /// </summary>
    internal static class PlatformAssemblyResolver
    {
        private static readonly object SyncRoot = new object();
        private static bool _initialized;

        internal static void EnsureInitialized()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                AssemblyLoadContext.Default.Resolving += OnResolving;
                _initialized = true;
            }
        }

        private static Assembly OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            return ResolvePlatformAssembly(assemblyName);
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (args == null || string.IsNullOrWhiteSpace(args.Name))
            {
                return null;
            }

            return ResolvePlatformAssembly(new AssemblyName(args.Name));
        }

        private static Assembly ResolvePlatformAssembly(AssemblyName assemblyName)
        {
            var simpleName = assemblyName != null ? (assemblyName.Name ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(simpleName)
                || !simpleName.StartsWith("RomM.Platforms.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var platformsRoot = ResolvePlatformsRoot();
            if (string.IsNullOrWhiteSpace(platformsRoot) || !Directory.Exists(platformsRoot))
            {
                return null;
            }

            var assemblyPath = Path.Combine(platformsRoot, simpleName + ".dll");
            if (!File.Exists(assemblyPath))
            {
                return null;
            }

            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
            catch
            {
                return null;
            }
        }

        private static string ResolvePlatformsRoot()
        {
            var pluginRoot = PluginPaths.GetPluginRootDirectory();
            if (string.IsNullOrWhiteSpace(pluginRoot))
            {
                return string.Empty;
            }

            return Path.Combine(pluginRoot, "system", "platforms");
        }
    }
}
