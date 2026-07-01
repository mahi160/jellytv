using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Data;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Fills a time range with Programs from the configured Programming Blocks, honoring each block's
/// ordering strategy, Cooldown (movies), and Active Series continuation (episodes).
/// </summary>
public class ScheduleGenerator
{
    private readonly MovieHistoryRepository _movieHistory;
    private readonly EpisodeStateRepository _episodeState;
    private readonly Random _random;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleGenerator"/> class.
    /// </summary>
    /// <param name="movieHistory">Cooldown / play-count history for movies.</param>
    /// <param name="episodeState">Active Series tracking per block.</param>
    /// <param name="random">Random source (injectable for deterministic tests).</param>
    public ScheduleGenerator(MovieHistoryRepository movieHistory, EpisodeStateRepository episodeState, Random? random = null)
    {
        _movieHistory = movieHistory;
        _episodeState = episodeState;
        _random = random ?? new Random();
    }

    /// <summary>
    /// Generates Programs covering [<paramref name="rangeStartUtc"/>, <paramref name="rangeEndUtc"/>).
    /// Does not persist anything — see <see cref="ProgramRepository.ReplaceAll"/> for that (full wipe-and-rebuild).
    /// </summary>
    /// <param name="rangeStartUtc">Start of the range (UTC).</param>
    /// <param name="rangeEndUtc">End of the range (UTC, exclusive).</param>
    /// <param name="timeZone">Timezone the blocks' daily start/end times are expressed in.</param>
    /// <param name="blocks">The configured Programming Blocks.</param>
    /// <param name="poolProvider">Resolves a block's matching candidates (movies, or all episodes of all matching series).</param>
    /// <returns>The generated Programs, in chronological order.</returns>
    public IReadOnlyList<Data.Program> Generate(
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        TimeZoneInfo timeZone,
        IReadOnlyList<ProgrammingBlock> blocks,
        Func<ProgrammingBlock, IReadOnlyList<ScheduleCandidate>> poolProvider)
    {
        var programs = new List<Data.Program>();
        if (blocks.Count == 0)
        {
            return programs;
        }

        // Tracks movies scheduled within this run, so cooldown/sequential logic sees them
        // immediately without a DB round-trip (they aren't persisted until the whole run completes).
        var recentlyScheduled = new Dictionary<Guid, DateTime>();
        var current = rangeStartUtc;
        var guard = 0;
        const int maxIterations = 200_000; // ponytail: safety valve against config causing an infinite loop, not a real limit

        while (current < rangeEndUtc && guard++ < maxIterations)
        {
            var block = FindActiveBlock(blocks, current, timeZone);
            if (block is null)
            {
                current = FindNextBlockStart(blocks, current, timeZone);
                continue;
            }

            var pool = poolProvider(block);
            if (pool.Count == 0)
            {
                current = NextBlockBoundary(block, current, timeZone);
                continue;
            }

            var candidate = block.ContentType == BlockContentType.Movie
                ? PickMovie(block, pool, recentlyScheduled, current)
                : PickEpisode(block, pool);

            var duration = candidate.Duration > TimeSpan.Zero ? candidate.Duration : TimeSpan.FromMinutes(30);
            var start = current;
            var end = start + duration;

            programs.Add(new Data.Program { BlockName = block.Name, ItemId = candidate.ItemId, StartUtc = start, EndUtc = end });

            if (block.ContentType == BlockContentType.Movie)
            {
                recentlyScheduled[candidate.ItemId] = start;
            }

            current = end;
        }

        return programs;
    }

    private static TimeSpan ParseTimeOfDay(string value) =>
        TimeSpan.ParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture);

    private static bool WindowContains(TimeSpan start, TimeSpan end, TimeSpan t) =>
        start <= end ? t >= start && t < end : t >= start || t < end;

    private static ProgrammingBlock? FindActiveBlock(IReadOnlyList<ProgrammingBlock> blocks, DateTime currentUtc, TimeZoneInfo timeZone)
    {
        var localTod = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timeZone).TimeOfDay;
        return blocks.FirstOrDefault(b => WindowContains(ParseTimeOfDay(b.StartTime), ParseTimeOfDay(b.EndTime), localTod));
    }

    private static DateTime FindNextBlockStart(IReadOnlyList<ProgrammingBlock> blocks, DateTime currentUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timeZone);
        DateTime? best = null;
        foreach (var block in blocks)
        {
            var candidate = local.Date + ParseTimeOfDay(block.StartTime);
            if (candidate <= local)
            {
                candidate = candidate.AddDays(1);
            }

            if (best is null || candidate < best)
            {
                best = candidate;
            }
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(best!.Value, DateTimeKind.Unspecified), timeZone);
    }

    private static DateTime NextBlockBoundary(ProgrammingBlock block, DateTime currentUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timeZone);
        var end = local.Date + ParseTimeOfDay(block.EndTime);
        if (end <= local)
        {
            end = end.AddDays(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), timeZone);
    }

    private DateTime? GetEffectiveLastAired(Guid itemId, IDictionary<Guid, DateTime> recentlyScheduled) =>
        recentlyScheduled.TryGetValue(itemId, out var t) ? t : _movieHistory.GetLastAired(itemId);

    private ScheduleCandidate PickMovie(
        ProgrammingBlock block,
        IReadOnlyList<ScheduleCandidate> pool,
        Dictionary<Guid, DateTime> recentlyScheduled,
        DateTime atUtc)
    {
        var cooldown = TimeSpan.FromDays(block.MovieCooldownDays);
        var eligible = pool.Where(c =>
        {
            var last = GetEffectiveLastAired(c.ItemId, recentlyScheduled);
            return last is null || (atUtc - last.Value) >= cooldown;
        }).ToList();

        if (eligible.Count == 0)
        {
            // Pool too small to honor Cooldown — relax it and take the least-recently-aired item (never wipe a slot).
            return pool.OrderBy(c => GetEffectiveLastAired(c.ItemId, recentlyScheduled) ?? DateTime.MinValue).First();
        }

        return SelectByOrdering(eligible, block.Order, block.WeightFactor, recentlyScheduled);
    }

    private ScheduleCandidate SelectByOrdering(
        IReadOnlyList<ScheduleCandidate> pool,
        OrderingStrategy order,
        WeightFactor weightFactor,
        IDictionary<Guid, DateTime> recentlyScheduled)
    {
        return order switch
        {
            OrderingStrategy.Sequential => PickSequential(pool, recentlyScheduled),
            OrderingStrategy.Random => pool[_random.Next(pool.Count)],
            OrderingStrategy.WeightedRandom => PickWeighted(pool, weightFactor),
            OrderingStrategy.Chronological or OrderingStrategy.OldestFirst => PickByReleaseDate(pool, newestFirst: false),
            OrderingStrategy.NewestFirst => PickByReleaseDate(pool, newestFirst: true),
            // NeverPlayed and LeastPlayed both sort ascending by airings — items that have never aired (count 0) sort first naturally.
            OrderingStrategy.LeastPlayed or OrderingStrategy.NeverPlayed => pool.OrderBy(c => _movieHistory.GetAiredCount(c.ItemId)).First(),
            _ => pool[0]
        };
    }

    private ScheduleCandidate PickSequential(IReadOnlyList<ScheduleCandidate> pool, IDictionary<Guid, DateTime> recentlyScheduled)
    {
        var sorted = pool.OrderBy(c => c.ItemId).ToList();
        ScheduleCandidate? lastAiredItem = null;
        var lastAiredTime = DateTime.MinValue;

        foreach (var c in sorted)
        {
            var t = GetEffectiveLastAired(c.ItemId, recentlyScheduled);
            if (t.HasValue && t.Value > lastAiredTime)
            {
                lastAiredTime = t.Value;
                lastAiredItem = c;
            }
        }

        if (lastAiredItem is null)
        {
            return sorted[0];
        }

        var idx = sorted.IndexOf(lastAiredItem);
        return sorted[(idx + 1) % sorted.Count];
    }

    private static ScheduleCandidate PickByReleaseDate(IReadOnlyList<ScheduleCandidate> pool, bool newestFirst) =>
        newestFirst
            ? pool.OrderByDescending(c => c.ReleaseDate ?? DateTime.MinValue).First()
            : pool.OrderBy(c => c.ReleaseDate ?? DateTime.MinValue).First();

    private ScheduleCandidate PickWeighted(IReadOnlyList<ScheduleCandidate> pool, WeightFactor factor)
    {
        var minTicks = 0L;
        var rangeTicks = 1L;
        if (factor == WeightFactor.Recency)
        {
            var ticks = pool.Select(c => (c.ReleaseDate ?? DateTime.MinValue).Ticks).ToList();
            minTicks = ticks.Min();
            rangeTicks = Math.Max(ticks.Max() - minTicks, 1);
        }

        var weights = pool.Select(c => factor switch
        {
            WeightFactor.Rating => Math.Max(c.Rating ?? 0.1, 0.1),
            WeightFactor.Recency => Math.Max(((c.ReleaseDate ?? DateTime.MinValue).Ticks - minTicks) / (double)rangeTicks, 0.01),
            WeightFactor.PlayCount => 1.0 / (_movieHistory.GetAiredCount(c.ItemId) + 1),
            _ => 1.0
        }).ToList();

        var total = weights.Sum();
        var roll = _random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < pool.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                return pool[i];
            }
        }

        return pool[^1];
    }

    private ScheduleCandidate PickEpisode(ProgrammingBlock block, IReadOnlyList<ScheduleCandidate> pool)
    {
        var state = _episodeState.Get(block.Name);
        if (state is null)
        {
            return StartNewSeries(block, pool, excludeSeriesId: null, forceRandom: false);
        }

        var seriesEpisodes = pool.Where(c => c.SeriesId == state.SeriesId)
            .OrderBy(c => c.SeasonNumber)
            .ThenBy(c => c.EpisodeNumber)
            .ToList();

        var next = seriesEpisodes.FirstOrDefault(c =>
            c.SeasonNumber > state.SeasonNumber ||
            (c.SeasonNumber == state.SeasonNumber && c.EpisodeNumber > state.EpisodeNumber));

        if (next is not null)
        {
            SetActiveEpisode(block.Name, next);
            return next;
        }

        // Active Series finished — apply the block's SeriesEndBehavior.
        if (block.SeriesEndBehavior == SeriesEndBehavior.Restart && seriesEpisodes.Count > 0)
        {
            var first = seriesEpisodes[0];
            SetActiveEpisode(block.Name, first);
            return first;
        }

        return StartNewSeries(
            block,
            pool,
            excludeSeriesId: state.SeriesId,
            forceRandom: block.SeriesEndBehavior == SeriesEndBehavior.Random);
    }

    private ScheduleCandidate StartNewSeries(ProgrammingBlock block, IReadOnlyList<ScheduleCandidate> pool, Guid? excludeSeriesId, bool forceRandom)
    {
        var seriesGroups = pool.GroupBy(c => c.SeriesId!.Value).ToList();
        var candidateGroups = excludeSeriesId.HasValue
            ? seriesGroups.Where(g => g.Key != excludeSeriesId.Value).ToList()
            : seriesGroups;

        if (candidateGroups.Count == 0)
        {
            // Only one series matches the block's filters — reuse it (Restart in all but name).
            candidateGroups = seriesGroups;
        }

        var representatives = candidateGroups
            .Select(g => g.OrderBy(c => c.SeasonNumber).ThenBy(c => c.EpisodeNumber).First())
            .ToList();

        var chosen = PickNextSeriesRepresentative(representatives, block, excludeSeriesId, forceRandom);

        var firstEpisode = pool
            .Where(c => c.SeriesId == chosen.SeriesId)
            .OrderBy(c => c.SeasonNumber)
            .ThenBy(c => c.EpisodeNumber)
            .First();

        SetActiveEpisode(block.Name, firstEpisode);
        return firstEpisode;
    }

    private ScheduleCandidate PickNextSeriesRepresentative(
        IReadOnlyList<ScheduleCandidate> representatives,
        ProgrammingBlock block,
        Guid? excludeSeriesId,
        bool forceRandom)
    {
        if (forceRandom || representatives.Count == 1)
        {
            return representatives[_random.Next(representatives.Count)];
        }

        switch (block.Order)
        {
            case OrderingStrategy.Sequential:
                var sorted = representatives.OrderBy(c => c.SeriesId).ToList();
                if (excludeSeriesId is null)
                {
                    return sorted[0];
                }

                var idx = sorted.FindIndex(c => c.SeriesId == excludeSeriesId);
                return idx < 0 ? sorted[0] : sorted[(idx + 1) % sorted.Count];
            case OrderingStrategy.Chronological:
            case OrderingStrategy.OldestFirst:
                return PickByReleaseDate(representatives, newestFirst: false);
            case OrderingStrategy.NewestFirst:
                return PickByReleaseDate(representatives, newestFirst: true);
            case OrderingStrategy.WeightedRandom when block.WeightFactor == WeightFactor.Rating:
                return PickWeighted(representatives, WeightFactor.Rating);
            default:
                // Random, WeightedRandom (non-rating factors), LeastPlayed, NeverPlayed: play-count concepts
                // don't map cleanly onto "which series to start next", so fall back to a uniform random pick.
                return representatives[_random.Next(representatives.Count)];
        }
    }

    private void SetActiveEpisode(string blockName, ScheduleCandidate episode) =>
        _episodeState.Set(new ActiveSeriesState
        {
            BlockName = blockName,
            SeriesId = episode.SeriesId!.Value,
            SeasonNumber = episode.SeasonNumber!.Value,
            EpisodeNumber = episode.EpisodeNumber!.Value
        });
}
