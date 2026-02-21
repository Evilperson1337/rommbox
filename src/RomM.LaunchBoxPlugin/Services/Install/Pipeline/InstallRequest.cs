using System;
using Unbroken.LaunchBox.Plugins.Data;

namespace RomMbox.Services.Install.Pipeline
{
    internal sealed class InstallRequest
    {
        public InstallRequest(IGame game, IDataManager dataManager)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            DataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        }

        public IGame Game { get; }
        public IDataManager DataManager { get; }
    }
}
