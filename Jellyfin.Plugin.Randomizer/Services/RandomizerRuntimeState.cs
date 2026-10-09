using System.Threading;

namespace Jellyfin.Plugin.Randomizer.Services;

public sealed class RandomizerRuntimeState
{
    private int pluginEnabled = 1;

    public bool PluginEnabled => Volatile.Read(ref pluginEnabled) == 1;

    public void SetPluginEnabled(bool enabled) =>
        Volatile.Write(ref pluginEnabled, enabled ? 1 : 0);
}
