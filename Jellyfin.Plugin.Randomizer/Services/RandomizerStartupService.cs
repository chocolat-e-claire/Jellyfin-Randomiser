using System.Reflection;
using System.Runtime.Loader;
using Jellyfin.Plugin.Randomizer.Web;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Randomizer.Services;

public sealed class RandomizerStartupService : IScheduledTask
{
    private static readonly Guid TransformationId = Guid.Parse("a2b4adf7-7838-4d4d-a85b-83b2f6b65a2c");
    private readonly ILogger<RandomizerStartupService> logger;

    public RandomizerStartupService(ILogger<RandomizerStartupService> logger)
    {
        this.logger = logger;
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await Task.Yield();

        try
        {
            var fileTransformationAssembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(assembly =>
                    assembly.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);

            if (fileTransformationAssembly is null)
            {
                logger.LogDebug("Jellyfin File Transformation plugin was not found; Randomizer Web integration is disabled.");
                return;
            }

            var pluginInterfaceType = fileTransformationAssembly.GetType(
                "Jellyfin.Plugin.FileTransformation.PluginInterface");

            var registerMethod = pluginInterfaceType?.GetMethod("RegisterTransformation");

            if (registerMethod is null)
            {
                logger.LogWarning("Jellyfin File Transformation plugin was found, but its registration API was unavailable.");
                return;
            }

            var payload = new JObject
            {
                ["id"] = TransformationId,
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = GetType().Assembly.FullName,
                ["callbackClass"] = typeof(RandomizerWebTransformation).FullName,
                ["callbackMethod"] = nameof(RandomizerWebTransformation.TransformIndexHtml)
            };

            registerMethod.Invoke(null, new object?[] { payload });
            logger.LogInformation("Registered Jellyfin Randomizer Web transformation for index.html.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to register the Jellyfin Randomizer Web transformation. The server plugin will continue without Web injection.");
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }

    public string Name => "Jellyfin Randomizer Startup";

    public string Key => "Jellyfin.Plugin.Randomizer.Startup";

    public string Description => "Registers the optional Jellyfin Web transformation used by Jellyfin Randomizer.";

    public string Category => "Startup Services";
}