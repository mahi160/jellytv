using System;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// A concrete scheduled item in the timeline (e.g. "20:00 Oppenheimer"). Runtime/generated data.
/// </summary>
public class Program
{
    /// <summary>Gets or sets the row id.</summary>
    public long Id { get; set; }

    /// <summary>Gets or sets the name of the Programming Block that produced this Program.</summary>
    public string BlockName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin BaseItem id being aired.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the UTC start time.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Gets or sets the UTC end time.</summary>
    public DateTime EndUtc { get; set; }
}

/// <summary>
/// Tracks a Programming Block's Active Series and its current episode.
/// </summary>
public class ActiveSeriesState
{
    /// <summary>Gets or sets the owning Programming Block's name.</summary>
    public string BlockName { get; set; } = string.Empty;

    /// <summary>Gets or sets the active series' Jellyfin item id.</summary>
    public Guid SeriesId { get; set; }

    /// <summary>Gets or sets the season number of the current episode.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the episode number of the current episode.</summary>
    public int EpisodeNumber { get; set; }
}

/// <summary>
/// Records that a movie aired, for Cooldown enforcement.
/// </summary>
public class MovieHistoryEntry
{
    /// <summary>Gets or sets the movie's Jellyfin item id.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets when it aired (UTC).</summary>
    public DateTime AiredUtc { get; set; }
}
