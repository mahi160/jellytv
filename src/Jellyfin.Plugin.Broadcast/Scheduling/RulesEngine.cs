using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Broadcast.Configuration;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Translates a Programming Block's filters into a Jellyfin library query.
/// V1 filters: Library, Genre, Tags, Rating, Year range, Favorite.
/// Studio/Director/Actor/Collection/Runtime/Resolution/custom metadata are deferred until a block actually needs them.
/// </summary>
public static class RulesEngine
{
    /// <summary>
    /// Builds the library query for a Programming Block's filters (excluding the Library filter,
    /// which needs folder id lookup and is applied separately — see <see cref="LibraryPoolResolver"/>).
    /// </summary>
    /// <param name="block">The Programming Block.</param>
    /// <returns>An <see cref="InternalItemsQuery"/> ready to run against Jellyfin's library.</returns>
    public static InternalItemsQuery BuildQuery(ProgrammingBlock block)
    {
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = block.ContentType == BlockContentType.Movie
                ? new[] { BaseItemKind.Movie }
                : new[] { BaseItemKind.Episode }
        };

        if (block.Genres.Count > 0)
        {
            query.Genres = block.Genres;
        }

        if (block.Tags.Count > 0)
        {
            query.Tags = block.Tags.ToArray();
        }

        if (block.MinRating.HasValue)
        {
            query.MinCommunityRating = block.MinRating;
        }

        if (block.MinYear.HasValue)
        {
            query.MinPremiereDate = new DateTime(block.MinYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        if (block.MaxYear.HasValue)
        {
            query.MaxPremiereDate = new DateTime(block.MaxYear.Value, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        }

        if (block.FavoritesOnly)
        {
            query.IsFavorite = true;
        }

        return query;
    }
}
