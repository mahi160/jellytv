using System;

namespace Jellyfin.Plugin.Broadcast.Api;

/// <summary>
/// A Program as returned by the REST API.
/// </summary>
public class ProgramDto
{
    /// <summary>Gets or sets the Jellyfin item id being aired.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the Programming Block that produced this Program.</summary>
    public string BlockName { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC start time.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Gets or sets the UTC end time.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Gets or sets how many seconds into the item playback should start, if applicable (Current only).</summary>
    public double? OffsetSeconds { get; set; }

    /// <summary>
    /// Builds a DTO from a Program.
    /// </summary>
    /// <param name="program">The Program.</param>
    /// <param name="offset">The playback offset, if resolving "current".</param>
    /// <returns>The DTO.</returns>
    public static ProgramDto From(Data.Program program, TimeSpan? offset) => new()
    {
        ItemId = program.ItemId,
        BlockName = program.BlockName,
        StartUtc = program.StartUtc,
        EndUtc = program.EndUtc,
        OffsetSeconds = offset?.TotalSeconds
    };
}
