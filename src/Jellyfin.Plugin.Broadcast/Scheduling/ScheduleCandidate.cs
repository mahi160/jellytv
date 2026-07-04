using System;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// A library item eligible to be scheduled, with just the fields the generator needs.
/// Decouples scheduling logic from Jellyfin's BaseItem so it can be unit tested without a live server.
/// </summary>
public class ScheduleCandidate
{
    /// <summary>Gets or sets the Jellyfin item id.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets how long the item runs.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Gets or sets the community rating, if any (drives WeightedRandom/Rating).</summary>
    public double? Rating { get; set; }

    /// <summary>Gets or sets the release date, if known (drives Chronological/NewestFirst/OldestFirst/Recency).</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>Gets or sets the owning series' id. Null for movies; never null for episodes
    /// (<see cref="BaseItemCandidateAdapter"/> drops episodes that can't be tracked).</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>Gets or sets the season number. Null for movies; never null for episodes.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number within the season. Null for movies; never null for episodes.</summary>
    public int? EpisodeNumber { get; set; }
}
