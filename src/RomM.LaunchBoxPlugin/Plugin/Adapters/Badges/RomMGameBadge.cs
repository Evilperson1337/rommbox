using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;
using RomMbox.Plugin;
using RomMbox.Services;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Plugin.Adapters.Badges
{
    [Export(typeof(IGameBadge))]
    public sealed class RomMGameBadge : IGameBadge
    {
        private static readonly Lazy<Image> DefaultIconCache = new(LoadDefaultIcon);
        private static readonly Lazy<RomMGameBadgeDetector> DetectorCache = new(() => new RomMGameBadgeDetector(PluginEntry.Logger));

        public RomMGameBadge()
        {
            PluginEntry.EnsureInitialized();
        }

        public string Name => "RomM";

        public string UniqueId => "RomM";

        public Image DefaultIcon => DefaultIconCache.Value;

        public int Index { get; set; } = 240;

        public bool GetAppliesToGame(IGame game)
        {
            var applies = DetectorCache.Value.AppliesTo(game);
            PluginEntry.Logger?.Debug($"[RomM Badge] GetAppliesToGame returned {applies} for GameId={game?.Id ?? string.Empty} Title=\"{game?.Title ?? string.Empty}\".");
            return applies;
        }

        private static Image LoadDefaultIcon()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                PluginEntry.Logger?.Debug("[RomM Badge] DefaultIcon requested.");
                return new BadgeAssetService(PluginEntry.Logger).LoadBadgeImage();
            }
            catch (Exception ex)
            {
                PluginEntry.Logger?.Error("Failed to load RomM badge icon.", ex);
                return null;
            }
            finally
            {
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > 50)
                {
                    PluginEntry.Logger?.Warning($"RomM badge icon load slow. DurationMs={stopwatch.ElapsedMilliseconds}.");
                }
            }
        }
    }
}
