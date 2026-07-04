using System;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Scheduling;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class BaseItemCandidateAdapterTests
{
    private static Episode EpisodeItem(Guid? seriesId = null, int? season = 1, int? episode = 1) => new()
    {
        Id = Guid.NewGuid(),
        SeriesId = seriesId ?? Guid.NewGuid(),
        ParentIndexNumber = season,
        IndexNumber = episode,
        RunTimeTicks = TimeSpan.FromMinutes(30).Ticks
    };

    [Fact]
    public void ToCandidates_MapsEpisodeFields()
    {
        var seriesId = Guid.NewGuid();
        var item = EpisodeItem(seriesId, season: 2, episode: 5);

        var candidates = BaseItemCandidateAdapter.ToCandidates(new[] { item });

        var c = Assert.Single(candidates);
        Assert.Equal(item.Id, c.ItemId);
        Assert.Equal(seriesId, c.SeriesId);
        Assert.Equal(2, c.SeasonNumber);
        Assert.Equal(5, c.EpisodeNumber);
        Assert.Equal(TimeSpan.FromMinutes(30), c.Duration);
    }

    [Fact]
    public void ToCandidates_DropsEpisodesThatCannotBeTracked()
    {
        // Specials/extras/unmatched files without season/episode numbers or a series id used to
        // null-crash the generator's Active Series cursor mid-regeneration — they must be excluded.
        var items = new[]
        {
            EpisodeItem(),
            EpisodeItem(season: null),
            EpisodeItem(episode: null),
            EpisodeItem(seriesId: Guid.Empty)
        };

        var candidates = BaseItemCandidateAdapter.ToCandidates(items);

        Assert.Equal(items[0].Id, Assert.Single(candidates).ItemId);
    }

    [Fact]
    public void ToCandidates_KeepsMovies_EvenWithoutEpisodeFields()
    {
        var movie = new Movie { Id = Guid.NewGuid(), RunTimeTicks = TimeSpan.FromHours(2).Ticks };

        var candidates = BaseItemCandidateAdapter.ToCandidates(new[] { movie });

        var c = Assert.Single(candidates);
        Assert.Equal(movie.Id, c.ItemId);
        Assert.Null(c.SeriesId);
    }
}
