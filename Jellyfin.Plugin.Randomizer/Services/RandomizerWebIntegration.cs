using System.Runtime.Loader;
using Jellyfin.Plugin.Randomizer.Web;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Randomizer.Services;

public sealed class RandomizerWebIntegration
{
    private static readonly Guid TransformationId =
        Guid.Parse("a2b4adf7-7838-4d4d-a85b-83b2f6b65a2c");

    private readonly ILogger<RandomizerWebIntegration> logger;
    private readonly object gate = new();
    private bool registered;

    public RandomizerWebIntegration(ILogger<RandomizerWebIntegration> logger)
    {
        this.logger = logger;
    }

    public bool IsRegistered
    {
        get
        {
            lock (gate)
            {
                return registered;
            }
        }
    }

    public bool Register()
    {
        lock (gate)
        {
            if (registered)
            {
                return true;
            }

            try
            {
                var pluginInterfaceType = FindPluginInterface();
                if (pluginInterfaceType is null)
                {
                    logger.LogWarning("Randomizer could not find the File Transformation PluginInterface.");
                    return false;
                }

                var registerMethod = pluginInterfaceType.GetMethod("RegisterTransformation");
                if (registerMethod is null)
                {
                    logger.LogWarning("Randomizer found File Transformation but RegisterTransformation was unavailable.");
                    return false;
                }

                var payloadType = registerMethod.GetParameters()[0].ParameterType;
                var parseMethod = payloadType.GetMethod("Parse", new[] { typeof(string) });
                if (parseMethod is null)
                {
                    logger.LogWarning(
                        "Randomizer found File Transformation but its registration payload parser was unavailable on {PayloadType}.",
                        payloadType.FullName);
                    return false;
                }

                var payloadJson =
                    $@"{{""id"":""{TransformationId}"",""fileNamePattern"":""index.html"",""callbackAssembly"":""{typeof(RandomizerWebTransformation).Assembly.FullName}"",""callbackClass"":""{typeof(RandomizerWebTransformation).FullName}"",""callbackMethod"":""{nameof(RandomizerWebTransformation.TransformIndexHtml)}""}}";
                var payload = parseMethod.Invoke(null, new object?[] { payloadJson });

                registerMethod.Invoke(null, new[] { payload });
                registered = true;
                logger.LogInformation("Registered Jellyfin Randomizer Web transformation for index.html.");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to register the Jellyfin Randomizer Web transformation.");
                return false;
            }
        }
    }

    public bool Unregister()
    {
        lock (gate)
        {
            if (!registered)
            {
                return true;
            }

            try
            {
                var pluginInterfaceType = FindPluginInterface();
                if (pluginInterfaceType is null)
                {
                    logger.LogWarning("Randomizer could not find the File Transformation PluginInterface while disabling.");
                    registered = false;
                    return true;
                }

                var removeMethod = pluginInterfaceType.GetMethod(
                    "RemoveTransformation",
                    new[] { typeof(Guid) });

                if (removeMethod is null)
                {
                    logger.LogWarning(
                        "File Transformation does not expose RemoveTransformation; Randomizer will remain runtime-disabled through its callback guard.");
                    registered = false;
                    return true;
                }

                removeMethod.Invoke(null, new object[] { TransformationId });
                registered = false;
                logger.LogInformation("Unregistered Jellyfin Randomizer Web transformation.");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to unregister the Jellyfin Randomizer Web transformation.");
                return false;
            }
        }
    }

    private static Type? FindPluginInterface()
    {
        var assembly = AssemblyLoadContext.All
            .SelectMany(x => x.Assemblies)
            .FirstOrDefault(x =>
                x.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

        return assembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
    }
}
