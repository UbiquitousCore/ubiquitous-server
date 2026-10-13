using Microsoft.Extensions.Logging;

namespace Ubiquitous.Modules.Playerbots;

public sealed class PlayerbotsModule : IServerModule
{
    public string Name => "playerbots";

    public BotManager? Manager { get; private set; }

    public void Register(IModuleHost host)
    {
        var settings = PlayerbotSettings.From(host.Settings);
        if (!settings.Enabled)
        {
            host.Logger.LogInformation("Playerbots are off; set modules.playerbots.enabled to start them");
            return;
        }

        Manager = new BotManager(host, settings);
        host.OnWorldTick(Manager.TickAsync);
        host.OnShutdown(Manager.ShutdownAsync);
        host.Logger.LogInformation("Playerbots on: {Count} random bots", settings.RandomBotCount);
    }
}
