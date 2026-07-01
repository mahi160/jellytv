using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Scheduling;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class RulesEngineTests
{
    [Fact]
    public void BuildQuery_MovieBlock_IncludesOnlyMovies()
    {
        var block = new ProgrammingBlock { ContentType = BlockContentType.Movie };
        var query = RulesEngine.BuildQuery(block);

        Assert.Equal(new[] { BaseItemKind.Movie }, query.IncludeItemTypes);
    }

    [Fact]
    public void BuildQuery_EpisodeBlock_IncludesOnlyEpisodes()
    {
        var block = new ProgrammingBlock { ContentType = BlockContentType.Episode };
        var query = RulesEngine.BuildQuery(block);

        Assert.Equal(new[] { BaseItemKind.Episode }, query.IncludeItemTypes);
    }

    [Fact]
    public void BuildQuery_AppliesGenreTagRatingYearAndFavoriteFilters()
    {
        var block = new ProgrammingBlock
        {
            Genres = new() { "Action" },
            Tags = new() { "explicit" },
            MinRating = 7.0,
            MinYear = 2010,
            MaxYear = 2020,
            FavoritesOnly = true
        };

        var query = RulesEngine.BuildQuery(block);

        Assert.Equal(new[] { "Action" }, query.Genres);
        Assert.Equal(new[] { "explicit" }, query.Tags);
        Assert.Equal(7.0, query.MinCommunityRating);
        Assert.Equal(new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc), query.MinPremiereDate);
        Assert.Equal(new DateTime(2020, 12, 31, 23, 59, 59, DateTimeKind.Utc), query.MaxPremiereDate);
        Assert.True(query.IsFavorite);
    }

    [Fact]
    public void BuildQuery_NoFiltersSet_LeavesThemNull()
    {
        var block = new ProgrammingBlock();
        var query = RulesEngine.BuildQuery(block);

        Assert.Empty(query.Genres);
        Assert.Empty(query.Tags);
        Assert.Null(query.MinCommunityRating);
        Assert.Null(query.MinPremiereDate);
        Assert.Null(query.MaxPremiereDate);
        Assert.Null(query.IsFavorite);
    }
}
