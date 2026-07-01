using System;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Data;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Orchestrates a full schedule regeneration: resolves each block's pool, runs the generator,
/// and persists the result (Programs replaced wholesale, movie airings recorded for future Cooldown checks).
/// </summary>
public class ScheduleRegenerationService
{
    // Retention floor for MovieHistory: even with a 0-day Cooldown configured everywhere, keep at least
    // this much history (LeastPlayed/NeverPlayed ordering and Sequential's cursor both read from it).
    private static readonly TimeSpan MinimumHistoryRetention = TimeSpan.FromDays(90);

    private readonly IMediaPoolResolver _poolResolver;
    private readonly ScheduleGenerator _generator;
    private readonly ProgramRepository _programs;
    private readonly MovieHistoryRepository _movieHistory;
    private readonly ILogger<ScheduleRegenerationService> _logger;
    private readonly SemaphoreSlim _regenerationLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleRegenerationService"/> class.
    /// </summary>
    /// <param name="poolResolver">Resolves each block's matching library items.</param>
    /// <param name="generator">Fills a time range with Programs.</param>
    /// <param name="programs">Persists the generated Programs.</param>
    /// <param name="movieHistory">Records movie airings for Cooldown.</param>
    /// <param name="logger">Logger.</param>
    public ScheduleRegenerationService(
        IMediaPoolResolver poolResolver,
        ScheduleGenerator generator,
        ProgramRepository programs,
        MovieHistoryRepository movieHistory,
        ILogger<ScheduleRegenerationService> logger)
    {
        _poolResolver = poolResolver;
        _generator = generator;
        _programs = programs;
        _movieHistory = movieHistory;
        _logger = logger;
    }

    /// <summary>
    /// Regenerates the schedule for the configured duration, starting now. Manual clicks, the daily
    /// scheduled task, and library/settings-change triggers can all call this independently — a
    /// regeneration already in progress causes this call to skip immediately rather than run concurrently.
    /// </summary>
    /// <param name="config">The plugin configuration (blocks, timezone, duration).</param>
    /// <param name="nowUtc">The current UTC time (injectable for tests).</param>
    /// <returns>True if a regeneration ran; false if one was already in progress and this call was skipped.</returns>
    public bool Regenerate(PluginConfiguration config, DateTime nowUtc)
    {
        if (!_regenerationLock.Wait(0))
        {
            _logger.LogInformation("Schedule regeneration already in progress — skipping this trigger.");
            return false;
        }

        try
        {
            RunRegeneration(config, nowUtc);
            return true;
        }
        finally
        {
            _regenerationLock.Release();
        }
    }

    private void RunRegeneration(PluginConfiguration config, DateTime nowUtc)
    {
        var started = DateTime.UtcNow;

        var (validBlocks, invalidBlocks) = BlockValidator.Validate(config.Blocks);
        foreach (var (block, reason) in invalidBlocks)
        {
            _logger.LogWarning("Skipping Programming Block '{BlockName}': {Reason}", block.Name, reason);
        }

        if (validBlocks.Count == 0)
        {
            _logger.LogWarning("No valid Programming Blocks configured — nothing to schedule.");
            return;
        }

        var timeZone = ResolveTimeZone(config.TimeZone);
        var rangeEnd = nowUtc.AddDays(Math.Max(config.ScheduleDurationDays, 1));

        var generated = _generator.Generate(
            nowUtc,
            rangeEnd,
            timeZone,
            validBlocks,
            _poolResolver.GetMatchingPool);

        _programs.ReplaceAll(generated);

        var movieBlockNames = validBlocks
            .Where(b => b.ContentType == BlockContentType.Movie)
            .Select(b => b.Name)
            .ToHashSet();

        foreach (var program in generated.Where(p => movieBlockNames.Contains(p.BlockName)))
        {
            _movieHistory.RecordAired(program.ItemId, program.StartUtc);
        }

        var maxCooldown = validBlocks.Count > 0 ? validBlocks.Max(b => b.MovieCooldownDays) : 0;
        var retention = TimeSpan.FromDays(maxCooldown * 2);
        var cutoff = nowUtc - (retention > MinimumHistoryRetention ? retention : MinimumHistoryRetention);
        var pruned = _movieHistory.PruneOlderThan(cutoff);

        _logger.LogInformation(
            "Regenerated schedule: {ProgramCount} Programs across {BlockCount} blocks in {ElapsedMs}ms ({PrunedCount} old history rows pruned).",
            generated.Count,
            validBlocks.Count,
            (DateTime.UtcNow - started).TotalMilliseconds,
            pruned);
    }

    private TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning(ex, "Invalid TimeZone '{TimeZoneId}' in configuration — falling back to UTC.", timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }
}
