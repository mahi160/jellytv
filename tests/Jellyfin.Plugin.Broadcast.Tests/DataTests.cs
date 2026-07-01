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
}
