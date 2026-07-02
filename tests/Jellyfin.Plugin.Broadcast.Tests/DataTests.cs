using System;
using System.IO;
using Jellyfin.Plugin.Broadcast.Data;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class DataTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BroadcastDbContext _db;

    public DataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "broadcast-tests-" + Guid.NewGuid());
        _db = new BroadcastDbContext(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ReplaceAll_ThenGetProgramAt_ResolvesTheAiringProgram()
    {
        var repo = new ProgramRepository(_db);
        var itemId = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);

        repo.ReplaceAll(new[]
        {
            new Program { BlockName = "Prime Time", ItemId = itemId, StartUtc = start, EndUtc = end }
        });

        var resolved = repo.GetProgramAt(start.AddMinutes(17));

        Assert.NotNull(resolved);
        Assert.Equal(itemId, resolved!.ItemId);
        Assert.Equal("Prime Time", resolved.BlockName);
    }

    [Fact]
    public void ReplaceAll_WipesPreviousSchedule()
    {
        var repo = new ProgramRepository(_db);
        var start = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        repo.ReplaceAll(new[] { new Program { BlockName = "A", ItemId = Guid.NewGuid(), StartUtc = start, EndUtc = start.AddHours(1) } });

        repo.ReplaceAll(Array.Empty<Program>());

        Assert.Null(repo.GetProgramAt(start.AddMinutes(1)));
    }

    [Fact]
    public void EpisodeState_SetThenGet_RoundTrips()
    {
        var repo = new EpisodeStateRepository(_db);
        var seriesId = Guid.NewGuid();

        repo.Set(new ActiveSeriesState { BlockName = "Sitcom Hour", SeriesId = seriesId, SeasonNumber = 3, EpisodeNumber = 18 });
        var state = repo.Get("Sitcom Hour");

        Assert.NotNull(state);
        Assert.Equal(seriesId, state!.SeriesId);
        Assert.Equal(3, state.SeasonNumber);
        Assert.Equal(18, state.EpisodeNumber);
    }

    [Fact]
    public void EpisodeState_SetTwice_Upserts()
    {
        var repo = new EpisodeStateRepository(_db);
        var seriesId = Guid.NewGuid();
        repo.Set(new ActiveSeriesState { BlockName = "Sitcom Hour", SeriesId = seriesId, SeasonNumber = 3, EpisodeNumber = 18 });
        repo.Set(new ActiveSeriesState { BlockName = "Sitcom Hour", SeriesId = seriesId, SeasonNumber = 3, EpisodeNumber = 19 });

        var state = repo.Get("Sitcom Hour");

        Assert.Equal(19, state!.EpisodeNumber);
    }

    [Fact]
    public void MovieHistory_NoRecord_ReturnsNull()
    {
        var repo = new MovieHistoryRepository(_db);
        Assert.Null(repo.GetLastAired(Guid.NewGuid()));
    }

    [Fact]
    public void MovieHistory_RecordAired_ReturnsMostRecent()
    {
        var repo = new MovieHistoryRepository(_db);
        var itemId = Guid.NewGuid();
        repo.RecordAired(itemId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        repo.RecordAired(itemId, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var last = repo.GetLastAired(itemId);

        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), last);
    }

    [Fact]
    public void MovieHistory_PruneOlderThan_DeletesOnlyOldRows()
    {
        var repo = new MovieHistoryRepository(_db);
        var itemId = Guid.NewGuid();
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var recent = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        repo.RecordAired(itemId, old);
        repo.RecordAired(itemId, recent);

        var deleted = repo.PruneOlderThan(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, deleted);
        Assert.Equal(recent, repo.GetLastAired(itemId));
        Assert.Equal(1, repo.GetAiredCount(itemId));
    }

    [Fact]
    public void MovieHistory_PruneAtOrAfter_DeletesFutureSpeculativeRows_KeepsPast()
    {
        var repo = new MovieHistoryRepository(_db);
        var itemId = Guid.NewGuid();
        var now = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var past = now.AddDays(-1);
        var future = now.AddDays(1);
        repo.RecordAired(itemId, past);
        repo.RecordAired(itemId, future);

        var deleted = repo.PruneAtOrAfter(now);

        Assert.Equal(1, deleted);
        Assert.Equal(past, repo.GetLastAired(itemId));
        Assert.Equal(1, repo.GetAiredCount(itemId));
    }

    [Fact]
    public void MovieHistory_GetSummary_ReturnsLastAiredAndCountPerItem()
    {
        var repo = new MovieHistoryRepository(_db);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        repo.RecordAired(a, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        repo.RecordAired(a, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        repo.RecordAired(b, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var summary = repo.GetSummary();

        Assert.Equal(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), summary[a].LastAired);
        Assert.Equal(2, summary[a].Count);
        Assert.Equal(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), summary[b].LastAired);
        Assert.Equal(1, summary[b].Count);
    }

    [Fact]
    public void GetLastProgramForBlockAtOrBefore_ReturnsMostRecentMatchingBlock_ExcludingFuture()
    {
        var repo = new ProgramRepository(_db);
        var now = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var early = new Program { BlockName = "Sitcom Hour", ItemId = Guid.NewGuid(), StartUtc = now.AddHours(-2), EndUtc = now.AddHours(-1) };
        var recent = new Program { BlockName = "Sitcom Hour", ItemId = Guid.NewGuid(), StartUtc = now.AddMinutes(-30), EndUtc = now.AddMinutes(30) };
        var future = new Program { BlockName = "Sitcom Hour", ItemId = Guid.NewGuid(), StartUtc = now.AddHours(2), EndUtc = now.AddHours(3) };
        var otherBlock = new Program { BlockName = "Movie Night", ItemId = Guid.NewGuid(), StartUtc = now.AddMinutes(-10), EndUtc = now.AddHours(1) };
        repo.ReplaceAll(new[] { early, recent, future, otherBlock });

        var result = repo.GetLastProgramForBlockAtOrBefore("Sitcom Hour", now);

        Assert.NotNull(result);
        Assert.Equal(recent.ItemId, result!.ItemId);
    }
}
