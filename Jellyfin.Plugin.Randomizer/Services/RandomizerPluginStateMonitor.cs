using System.Text.Json;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Randomizer.Services;

public sealed class RandomizerPluginStateMonitor : IHostedService
{
    private readonly IPluginManager pluginManager;
    private readonly RandomizerRuntimeState runtimeState;
    private readonly RandomizerWebIntegration webIntegration;
    private readonly ILogger<RandomizerPluginStateMonitor> logger;

    private CancellationTokenSource? cancellationTokenSource;
    private Task? monitorTask;

    public RandomizerPluginStateMonitor(
        IPluginManager pluginManager,
        RandomizerRuntimeState runtimeState,
        RandomizerWebIntegration webIntegration,
        ILogger<RandomizerPluginStateMonitor> logger)
    {
        this.pluginManager = pluginManager;
        this.runtimeState = runtimeState;
        this.webIntegration = webIntegration;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        monitorTask = MonitorAsync(cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (cancellationTokenSource is null || monitorTask is null)
        {
            return;
        }

        await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await monitorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var plugin = pluginManager.Plugins.FirstOrDefault(p => p.Id == Plugin.PluginId);
                if (plugin is not null && TryReadPersistedEnabled(plugin.Path, out var enabled))
                {
                    SyncJellyfinUiStatus(plugin, enabled);

                    if (enabled != runtimeState.PluginEnabled)
                    {
                        runtimeState.SetPluginEnabled(enabled);

                        if (enabled)
                        {
                            webIntegration.Register();
                            logger.LogInformation("Jellyfin Randomizer runtime enable detected.");
                        }
                        else
                        {
                            webIntegration.Unregister();
                            logger.LogInformation("Jellyfin Randomizer runtime disable detected.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while monitoring Jellyfin Randomizer plugin state.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static void SyncJellyfinUiStatus(LocalPlugin plugin, bool enabled)
    {
        if (plugin.Manifest.Status != PluginStatus.Restart)
        {
            return;
        }

        plugin.Manifest.Status = enabled
            ? PluginStatus.Active
            : PluginStatus.Disabled;
    }

    private static bool TryReadPersistedEnabled(string pluginPath, out bool enabled)
    {
        enabled = true;
        var path = Path.Combine(pluginPath, "meta.json");

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("status", out var status))
            {
                return true;
            }

            if (status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var numericStatus))
            {
                enabled = numericStatus != (int)PluginStatus.Disabled;
                return true;
            }

            if (status.ValueKind == JsonValueKind.String)
            {
                var value = status.GetString();
                enabled = !string.Equals(value, nameof(PluginStatus.Disabled), StringComparison.OrdinalIgnoreCase);
                return true;
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
