using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Converts Jellyfin's <see cref="BaseItem"/> into the lightweight <see cref="ScheduleCandidate"/>
/// the scheduler works with, so scheduling logic stays independent of Jellyfin's domain types.
/// </summary>
public static class BaseItemCandidateAdapter
{
    /// <summary>
    /// Converts a pool of library items into schedule candidates.
    /// </summary>
    /// <param name="items">The items resolved by <see cref="LibraryPoolResolver"/>.</param>
    /// <returns>The equivalent candidates.</returns>
    public static IReadOnlyList<ScheduleCandidate> ToCandidates(IEnumerable<BaseItem> items)
    {
        return items
            .Where(IsSchedulable)
            .Select(item => new ScheduleCandidate
            {
                ItemId = item.Id,
                Duration = item.RunTimeTicks.HasValue ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : TimeSpan.Zero,
                Rating = item.CommunityRating,
                ReleaseDate = item.PremiereDate,
                SeriesId = (item as Episode)?.SeriesId,
                SeasonNumber = (item as Episode)?.ParentIndexNumber,
                EpisodeNumber = (item as Episode)?.IndexNumber
            }).ToList();
    }

    /// <summary>
    /// Episodes without a series id or season/episode numbers (specials, extras, unmatched files) can't
    /// participate in Active Series tracking — the scheduler's cursor is (series, season, episode) — so
    /// they're excluded here at the boundary instead of null-crashing the generator mid-regeneration.
    /// </summary>
    private static bool IsSchedulable(BaseItem item) =>
        item is not Episode episode ||
        (episode.SeriesId != Guid.Empty && episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue);
}
