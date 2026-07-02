using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Jellyfin.Plugin.Broadcast.Configuration;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

/// <summary>A trivial fake so ScheduleRegenerationService can be tested without a live Jellyfin library.</summary>
internal sealed class FakeMediaPoolResolver : IMediaPoolResolver
{
    public IReadOnlyList<ScheduleCandidate> Pool { get; set; } = Array.Empty<ScheduleCandidate>();

    public IReadOnlyList<ScheduleCandidate> GetMatchingPool(ProgrammingBlock block) => Pool;
}

public class ScheduleRegenerationServiceTests : IDisposable
{
    private static readonly DateTime Day1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _tempDir;
    private readonly BroadcastDbContext _db;
    private readonly ProgramRepository _programs;
    private readonly MovieHistoryRepository _movieHistory;
    private readonly EpisodeStateRepository _episodeState;
    private readonly FakeMediaPoolResolver _pool;
    private readonly ScheduleRegenerationService _service;

    public ScheduleRegenerationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "broadcast-regen-" + Guid.NewGuid());
        _db = new BroadcastDbContext(_tempDir);
        _programs = new ProgramRepository(_db);
        _movieHistory = new MovieHistoryRepository(_db);
        _episodeState = new EpisodeStateRepository(_db);
        _pool = new FakeMediaPoolResolver
        {
            Pool = new[] { new ScheduleCandidate { ItemId = Guid.NewGuid(), Duration = TimeSpan.FromHours(1) } }
        };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        _service = new ScheduleRegenerationService(_pool, generator, _programs, _movieHistory, _episodeState, NullLogger<ScheduleRegenerationService>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    private static PluginConfiguration ConfigWith(params ProgrammingBlock[] blocks)
    {
        var config = new PluginConfiguration { ScheduleDurationDays = 1, TimeZone = "UTC" };
        config.Blocks.AddRange(blocks);
        return config;
    }

    private static ProgrammingBlock ValidBlock(string name = "All Day") =>
        new() { Name = name, StartTime = "00:00", EndTime = "23:59" };

    [Fact]
    public void Regenerate_WithValidBlock_PopulatesSchedule()
    {
        var ran = _service.Regenerate(ConfigWith(ValidBlock()), Day1);

        Assert.True(ran);
        Assert.NotEmpty(_programs.GetRange(Day1, Day1.AddDays(1)));
    }

    [Fact]
    public void Regenerate_SkipsInvalidBlocks_ButStillSchedulesValidOnes()
    {
        var bad = ValidBlock("Bad");
        bad.StartTime = "not-a-time";
        var config = ConfigWith(ValidBlock("Good"), bad);

        var ran = _service.Regenerate(config, Day1);

        Assert.True(ran);
        var programs = _programs.GetRange(Day1, Day1.AddDays(1));
        Assert.NotEmpty(programs);
        Assert.All(programs, p => Assert.Equal("Good", p.BlockName));
    }

    [Fact]
    public void Regenerate_AllBlocksInvalid_DoesNotThrow_LeavesScheduleEmpty()
    {
        var bad = ValidBlock();
        bad.StartTime = "garbage";

        var ran = _service.Regenerate(ConfigWith(bad), Day1);

        Assert.True(ran);
        Assert.Empty(_programs.GetRange(Day1, Day1.AddDays(1)));
    }

    [Fact]
    public void Regenerate_InvalidTimeZone_FallsBackToUtc_DoesNotThrow()
    {
        var config = ConfigWith(ValidBlock());
        config.TimeZone = "Not/A/RealZone";

        var ran = _service.Regenerate(config, Day1);

        Assert.True(ran);
        Assert.NotEmpty(_programs.GetRange(Day1, Day1.AddDays(1)));
    }

    [Fact]
    public void Regenerate_PrunesMovieHistoryOlderThanRetentionWindow()
    {
        var itemId = Guid.NewGuid();
        _movieHistory.RecordAired(itemId, Day1.AddDays(-1000));

        _service.Regenerate(ConfigWith(ValidBlock()), Day1);

        Assert.Equal(0, _movieHistory.GetAiredCount(itemId));
    }

    [Fact]
    public void Regenerate_PreservesCurrentlyAiringProgram_InsteadOfCuttingItOff()
    {
        _service.Regenerate(ConfigWith(ValidBlock()), Day1);
        var before = _programs.GetProgramAt(Day1.AddMinutes(30));
        Assert.NotNull(before);

        // Regenerate again "later", mid-program — must not replace the program currently airing.
        var laterNow = before!.StartUtc.AddMinutes(30);
        _service.Regenerate(ConfigWith(ValidBlock()), laterNow);

        var after = _programs.GetProgramAt(laterNow);
        Assert.NotNull(after);
        Assert.Equal(before.ItemId, after!.ItemId);
        Assert.Equal(before.StartUtc, after.StartUtc);
        Assert.Equal(before.EndUtc, after.EndUtc);
    }

    [Fact]
    public void Regenerate_DoesNotInflateMovieAiredCount_OnRepeatedRegeneration()
    {
        // A single-item pool means every generated slot is the same movie. Re-running regeneration a
        // few minutes later (as debounced library-change/config-save triggers would) must not multiply
        // its recorded airing count each time.
        _service.Regenerate(ConfigWith(ValidBlock()), Day1);
        var itemId = _pool.Pool[0].ItemId;

        var secondRunNow = Day1.AddMinutes(5);
        _service.Regenerate(ConfigWith(ValidBlock()), secondRunNow);
        var countAfterSecondRun = _movieHistory.GetAiredCount(itemId);

        _service.Regenerate(ConfigWith(ValidBlock()), secondRunNow.AddMinutes(5));

        Assert.Equal(countAfterSecondRun, _movieHistory.GetAiredCount(itemId));
    }

    [Fact]
    public void Regenerate_ConcurrentCall_SkipsInsteadOfRunningTwice()
    {
        // Simulate "already running" by calling Regenerate again from inside a pool-provider callback,
        // re-entering while the outer call's semaphore is still held.
        var reentrantRan = true;
        ScheduleRegenerationService? service = null;

        var reentrantPool = new ReentrantPoolResolver(() =>
        {
            reentrantRan = service!.Regenerate(ConfigWith(ValidBlock()), Day1);
        })
        {
            Pool = _pool.Pool
        };

        var generator = new ScheduleGenerator(_movieHistory, _episodeState, new Random(1));
        service = new ScheduleRegenerationService(reentrantPool, generator, _programs, _movieHistory, _episodeState, NullLogger<ScheduleRegenerationService>.Instance);

        var outerRan = service.Regenerate(ConfigWith(ValidBlock()), Day1);

        Assert.True(outerRan);
        Assert.False(reentrantRan);
    }

    private sealed class ReentrantPoolResolver : IMediaPoolResolver
    {
        private readonly Action _onFirstCall;
        private bool _called;

        public ReentrantPoolResolver(Action onFirstCall)
        {
            _onFirstCall = onFirstCall;
        }

        public IReadOnlyList<ScheduleCandidate> Pool { get; set; } = Array.Empty<ScheduleCandidate>();

        public IReadOnlyList<ScheduleCandidate> GetMatchingPool(ProgrammingBlock block)
        {
            if (!_called)
            {
                _called = true;
                _onFirstCall();
            }

            return Pool;
        }
    }
}
