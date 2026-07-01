using System.Collections.Generic;
using Jellyfin.Plugin.Broadcast.Configuration;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Resolves a Programming Block's matching media pool. Extracted so <see cref="ScheduleRegenerationService"/>
/// can be unit tested without a live Jellyfin library (<see cref="LibraryPoolResolver"/> is the real implementation).
/// </summary>
public interface IMediaPoolResolver
{
    /// <summary>
    /// Gets the pool of candidates matching a Programming Block's filters.
    /// </summary>
    /// <param name="block">The Programming Block.</param>
    /// <returns>The matching candidates.</returns>
    IReadOnlyList<ScheduleCandidate> GetMatchingPool(ProgrammingBlock block);
}
