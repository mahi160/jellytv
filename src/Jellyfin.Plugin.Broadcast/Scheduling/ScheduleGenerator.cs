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
    /// <param name="episodeState">Active Series tracking per block (read once per Generate call \u2014 see remarks on <see cref="Generate"/>).</param>
    /// <param name="random">Random source (injectable for deterministic tests).</param>
    public ScheduleGenerator(MovieHistoryRepository movieHistory, EpisodeStateRepository episodeState, Random? random = null)
    {
        _movieHistory = movieHistory;
        _episodeState = episodeState;
        _random = random ?? new Random();
    }

    /// <summary>
    /// Generates Programs covering [<paramref name="rangeStartUtc"/>, <paramref name="rangeEndUtc"/>).
    /// Does not persist anything \u2014 see <see cref="ProgramRepository.ReplaceAll"/> for that (full wipe-and-rebuild),
    /// and <see cref="ScheduleRegenerationService"/> for how Active Series progress actually gets committed
    /// (never from here: this whole range is in the future, so nothing generated has "really" aired yet).
    /// </summary>
    /// <param name="rangeStartUtc">Start of the range (UTC).</param>
    /// <param name="rangeEndUtc">End of the range (UTC, exclusive).</param>
    /// <param name="timeZone">Timezone the blocks' daily start/end times are expressed in.</param>
    /// <param name="blocks">The configured Programming Blocks.</param>
    /// <param name="poolProvider">Resolves a block's matching candidates (movies, or all episodes of all matching series).
    /// Called at most once per block per Generate call (result is cached) \u2014 without that, a multi-day schedule
    /// re-runs a full library query on every single program slot.</param>
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

        var poolCache = new Dictionary<string, IReadOnlyList<ScheduleCandidate>>();
        IReadOnlyList<ScheduleCandidate> GetPool(ProgrammingBlock b)
        {
            if (!poolCache.TryGetValue(b.Name, out var pool))
            {
                pool = poolProvider(b);
                poolCache[b.Name] = pool;
            }

            return pool;
        }

        // Loaded once instead of one SQLite connection per candidate per pick (see MovieHistoryRepository.GetSummary).
        var tracker = new MovieAiringTracker(_movieHistory.GetSummary());

        // Active Series cursor is tracked purely in-memory here, seeded lazily from the last *committed*
        // state (real playback progress, as of the previous regeneration \u2014 see ScheduleRegenerationService).
        // It is intentionally never written back to the DB from within Generate: every program produced by
        // this method is in the future, so persisting cursor movement here would advance the series by an
        // entire schedule's worth on every regeneration instead of by what viewers actually watched.
        var episodeCursors = new Dictionary<string, ActiveSeriesState?>();
        ActiveSeriesState? GetEpisodeCursor(string blockName)
        {
            if (!episodeCursors.TryGetValue(blockName, out var state))
            {
                state = _episodeState.Get(blockName);
                episodeCursors[blockName] = state;
            }

            return state;
        }

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

            var pool = GetPool(block);
            if (pool.Count == 0)
            {
                current = NextBlockBoundary(block, current, timeZone);
                continue;
            }

            var candidate = block.ContentType == BlockContentType.Movie
                ? PickMovie(block, pool, tracker, current)
                : PickEpisode(block, pool, episodeCursors, GetEpisodeCursor(block.Name));

            var duration = candidate.Duration > TimeSpan.Zero ? candidate.Duration : TimeSpan.FromMinutes(30);
            var start = current;
            var end = start + duration;

            programs.Add(new Data.Program { BlockName = block.Name, ItemId = candidate.ItemId, StartUtc = start, EndUtc = end });

            if (block.ContentType == BlockContentType.Movie)
            {
                tracker.RecordAiring(candidate.ItemId, start);
            }

            current = end;
        }

        return programs;
    }

    private static TimeSpan ParseTimeOfDay(string value) =>
        TimeSpan.ParseExact(value, "hh\\:mm", CultureInfo.InvariantCulture);

    private static bool WindowContains(TimeSpan start, TimeSpan end, TimeSpan t) =>
        start <= end ? t >= start && t < end : t >= start || t < end;

    /// <summary>
    /// Converts a local wall-clock time to UTC, tolerating the DST spring-forward gap (which
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> throws on) by nudging the
    /// time forward past the gap instead of failing the whole regeneration twice a year.
    /// </summary>
    private static DateTime ToUtcSafe(DateTime local, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified))
        {
            // Nonexistent local time (e.g. 02:30 during a spring-forward that skips 02:00-03:00) \u2014
            // walk forward in small steps until we land outside the gap (gaps are at most a few hours).
            do
            {
                unspecified = unspecified.AddMinutes(1);
            }
            while (timeZone.IsInvalidTime(unspecified));
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }

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

        return ToUtcSafe(best!.Value, timeZone);
    }

    private static DateTime NextBlockBoundary(ProgrammingBlock block, DateTime currentUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timeZone);
        var end = local.Date + ParseTimeOfDay(block.EndTime);
        if (end <= local)
        {
            end = end.AddDays(1);
        }

        return ToUtcSafe(end, timeZone);
    }

    private ScheduleCandidate PickMovie(
        ProgrammingBlock block,
        IReadOnlyList<ScheduleCandidate> pool,
        MovieAiringTracker tracker,
        DateTime atUtc)
    {
        var cooldown = TimeSpan.FromDays(block.MovieCooldownDays);
        var eligible = pool.Where(c =>
        {
            var last = tracker.GetLastAired(c.ItemId);
            return last is null || (atUtc - last.Value) >= cooldown;
        }).ToList();

        if (eligible.Count == 0)
        {
            // Pool too small to honor Cooldown \u2014 relax it and take the least-recently-aired item (never wipe a slot).
            return pool.OrderBy(c => tracker.GetLastAired(c.ItemId) ?? DateTime.MinValue).First();
        }

        return SelectByOrdering(eligible, block.Order, block.WeightFactor, tracker);
    }

    private ScheduleCandidate SelectByOrdering(
        IReadOnlyList<ScheduleCandidate> pool,
        OrderingStrategy order,
        WeightFactor weightFactor,
        MovieAiringTracker tracker)
    {
        return order switch
        {
            OrderingStrategy.Sequential => PickSequential(pool, tracker),
            OrderingStrategy.Random => pool[_random.Next(pool.Count)],
            OrderingStrategy.WeightedRandom => PickWeighted(pool, weightFactor, tracker),
            OrderingStrategy.Chronological or OrderingStrategy.OldestFirst => PickByReleaseDate(pool, newestFirst: false),
            OrderingStrategy.NewestFirst => PickByReleaseDate(pool, newestFirst: true),
            // NeverPlayed and LeastPlayed both sort ascending by airings \u2014 items that have never aired (count 0) sort first naturally.
            OrderingStrategy.LeastPlayed or OrderingStrategy.NeverPlayed => pool.OrderBy(c => tracker.GetCount(c.ItemId)).First(),
            _ => pool[0]
        };
    }

    /// <summary>
    /// Sequential order is a fixed, stable sequence through the pool (release date, then item id as a
    /// tiebreak) \u2014 not insertion/library order, which has no meaning here and would look arbitrary to an admin.
    /// </summary>
    private static ScheduleCandidate PickSequential(IReadOnlyList<ScheduleCandidate> pool, MovieAiringTracker tracker)
    {
        var sorted = pool.OrderBy(c => c.ReleaseDate ?? DateTime.MinValue).ThenBy(c => c.ItemId).ToList();
        ScheduleCandidate? lastAiredItem = null;
        var lastAiredTime = DateTime.MinValue;

        foreach (var c in sorted)
        {
            var t = tracker.GetLastAired(c.ItemId);
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

    private ScheduleCandidate PickWeighted(IReadOnlyList<ScheduleCandidate> pool, WeightFactor factor, MovieAiringTracker? tracker = null)
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
            WeightFactor.PlayCount => 1.0 / ((tracker?.GetCount(c.ItemId) ?? 0) + 1),
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

    private ScheduleCandidate PickEpisode(
        ProgrammingBlock block,
        IReadOnlyList<ScheduleCandidate> pool,
        Dictionary<string, ActiveSeriesState?> episodeCursors,
        ActiveSeriesState? state)
    {
        if (state is null)
        {
            return StartNewSeries(block, pool, episodeCursors, excludeSeriesId: null, forceRandom: false);
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
            SetActiveEpisode(block.Name, episodeCursors, next);
            return next;
        }

        // Active Series finished \u2014 apply the block's SeriesEndBehavior.
        if (block.SeriesEndBehavior == SeriesEndBehavior.Restart && seriesEpisodes.Count > 0)
        {
            var first = seriesEpisodes[0];
            SetActiveEpisode(block.Name, episodeCursors, first);
            return first;
        }

        return StartNewSeries(
            block,
            pool,
            episodeCursors,
            excludeSeriesId: state.SeriesId,
            forceRandom: block.SeriesEndBehavior == SeriesEndBehavior.Random);
    }

    private ScheduleCandidate StartNewSeries(
        ProgrammingBlock block,
        IReadOnlyList<ScheduleCandidate> pool,
        Dictionary<string, ActiveSeriesState?> episodeCursors,
        Guid? excludeSeriesId,
        bool forceRandom)
    {
        var seriesGroups = pool.GroupBy(c => c.SeriesId!.Value).ToList();
        var candidateGroups = excludeSeriesId.HasValue
            ? seriesGroups.Where(g => g.Key != excludeSeriesId.Value).ToList()
            : seriesGroups;

        if (candidateGroups.Count == 0)
        {
            // Only one series matches the block's filters \u2014 reuse it (Restart in all but name).
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

        SetActiveEpisode(block.Name, episodeCursors, firstEpisode);
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

    private static void SetActiveEpisode(string blockName, Dictionary<string, ActiveSeriesState?> episodeCursors, ScheduleCandidate episode) =>
        episodeCursors[blockName] = new ActiveSeriesState
        {
            BlockName = blockName,
            SeriesId = episode.SeriesId!.Value,
            SeasonNumber = episode.SeasonNumber!.Value,
            EpisodeNumber = episode.EpisodeNumber!.Value
        };

    /// <summary>
    /// In-memory movie airing history for the duration of a single Generate call: seeded once from
    /// <see cref="MovieHistoryRepository.GetSummary"/> instead of a query per candidate, and updated as
    /// programs are scheduled so Cooldown/LeastPlayed/PlayCount see items picked earlier in the same run
    /// without a DB round-trip.
    /// </summary>
    private sealed class MovieAiringTracker
    {
        private readonly Dictionary<Guid, DateTime> _lastAired = new();
        private readonly Dictionary<Guid, int> _counts = new();

        public MovieAiringTracker(IReadOnlyDictionary<Guid, (DateTime? LastAired, int Count)> summary)
        {
            foreach (var (id, stats) in summary)
            {
                if (stats.LastAired.HasValue)
                {
                    _lastAired[id] = stats.LastAired.Value;
                }

                _counts[id] = stats.Count;
            }
        }

        public DateTime? GetLastAired(Guid itemId) => _lastAired.TryGetValue(itemId, out var t) ? t : null;

        public int GetCount(Guid itemId) => _counts.TryGetValue(itemId, out var c) ? c : 0;

        public void RecordAiring(Guid itemId, DateTime whenUtc)
        {
            if (!_lastAired.TryGetValue(itemId, out var existing) || whenUtc > existing)
            {
                _lastAired[itemId] = whenUtc;
            }

            _counts[itemId] = GetCount(itemId) + 1;
        }
    }
}
