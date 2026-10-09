using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Randomizer;

public enum RandomizationMode
{
    RandomMovie,
    RandomShow,
    RandomEpisode
}

public enum RandomizationStrategy
{
    EqualShow,
    EqualEpisode
}

public enum WatchedFilter
{
    All,
    Unwatched,
    Watched
}

public sealed class RandomizeRequest
{
    public RandomizationMode Mode { get; set; }
    public Guid? LibraryId { get; set; }
    public string? Genre { get; set; }
    public List<Guid> ItemIds { get; set; } = new();
    public RandomizationStrategy Strategy { get; set; } = RandomizationStrategy.EqualEpisode;
    public WatchedFilter Watched { get; set; } = WatchedFilter.All;
    public int AvoidRecent { get; set; }
}

public sealed record LibraryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name);

public sealed class RandomizeResult
{
    [JsonPropertyName("itemId")]
    public Guid ItemId { get; init; }

    [JsonPropertyName("itemType")]
    public string ItemType { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("seriesName")]
    public string? SeriesName { get; init; }

    [JsonPropertyName("seasonNumber")]
    public int? SeasonNumber { get; init; }

    [JsonPropertyName("episodeNumber")]
    public int? EpisodeNumber { get; init; }

    [JsonPropertyName("overview")]
    public string? Overview { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }
}
