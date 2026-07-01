using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Broadcast.Configuration;

/// <summary>
/// What happens when a series' Active Episode is its last one.
/// </summary>
public enum SeriesEndBehavior
{
    /// <summary>Restart the same series from episode 1.</summary>
    Restart,

    /// <summary>Pick another series matching the block's filters (using the block's ordering strategy).</summary>
    SelectAnotherSeries,

    /// <summary>Pick a random series matching the block's filters.</summary>
    Random
}

/// <summary>
/// Supported ordering strategies for picking items within a Programming Block.
/// </summary>
public enum OrderingStrategy
{
    /// <summary>Play items in a fixed sequence.</summary>
    Sequential,

    /// <summary>Play items in random order.</summary>
    Random,

    /// <summary>Play items randomly, weighted by <see cref="ProgrammingBlock.WeightFactor"/>.</summary>
    WeightedRandom,

    /// <summary>Oldest release date first.</summary>
    Chronological,

    /// <summary>Newest release date first.</summary>
    NewestFirst,

    /// <summary>Oldest release date first (alias kept distinct from Chronological for clarity in UI).</summary>
    OldestFirst,

    /// <summary>Items played least often first.</summary>
    LeastPlayed,

    /// <summary>Items never played first.</summary>
    NeverPlayed
}

/// <summary>
/// The factor that drives WeightedRandom ordering.
/// </summary>
public enum WeightFactor
{
    /// <summary>Weight by community/critic rating.</summary>
    Rating,

    /// <summary>Weight by recency (newer = more likely).</summary>
    Recency,

    /// <summary>Weight by play count (fewer plays = more likely).</summary>
    PlayCount
}

/// <summary>
/// A recurring rule defining a time range, media filters, and ordering strategy.
/// Produces Programs (concrete scheduled items) when the schedule is generated.
/// </summary>
public class ProgrammingBlock
{
    /// <summary>Gets or sets the display name, e.g. "Prime Time".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the daily start time (e.g. "19:00").</summary>
    public string StartTime { get; set; } = "00:00";

    /// <summary>Gets or sets the daily end time (e.g. "23:00"). May be past midnight relative to StartTime.</summary>
    public string EndTime { get; set; } = "00:00";

    /// <summary>Gets or sets the Jellyfin library names to draw from. Empty = all libraries.</summary>
    public List<string> Libraries { get; set; } = new();

    /// <summary>Gets or sets required genres (AND-combined with other filters; OR within this list).</summary>
    public List<string> Genres { get; set; } = new();

    /// <summary>Gets or sets required tags.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Gets or sets the minimum community rating (e.g. 7.0), or null for no minimum.</summary>
    public double? MinRating { get; set; }

    /// <summary>Gets or sets the minimum release year, or null for no minimum.</summary>
    public int? MinYear { get; set; }

    /// <summary>Gets or sets the maximum release year, or null for no maximum.</summary>
    public int? MaxYear { get; set; }

    /// <summary>Gets or sets a value indicating whether only items marked Favorite are eligible.</summary>
    public bool FavoritesOnly { get; set; }

    /// <summary>Gets or sets the media kind this block schedules.</summary>
    public BlockContentType ContentType { get; set; } = BlockContentType.Movie;

    /// <summary>Gets or sets the ordering strategy for picking items in this block.</summary>
    public OrderingStrategy Order { get; set; } = OrderingStrategy.Random;

    /// <summary>Gets or sets the factor driving WeightedRandom ordering (ignored otherwise).</summary>
    public WeightFactor WeightFactor { get; set; } = WeightFactor.Rating;

    /// <summary>Gets or sets the movie replay cooldown, in days, for this block. Auto-relaxed if the pool is too small.</summary>
    public int MovieCooldownDays { get; set; } = 30;

    /// <summary>Gets or sets what happens when a series' Active Episode is its last one (ContentType = Episode only).</summary>
    public SeriesEndBehavior SeriesEndBehavior { get; set; } = SeriesEndBehavior.SelectAnotherSeries;
}

/// <summary>
/// What kind of media a Programming Block schedules.
/// </summary>
public enum BlockContentType
{
    /// <summary>Movies.</summary>
    Movie,

    /// <summary>TV episodes.</summary>
    Episode
}

/// <summary>
/// Plugin configuration: channel settings and the list of Programming Blocks.
/// Generated/runtime state (Schedules, EpisodeState, MovieHistory) lives in SQLite instead.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ChannelName = "My TV";
        TimeZone = "UTC";
        ScheduleDurationDays = 7;
        RegenerationTime = "04:00";
        Blocks = new List<ProgrammingBlock>();
    }

    /// <summary>Gets or sets the channel's display name.</summary>
    public string ChannelName { get; set; }

    /// <summary>Gets or sets an optional logo URL for the channel.</summary>
    public string? ChannelLogoUrl { get; set; }

    /// <summary>Gets or sets the IANA timezone used to resolve schedule times (e.g. "America/New_York").</summary>
    public string TimeZone { get; set; }

    /// <summary>Gets or sets how many days ahead the schedule is generated (1, 7, or 30).</summary>
    public int ScheduleDurationDays { get; set; }

    /// <summary>Gets or sets the time of day (HH:mm) automatic daily regeneration runs.</summary>
    public string RegenerationTime { get; set; }

    /// <summary>Gets or sets the configured Programming Blocks.</summary>
    public List<ProgrammingBlock> Blocks { get; set; }
}
