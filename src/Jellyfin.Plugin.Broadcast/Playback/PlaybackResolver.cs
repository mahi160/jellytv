using System;
using Jellyfin.Plugin.Broadcast.Data;

namespace Jellyfin.Plugin.Broadcast.Playback;

/// <summary>
/// Resolves what's currently/next airing on the channel and how far into it playback should start,
/// per the PRD's Live Position Resolution: find the scheduled item, then calculate the elapsed offset.
/// </summary>
public class PlaybackResolver
{
    private readonly ProgramRepository _programs;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackResolver"/> class.
    /// </summary>
    /// <param name="programs">The generated schedule.</param>
    public PlaybackResolver(ProgramRepository programs)
    {
        _programs = programs;
    }

    /// <summary>
    /// Resolves the Program airing right now and the offset playback should start at.
    /// </summary>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <returns>The resolved Program and offset, or null if nothing is scheduled at that time.</returns>
    public ResolvedProgram? GetCurrent(DateTime nowUtc)
    {
        var program = _programs.GetProgramAt(nowUtc);
        return program is null ? null : new ResolvedProgram(program, nowUtc - program.StartUtc);
    }

    /// <summary>
    /// Gets the Program that will air after whatever is currently airing (or after "now" if nothing is airing).
    /// </summary>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <returns>The next Program, or null if none is scheduled.</returns>
    public Program? GetNext(DateTime nowUtc)
    {
        var current = _programs.GetProgramAt(nowUtc);
        var after = current?.EndUtc ?? nowUtc;
        return _programs.GetNextAfter(after);
    }
}
