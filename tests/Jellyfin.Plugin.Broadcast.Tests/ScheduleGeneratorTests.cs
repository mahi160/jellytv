using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Scheduling;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class ScheduleGeneratorTests : IDisposable
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTime Day1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _tempDir;
    private readonly BroadcastDbContext _db;
    private readonly MovieHistoryRepository _movieHistory;
    private readonly EpisodeStateRepository _episodeState;

    public ScheduleGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "broadcast-schedgen-" + Guid.NewGuid());
        _db = new BroadcastDbContext(_tempDir);
        _movieHistory = new MovieHistoryRepository(_db);
        _episodeState = new EpisodeStateRepository(_db);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    // StartTime == EndTime means the full 24 hours (see DailyWindow).
    private static ProgrammingBlock MovieBlock(string name = "Prime Time", string start = "00:00", string end = "00:00", int cooldownDays = 30, OrderingStrategy order = OrderingStrategy.Sequential) =>
        new() { Name = name, StartTime = start, EndTime = end, ContentType = BlockContentType.Movie, MovieCooldownDays = cooldownDays, Order = order };

    [Fact]
    public void Generate_FillsWindowSequentially_UsingItemDurations()
    {
        var block = MovieBlock();
        var pool = new[]
        {
            new ScheduleCandidate { ItemId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Duration = TimeSpan.FromHours(1) },
            new ScheduleCandidate { ItemId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Duration = TimeSpan.FromHours(2) }
        };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(Day1, Day1.AddHours(3), Utc, new[] { block }, _ => pool);

        Assert.Equal(2, programs.Count);
        Assert.Equal(pool[0].ItemId, programs[0].ItemId);
        Assert.Equal(Day1, programs[0].StartUtc);
        Assert.Equal(Day1.AddHours(1), programs[0].EndUtc);
        Assert.Equal(pool[1].ItemId, programs[1].ItemId);
        Assert.Equal(Day1.AddHours(1), programs[1].StartUtc);
    }

    [Fact]
    public void Sequential_CyclesThroughPoolInOrder()
    {
        var block = MovieBlock(order: OrderingStrategy.Sequential, cooldownDays: 0);
        var a = new ScheduleCandidate { ItemId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Duration = TimeSpan.FromHours(1) };
        var b = new ScheduleCandidate { ItemId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Duration = TimeSpan.FromHours(1) };
        var pool = new[] { a, b };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(Day1, Day1.AddHours(4), Utc, new[] { block }, _ => pool);

        Assert.Equal(new[] { a.ItemId, b.ItemId, a.ItemId, b.ItemId }, programs.Select(p => p.ItemId));
    }

    [Fact]
    public void Cooldown_PreventsImmediateRepeat_WhenPoolIsLargeEnough()
    {
        var block = MovieBlock(order: OrderingStrategy.Sequential, cooldownDays: 30);
        var a = new ScheduleCandidate { ItemId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Duration = TimeSpan.FromHours(1) };
        var b = new ScheduleCandidate { ItemId = Guid.Parse("00000000-0000-0000-0000-000000000002"), Duration = TimeSpan.FromHours(1) };
        var pool = new[] { a, b };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(Day1, Day1.AddHours(4), Utc, new[] { block }, _ => pool);

        // Never two consecutive airings of the same item while both are within cooldown.
        for (var i = 1; i < programs.Count; i++)
        {
            Assert.NotEqual(programs[i - 1].ItemId, programs[i].ItemId);
        }
    }

    [Fact]
    public void Cooldown_RelaxesToLeastRecentlyAired_WhenPoolTooSmall()
    {
        var block = MovieBlock(cooldownDays: 30);
        var only = new ScheduleCandidate { ItemId = Guid.NewGuid(), Duration = TimeSpan.FromHours(1) };
        var pool = new[] { only };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        // With only one movie in the pool, cooldown can never be honored on the second slot — must still fill it.
        var programs = generator.Generate(Day1, Day1.AddHours(2), Utc, new[] { block }, _ => pool);

        Assert.Equal(2, programs.Count);
        Assert.All(programs, p => Assert.Equal(only.ItemId, p.ItemId));
    }

    [Fact]
    public void EmptyPool_SkipsPastBlockWithoutInfiniteLoop()
    {
        var blockA = new ProgrammingBlock { Name = "Empty", StartTime = "00:00", EndTime = "12:00", ContentType = BlockContentType.Movie };
        var blockB = MovieBlock(name: "Filled", start: "12:00", end: "00:00");
        var filler = new ScheduleCandidate { ItemId = Guid.NewGuid(), Duration = TimeSpan.FromHours(1) };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(
            Day1,
            Day1.AddHours(13),
            Utc,
            new[] { blockA, blockB },
            block => block.Name == "Empty" ? Array.Empty<ScheduleCandidate>() : new[] { filler });

        Assert.All(programs, p => Assert.Equal("Filled", p.BlockName));
        Assert.True(programs.Count > 0);
        Assert.True(programs[0].StartUtc >= Day1.AddHours(12));
    }

    [Fact]
    public void Episodes_AdvanceToNextEpisode_ThenRestartOnFinalEpisode()
    {
        var series = Guid.NewGuid();
        var block = new ProgrammingBlock
        {
            Name = "Sitcom Hour",
            StartTime = "00:00",
            EndTime = "23:59",
            ContentType = BlockContentType.Episode,
            SeriesEndBehavior = SeriesEndBehavior.Restart
        };
        var pool = new[]
        {
            new ScheduleCandidate { ItemId = Guid.NewGuid(), SeriesId = series, SeasonNumber = 1, EpisodeNumber = 1, Duration = TimeSpan.FromMinutes(30) },
            new ScheduleCandidate { ItemId = Guid.NewGuid(), SeriesId = series, SeasonNumber = 1, EpisodeNumber = 2, Duration = TimeSpan.FromMinutes(30) }
        };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(Day1, Day1.AddMinutes(90), Utc, new[] { block }, _ => pool);

        Assert.Equal(new[] { pool[0].ItemId, pool[1].ItemId, pool[0].ItemId }, programs.Select(p => p.ItemId));
    }

    [Fact]
    public void FullDayBlock_CoversMidnight_WithNoDeadAirGap()
    {
        // A 00:00–00:00 block is the full 24 hours — the schedule must be contiguous across midnight
        // (a 23:59 end used to leave a nightly one-minute hole where nothing aired).
        var block = MovieBlock(cooldownDays: 0);
        var pool = new[] { new ScheduleCandidate { ItemId = Guid.NewGuid(), Duration = TimeSpan.FromHours(1) } };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(Day1, Day1.AddDays(2), Utc, new[] { block }, _ => pool);

        Assert.Equal(48, programs.Count);
        for (var i = 1; i < programs.Count; i++)
        {
            Assert.Equal(programs[i - 1].EndUtc, programs[i].StartUtc);
        }
    }

    [Fact]
    public void Generate_DoesNotThrow_AcrossSpringForwardDstTransition()
    {
        // America/New_York springs forward at 02:00 -> 03:00 on 2026-03-08; a block boundary landing in
        // that nonexistent local hour must not crash the whole regeneration.
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var block = MovieBlock(start: "01:30", end: "04:00", cooldownDays: 0);
        var pool = new[] { new ScheduleCandidate { ItemId = Guid.NewGuid(), Duration = TimeSpan.FromMinutes(30) } };
        var rangeStart = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Unspecified), tz);

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        var programs = generator.Generate(rangeStart, rangeStart.AddDays(2), tz, new[] { block }, _ => pool);

        Assert.NotEmpty(programs);
    }

    [Fact]
    public void Episodes_SelectAnotherSeries_WhenActiveSeriesFinishes()
    {
        var seriesA = Guid.NewGuid();
        var seriesB = Guid.NewGuid();
        var block = new ProgrammingBlock
        {
            Name = "Sitcom Hour",
            StartTime = "00:00",
            EndTime = "23:59",
            ContentType = BlockContentType.Episode,
            Order = OrderingStrategy.Sequential,
            SeriesEndBehavior = SeriesEndBehavior.SelectAnotherSeries
        };
        var pool = new[]
        {
            new ScheduleCandidate { ItemId = Guid.NewGuid(), SeriesId = seriesA, SeasonNumber = 1, EpisodeNumber = 1, Duration = TimeSpan.FromMinutes(30) },
            new ScheduleCandidate { ItemId = Guid.NewGuid(), SeriesId = seriesB, SeasonNumber = 1, EpisodeNumber = 1, Duration = TimeSpan.FromMinutes(30) }
        };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        // Series A has only one episode, so slot 2 must come from a different series.
        var programs = generator.Generate(Day1, Day1.AddHours(1), Utc, new[] { block }, _ => pool);

        var seriesIds = programs.Select(p => pool.First(c => c.ItemId == p.ItemId).SeriesId).ToList();
        Assert.Equal(2, programs.Count);
        Assert.NotEqual(seriesIds[0], seriesIds[1]);
        Assert.Contains(seriesIds[0], new Guid?[] { seriesA, seriesB });
        Assert.Contains(seriesIds[1], new Guid?[] { seriesA, seriesB });
    }
}
