using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Configuration;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Validates Programming Blocks before they reach the generator. A single admin typo (bad time
/// format, inverted year range, duplicate name) must not take down the entire schedule regeneration.
/// </summary>
public static class BlockValidator
{
    /// <summary>
    /// Splits blocks into valid ones (safe to schedule) and invalid ones (with a reason each).
    /// </summary>
    /// <param name="blocks">The configured Programming Blocks.</param>
    /// <returns>The valid blocks, and the rejected ones with reasons.</returns>
    public static (IReadOnlyList<ProgrammingBlock> Valid, IReadOnlyList<(ProgrammingBlock Block, string Reason)> Invalid) Validate(
        IReadOnlyList<ProgrammingBlock> blocks)
    {
        var valid = new List<ProgrammingBlock>();
        var invalid = new List<(ProgrammingBlock, string)>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            var reason = GetInvalidReason(block, seenNames) ?? GetOverlapReason(block, valid);
            if (reason is null)
            {
                seenNames.Add(block.Name);
                valid.Add(block);
            }
            else
            {
                invalid.Add((block, reason));
            }
        }

        return (valid, invalid);
    }

    private static string? GetInvalidReason(ProgrammingBlock block, HashSet<string> seenNames)
    {
        if (string.IsNullOrWhiteSpace(block.Name))
        {
            return "Block name is required.";
        }

        if (seenNames.Contains(block.Name))
        {
            return $"Duplicate block name '{block.Name}' \u2014 names must be unique (used as the key for Active Series/history tracking).";
        }

        if (!IsValidTimeOfDay(block.StartTime))
        {
            return $"StartTime '{block.StartTime}' is not a valid HH:mm time.";
        }

        if (!IsValidTimeOfDay(block.EndTime))
        {
            return $"EndTime '{block.EndTime}' is not a valid HH:mm time.";
        }

        if (block.MinYear.HasValue && block.MaxYear.HasValue && block.MinYear > block.MaxYear)
        {
            return $"MinYear ({block.MinYear}) is after MaxYear ({block.MaxYear}).";
        }

        if (block.MovieCooldownDays < 0)
        {
            return "MovieCooldownDays cannot be negative.";
        }

        return null;
    }

    private static bool IsValidTimeOfDay(string value) =>
        TimeSpan.TryParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// Two blocks whose daily windows overlap would make <c>ScheduleGenerator.FindActiveBlock</c> silently
    /// pick whichever comes first in the list — the later one would never get a single minute scheduled.
    /// Rejecting it here surfaces that as a config error instead of a mysteriously empty block.
    /// </summary>
    private static string? GetOverlapReason(ProgrammingBlock block, IReadOnlyList<ProgrammingBlock> alreadyValid)
    {
        var start = TimeSpan.ParseExact(block.StartTime, "hh\\:mm", CultureInfo.InvariantCulture);
        var end = TimeSpan.ParseExact(block.EndTime, "hh\\:mm", CultureInfo.InvariantCulture);

        foreach (var other in alreadyValid)
        {
            var otherStart = TimeSpan.ParseExact(other.StartTime, "hh\\:mm", CultureInfo.InvariantCulture);
            var otherEnd = TimeSpan.ParseExact(other.EndTime, "hh\\:mm", CultureInfo.InvariantCulture);

            if (WindowsOverlap(start, end, otherStart, otherEnd))
            {
                return $"'{block.Name}' ({block.StartTime}-{block.EndTime}) overlaps '{other.Name}' ({other.StartTime}-{other.EndTime}) — the earlier block always wins, so the later one would never air.";
            }
        }

        return null;
    }

    // Two (possibly midnight-wrapping) daily windows overlap iff either one's start falls strictly
    // inside the other — same containment rule ScheduleGenerator.WindowContains uses for point lookups.
    private static bool WindowsOverlap(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd) =>
        Contains(aStart, aEnd, bStart) || Contains(bStart, bEnd, aStart);

    private static bool Contains(TimeSpan start, TimeSpan end, TimeSpan t) =>
        start <= end ? t >= start && t < end : t >= start || t < end;
}
