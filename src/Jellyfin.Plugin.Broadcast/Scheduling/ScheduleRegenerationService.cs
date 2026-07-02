using System;
using System.Collections.Generic;
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
    private readonly EpisodeStateRepository _episodeState;
    private readonly ILogger<ScheduleRegenerationService> _logger;
    private readonly SemaphoreSlim _regenerationLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleRegenerationService"/> class.
    /// </summary>
    /// <param name="poolResolver">Resolves each block's matching library items.</param>
    /// <param name="generator">Fills a time range with Programs.</param>
    /// <param name="programs">Persists the generated Programs.</param>
    /// <param name="movieHistory">Records movie airings for Cooldown.</param>
    /// <param name="episodeState">Reads/commits each block's Active Series.</param>
    /// <param name="logger">Logger.</param>
    public ScheduleRegenerationService(
        IMediaPoolResolver poolResolver,
        ScheduleGenerator generator,
        ProgramRepository programs,
        MovieHistoryRepository movieHistory,
        EpisodeStateRepository episodeState,
        ILogger<ScheduleRegenerationService> logger)
    {
        _poolResolver = poolResolver;
        _generator = generator;
        _programs = programs;
        _movieHistory = movieHistory;
        _episodeState = episodeState;
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

        // The schedule about to be replaced is the only place "what actually aired since the last
        // regeneration" lives — commit real Active Series progress from it before wiping (never from the
        // freshly generated schedule below, which is entirely in the future).
        CommitElapsedEpisodeState(validBlocks, nowUtc);

        // Drop speculative future airings the previous regeneration recorded for movies that haven't
        // actually aired (they're being superseded by the schedule generated below) — without this,
        // Cooldown/LeastPlayed see phantom future history and airing counts inflate by a full schedule's
        // worth every time the schedule regenerates.
        _movieHistory.PruneAtOrAfter(nowUtc);

        // Preserve whatever's currently airing instead of cutting it off mid-program on every regeneration
        // (library changes, config saves, and the daily task all trigger this).
        var current = _programs.GetProgramAt(nowUtc);
        var rangeStart = current?.EndUtc ?? nowUtc;

        var generated = _generator.Generate(
            rangeStart,
            rangeEnd,
            timeZone,
            validBlocks,
            _poolResolver.GetMatchingPool);

        var toPersist = current is null ? generated : Prepend(current, generated);
        _programs.ReplaceAll(toPersist);

        var movieBlockNames = validBlocks
            .Where(b => b.ContentType == BlockContentType.Movie)
            .Select(b => b.Name)
            .ToHashSet();

        // Single transaction instead of one connection per airing (ADR: this batch doesn't share a
        // transaction with the Programs replace above — a crash between the two can't corrupt either
        // table, but could leave history one regeneration behind Programs; regeneration is idempotent
        // and re-running fixes that, so it isn't worth a cross-repository transaction). Excludes the
        // preserved current program — it's already recorded from when the previous regeneration generated it.
        _movieHistory.RecordAiredBatch(generated
            .Where(p => movieBlockNames.Contains(p.BlockName))
            .Select(p => (p.ItemId, p.StartUtc)));

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

    /// <summary>
    /// Commits each Episode block's Active Series as of "now", derived from the schedule about to be
    /// replaced (i.e. from what actually aired since the previous regeneration) rather than from wherever
    /// the newly generated future schedule happens to land.
    /// </summary>
    private void CommitElapsedEpisodeState(IReadOnlyList<ProgrammingBlock> blocks, DateTime nowUtc)
    {
        foreach (var block in blocks.Where(b => b.ContentType == BlockContentType.Episode))
        {
            var lastAired = _programs.GetLastProgramForBlockAtOrBefore(block.Name, nowUtc);
            if (lastAired is null)
            {
                continue;
            }

            var pool = _poolResolver.GetMatchingPool(block);
            var candidate = pool.FirstOrDefault(c => c.ItemId == lastAired.ItemId);
            if (candidate?.SeriesId is null || candidate.SeasonNumber is null || candidate.EpisodeNumber is null)
            {
                // Item no longer matches the block's filters (removed/re-tagged) — nothing safe to commit;
                // the next regeneration's StartNewSeries logic will pick a fresh series instead.
                continue;
            }

            _episodeState.Set(new ActiveSeriesState
            {
                BlockName = block.Name,
                SeriesId = candidate.SeriesId.Value,
                SeasonNumber = candidate.SeasonNumber.Value,
                EpisodeNumber = candidate.EpisodeNumber.Value
            });
        }
    }

    private static List<Data.Program> Prepend(Data.Program item, IReadOnlyList<Data.Program> rest)
    {
        var list = new List<Data.Program>(rest.Count + 1) { item };
        list.AddRange(rest);
        return list;
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
