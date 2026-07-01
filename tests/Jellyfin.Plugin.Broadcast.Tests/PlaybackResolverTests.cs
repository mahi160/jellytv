using System;
using System.IO;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Playback;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class PlaybackResolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BroadcastDbContext _db;
    private readonly ProgramRepository _programs;
    private readonly PlaybackResolver _resolver;

    public PlaybackResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "broadcast-playback-" + Guid.NewGuid());
        _db = new BroadcastDbContext(_tempDir);
        _programs = new ProgramRepository(_db);
        _resolver = new PlaybackResolver(_programs);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    [Fact]
    public void GetCurrent_ReturnsOffsetIntoTheAiringProgram()
    {
        var start = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();
        _programs.ReplaceAll(new[] { new Program { BlockName = "Prime Time", ItemId = itemId, StartUtc = start, EndUtc = start.AddHours(2) } });

        var resolved = _resolver.GetCurrent(start.AddMinutes(17));

        Assert.NotNull(resolved);
        Assert.Equal(itemId, resolved!.Program.ItemId);
        Assert.Equal(TimeSpan.FromMinutes(17), resolved.Offset);
    }

    [Fact]
    public void GetCurrent_ReturnsNull_WhenNothingScheduled()
    {
        Assert.Null(_resolver.GetCurrent(DateTime.UtcNow));
    }

    [Fact]
    public void GetNext_ReturnsTheProgramAfterWhatsCurrentlyAiring()
    {
        var start = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _programs.ReplaceAll(new[]
        {
            new Program { BlockName = "A", ItemId = first, StartUtc = start, EndUtc = start.AddHours(1) },
            new Program { BlockName = "A", ItemId = second, StartUtc = start.AddHours(1), EndUtc = start.AddHours(2) }
        });

        var next = _resolver.GetNext(start.AddMinutes(30));

        Assert.NotNull(next);
        Assert.Equal(second, next!.ItemId);
    }

    [Fact]
    public void GetNext_WhenNothingCurrentlyAiring_ReturnsNextUpcomingProgram()
    {
        var start = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
        var itemId = Guid.NewGuid();
        _programs.ReplaceAll(new[] { new Program { BlockName = "A", ItemId = itemId, StartUtc = start, EndUtc = start.AddHours(1) } });

        var next = _resolver.GetNext(start.AddHours(-1));

        Assert.NotNull(next);
        Assert.Equal(itemId, next!.ItemId);
    }
}
