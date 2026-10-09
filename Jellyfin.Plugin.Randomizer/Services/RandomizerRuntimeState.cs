using System.Text.Json;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Randomizer.Services;

public sealed class RandomizerRuntimeState : BackgroundService
{
    private readonly IPluginManager pluginManager;
    private readonly ILogger<RandomizerRuntimeState> logger;
    private readonly SemaphoreSlim refreshSignal = new(0, 1);
    private FileSystemWatcher? watcher;
    private bool managerEnabled = true;

    public static RandomizerRuntimeState? Instance { get; private set; }

    public bool IsEnabled =>
        managerEnabled && Plugin.Instance?.Configuration.Enabled != false;

    public RandomizerRuntimeState(
        IPluginManager pluginManager,
        ILogger<RandomizerRuntimeState> logger)
    {
        this.pluginManager = pluginManager;
        this.logger = logger;
        Instance = this;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RefreshFromDisk();

        try
        {
            ConfigureWatcher();

            while (!stoppingToken.IsCancellationRequested)
            {
                var delayTask = Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                var signalTask = refreshSignal.WaitAsync(stoppingToken);

                await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
                RefreshFromDisk();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void ConfigureWatcher()
    {
        var plugin = GetLocalPlugin();
        if (plugin is null || string.IsNullOrEmpty(plugin.Path) || !Directory.Exists(plugin.Path))
        {
            logger.LogWarning("Randomizer could not locate its plugin directory for live enable/disable monitoring.");
            return;
        }

        watcher = new FileSystemWatcher(plugin.Path, "meta.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        watcher.Changed += OnManifestChanged;
        watcher.Created += OnManifestChanged;
        watcher.Renamed += OnManifestChanged;
        watcher.EnableRaisingEvents = true;
    }

    private void OnManifestChanged(object sender, FileSystemEventArgs args)
    {
        if (refreshSignal.CurrentCount == 0)
        {
            refreshSignal.Release();
        }
    }

    private void RefreshFromDisk()
    {
        var plugin = GetLocalPlugin();
        if (plugin is null)
        {
            return;
        }

        var manifestPath = Path.Combine(plugin.Path, "meta.json");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = File.OpenRead(manifestPath);
                using var document = JsonDocument.Parse(stream);

                var status = document.RootElement.TryGetProperty("status", out var statusElement)
                    && statusElement.ValueKind == JsonValueKind.Number
                    && statusElement.TryGetInt32(out var rawStatus)
                    ? (PluginStatus)rawStatus
                    : PluginStatus.Active;

                var normalizedStatus = status == PluginStatus.Disabled
                    ? PluginStatus.Disabled
                    : PluginStatus.Active;

                managerEnabled = normalizedStatus == PluginStatus.Active;

                if (plugin.Manifest.Status != normalizedStatus)
                {
                    plugin.Manifest.Status = normalizedStatus;
                    logger.LogInformation(
                        "Randomizer live plugin state synchronized to {Status}.",
                        normalizedStatus);
                }

                return;
            }
            catch (FileNotFoundException) when (attempt < 4)
            {
                Thread.Sleep(25);
            }
            catch (DirectoryNotFoundException) when (attempt < 4)
            {
                Thread.Sleep(25);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(25);
            }
            catch (JsonException) when (attempt < 4)
            {
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Randomizer could not read its plugin manifest for live state.");
                return;
            }
        }
    }

    private LocalPlugin? GetLocalPlugin()
    {
        return Plugin.Instance is { } instance
            ? pluginManager.GetPlugin(instance.Id, instance.Version)
            : null;
    }

    public override void Dispose()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        watcher?.Dispose();
        refreshSignal.Dispose();
        base.Dispose();
    }
}
