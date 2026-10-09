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
    private readonly ILogger<RandomizerPluginStateMonitor> logger;
    private readonly object syncGate = new();

    private CancellationTokenSource? cancellationTokenSource;
    private Task? monitorTask;
    private FileSystemWatcher? manifestWatcher;

    public RandomizerPluginStateMonitor(
        IPluginManager pluginManager,
        RandomizerRuntimeState runtimeState,
        ILogger<RandomizerPluginStateMonitor> logger)
    {
        this.pluginManager = pluginManager;
        this.runtimeState = runtimeState;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConfigureManifestWatcher();
        SyncPersistedState();
        monitorTask = MonitorAsync(cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        manifestWatcher?.Dispose();
        manifestWatcher = null;

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
            SyncPersistedState();

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

    private void ConfigureManifestWatcher()
    {
        var plugin = pluginManager.GetPlugin(Plugin.PluginId);
        if (plugin is null || !Directory.Exists(plugin.Path))
        {
            return;
        }

        manifestWatcher = new FileSystemWatcher(plugin.Path, "meta.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        manifestWatcher.Changed += OnManifestChanged;
        manifestWatcher.Created += OnManifestChanged;
        manifestWatcher.Renamed += OnManifestChanged;
    }

    private void OnManifestChanged(object sender, FileSystemEventArgs e) =>
        SyncPersistedState();

    private void SyncPersistedState()
    {
        lock (syncGate)
        {
            try
            {
                var plugin = pluginManager.GetPlugin(Plugin.PluginId);
                if (plugin is null || !TryReadPersistedStatus(plugin.Path, out var desiredStatus))
                {
                    return;
                }

                if (desiredStatus is not (PluginStatus.Active or PluginStatus.Disabled))
                {
                    return;
                }

                if (plugin.Manifest.Status != desiredStatus)
                {
                    plugin.Manifest.Status = desiredStatus;
                }

                var enabled = desiredStatus == PluginStatus.Active;
                if (runtimeState.PluginEnabled == enabled)
                {
                    return;
                }

                runtimeState.SetPluginEnabled(enabled);
                logger.LogInformation(
                    "Jellyfin Randomizer plugin manager state synchronized to {Status}.",
                    desiredStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while synchronizing Jellyfin Randomizer plugin state.");
            }
        }
    }

    private static bool TryReadPersistedStatus(string pluginPath, out PluginStatus status)
    {
        status = PluginStatus.Active;
        var path = Path.Combine(pluginPath, "meta.json");

        try
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("status", out var value))
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var numericStatus)
                && Enum.IsDefined(typeof(PluginStatus), numericStatus))
            {
                status = (PluginStatus)numericStatus;
                return true;
            }

            if (value.ValueKind == JsonValueKind.String
                && Enum.TryParse<PluginStatus>(value.GetString(), true, out var parsedStatus))
            {
                status = parsedStatus;
                return true;
            }

            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
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
