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
        logger.LogInformation("Randomizer startup service executing.");
        await Task.Yield();

        try
        {
            var fileTransformationAssembly =
                AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

            if (fileTransformationAssembly is null)
            {
                logger.LogWarning("Randomizer could not find the File Transformation assembly.");
                return;
            }

            logger.LogInformation("Randomizer found File Transformation assembly {Assembly}.", fileTransformationAssembly.FullName);

            var pluginInterfaceType = fileTransformationAssembly.GetType(
                "Jellyfin.Plugin.FileTransformation.PluginInterface");

            if (pluginInterfaceType is null)
            {
                logger.LogWarning("Randomizer found File Transformation but PluginInterface type was unavailable.");
                return;
            }

            var registerMethod = pluginInterfaceType.GetMethod("RegisterTransformation");

            if (registerMethod is null)
            {
                logger.LogWarning("Randomizer found File Transformation PluginInterface but RegisterTransformation was unavailable.");
                return;
            }

            var payload = new JObject
            {
                ["id"] = TransformationId,
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = typeof(RandomizerWebTransformation).Assembly.FullName,
                ["callbackClass"] = typeof(RandomizerWebTransformation).FullName,
                ["callbackMethod"] = nameof(RandomizerWebTransformation.TransformIndexHtml)
            };

            registerMethod.Invoke(null, new object?[] { payload });
            logger.LogInformation("Registered Jellyfin Randomizer Web transformation for index.html.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and ex is not StackOverflowException)
        {
            logger.LogError(ex, "Unable to register the Jellyfin Randomizer Web transformation.");
        }
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