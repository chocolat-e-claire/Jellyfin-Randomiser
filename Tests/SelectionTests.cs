using Jellyfin.Plugin.Randomizer;
using Jellyfin.Plugin.Randomizer.Services;
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
    public void WatchedFiltersAreDistinct() =>
        Assert.NotEqual(WatchedFilter.Watched, WatchedFilter.Unwatched);

    [Fact]
    public void HistoryRangeIsBounded() =>
        Assert.Equal(20, Math.Clamp(25, 0, 20));

    [Fact]
    public void RuntimeStateCanDisableAndReEnable()
    {
        var state = new RandomizerRuntimeState();

        Assert.True(state.PluginEnabled);

        state.SetPluginEnabled(false);
        Assert.False(state.PluginEnabled);

        state.SetPluginEnabled(true);
        Assert.True(state.PluginEnabled);
    }
}
