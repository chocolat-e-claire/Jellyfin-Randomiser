using Jellyfin.Plugin.Randomizer.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Randomizer;

public sealed class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId =
        Guid.Parse("4e1a3b62-3d7f-4d8f-a0a9-2f2f3c9d7c41");

    private readonly RandomizerRuntimeState runtimeState;

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        RandomizerRuntimeState runtimeState)
        : base(applicationPaths, xmlSerializer)
    {
        this.runtimeState = runtimeState;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Jellyfin Randomizer";

    public override Guid Id => PluginId;

    public bool IsRuntimeEnabled =>
        runtimeState.PluginEnabled && Configuration.Enabled;

    public override string Description =>
        "Server-side random selection for Jellyfin 10.10.7.";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace +
                ".Configuration.configPage.html"
        }
    };
}
