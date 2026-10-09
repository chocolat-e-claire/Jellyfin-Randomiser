using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Randomizer.Web;

public sealed class FileTransformationPayload
{
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}

public static class RandomizerWebTransformation
{
    public static string TransformIndexHtml(FileTransformationPayload payload)
    {
        var contents = payload.Contents ?? string.Empty;

        if (contents.Contains("data-jellyfin-randomizer-loader", StringComparison.Ordinal)
            || Plugin.Instance?.IsRuntimeEnabled != true)
        {
            return contents;
        }

        var version = Plugin.Instance?.Version.ToString() ?? "0.1.0.10";
        var injection = $"""
            <link data-jellyfin-randomizer-loader rel="stylesheet" href="../Randomizer/Styles.css?v={version}">
            <script data-jellyfin-randomizer-loader src="../Randomizer/Script.js?v={version}" defer></script>
            """;

        var closingBody = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return closingBody >= 0
            ? contents.Insert(closingBody, injection)
            : contents + injection;
    }
}
