using System;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Data;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Orchestrates a full schedule regeneration: resolves each block's pool, runs the generator,
/// and persists the result (Programs replaced wholesale, movie airings recorded for future Cooldown checks).
/// </summary>
public class ScheduleRegenerationService
{
    private readonly LibraryPoolResolver _poolResolver;
    private readonly ScheduleGenerator _generator;
    private readonly ProgramRepository _programs;
    private readonly MovieHistoryRepository _movieHistory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleRegenerationService"/> class.
    /// </summary>
    /// <param name="poolResolver">Resolves each block's matching library items.</param>
    /// <param name="generator">Fills a time range with Programs.</param>
    /// <param name="programs">Persists the generated Programs.</param>
    /// <param name="movieHistory">Records movie airings for Cooldown.</param>
    public ScheduleRegenerationService(
        LibraryPoolResolver poolResolver,
        ScheduleGenerator generator,
        ProgramRepository programs,
        MovieHistoryRepository movieHistory)
    {
        _poolResolver = poolResolver;
        _generator = generator;
        _programs = programs;
        _movieHistory = movieHistory;
    }

    /// <summary>
    /// Regenerates the schedule for the configured duration, starting now.
    /// </summary>
    /// <param name="config">The plugin configuration (blocks, timezone, duration).</param>
    /// <param name="nowUtc">The current UTC time (injectable for tests).</param>
    public void Regenerate(PluginConfiguration config, DateTime nowUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
        var rangeEnd = nowUtc.AddDays(config.ScheduleDurationDays);

        var generated = _generator.Generate(
            nowUtc,
            rangeEnd,
            timeZone,
            config.Blocks,
            block => BaseItemCandidateAdapter.ToCandidates(_poolResolver.GetMatchingPool(block)));

        _programs.ReplaceAll(generated);

        var movieBlockNames = config.Blocks
            .Where(b => b.ContentType == BlockContentType.Movie)
            .Select(b => b.Name)
            .ToHashSet();

        foreach (var program in generated.Where(p => movieBlockNames.Contains(p.BlockName)))
        {
            _movieHistory.RecordAired(program.ItemId, program.StartUtc);
        }
    }
}
