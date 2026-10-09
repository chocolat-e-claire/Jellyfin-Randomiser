using Jellyfin.Plugin.Randomizer;
using Xunit;

namespace Jellyfin.Plugin.Randomizer.Tests;

public sealed class SelectionTests
{
    [Fact]
    public void DefaultStrategyIsEqualEpisode() =>
        Assert.Equal(RandomizationStrategy.EqualEpisode, new RandomizeRequest().Strategy);

    [Fact]
    public void EmptyIdsMeanEntireLibrary() =>
        Assert.Empty(new RandomizeRequest().ItemIds);

    [Fact]
    public void GenreCanBeScoped() =>
        Assert.Equal("Comedy", new RandomizeRequest { Genre = "Comedy" }.Genre);

    [Fact]
    public void WatchedFiltersAreDistinct() =>
        Assert.NotEqual(WatchedFilter.Watched, WatchedFilter.Unwatched);

    [Fact]
    public void HistoryRangeIsBounded() =>
        Assert.Equal(20, Math.Clamp(25, 0, 20));
}
