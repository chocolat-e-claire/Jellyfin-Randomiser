using Jellyfin.Plugin.Randomizer.Web;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Randomizer.Services;

public sealed class RandomizerStartupService : IScheduledTask
{
    private readonly RandomizerWebIntegration webIntegration;
    private readonly ILogger<RandomizerStartupService> logger;

    public RandomizerStartupService(
        RandomizerWebIntegration webIntegration,
        ILogger<RandomizerStartupService> logger)
    {
        this.webIntegration = webIntegration;
        this.logger = logger;
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Randomizer startup service executing.");
        await Task.Yield();

        if (Plugin.Instance?.IsRuntimeEnabled != true)
        {
            webIntegration.Unregister();
            logger.LogInformation("Randomizer startup service is inactive; Web integration was not registered.");
            return;
        }

        webIntegration.Register();
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerStartup
        };
    }

    public string Name => "Jellyfin Randomizer Startup";

    public string Key => "Jellyfin.Plugin.Randomizer.Startup";

    public string Description => "Registers the optional Jellyfin Web transformation used by Jellyfin Randomizer.";

    public string Category => "Startup Services";
}
