using System;
using Jellyfin.Plugin.Broadcast.Data;

namespace Jellyfin.Plugin.Broadcast.Playback;

/// <summary>
/// A Program paired with how far into it playback should start right now.
/// </summary>
public class ResolvedProgram
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResolvedProgram"/> class.
    /// </summary>
    /// <param name="program">The currently airing Program.</param>
    /// <param name="offset">How far into the Program "now" falls.</param>
    public ResolvedProgram(Program program, TimeSpan offset)
    {
        Program = program;
        Offset = offset;
    }

    /// <summary>Gets the currently airing Program.</summary>
    public Program Program { get; }

    /// <summary>Gets the elapsed playback time — where Jellyfin playback should start from.</summary>
    public TimeSpan Offset { get; }
}
