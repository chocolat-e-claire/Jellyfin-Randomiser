using Jellyfin.Plugin.Randomizer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Randomizer;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<RandomHistoryService>();
        services.AddSingleton<RandomizerService>();
    }
}