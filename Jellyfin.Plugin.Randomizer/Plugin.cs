using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Randomizer;

public sealed class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }
    public override string Name => "Jellyfin Randomizer";
    public override Guid Id => Guid.Parse("4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41");
    public override string Description => "Server-side random selection for Jellyfin 10.10.7.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "randomizer-config",
            DisplayName = Name,
            MenuIcon = "shuffle",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
        };
    }
}