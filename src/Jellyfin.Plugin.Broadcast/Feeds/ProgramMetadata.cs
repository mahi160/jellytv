using System;

namespace Jellyfin.Plugin.Broadcast.Feeds;

/// <summary>
/// A Program enriched with the display metadata XMLTV needs (title/description/artwork).
/// Kept separate from <see cref="Data.Program"/> so feed generation doesn't need a live Jellyfin library to be unit tested.
/// </summary>
public class ProgramMetadata
{
    /// <summary>Gets or sets the UTC start time.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Gets or sets the UTC end time.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Gets or sets the item's display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the item's description/overview, if any.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets a URL to the item's primary artwork, if any.</summary>
    public string? ArtworkUrl { get; set; }
}
