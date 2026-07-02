using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// The daily schedule regeneration task, visible (and re-triggerable/re-schedulable) under
/// Dashboard &gt; Scheduled Tasks. Its default trigger time comes from the plugin's configured
/// RegenerationTime, but an admin can override it like any other Jellyfin scheduled task.
/// </summary>
public class RegenerationScheduledTask : IScheduledTask
{
    private readonly ScheduleRegenerationService _regenerationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegenerationScheduledTask"/> class.
    /// </summary>
    /// <param name="regenerationService">Regenerates the schedule.</param>
    public RegenerationScheduledTask(ScheduleRegenerationService regenerationService)
    {
        _regenerationService = regenerationService;
    }

    /// <inheritdoc />
    public string Name => "Regenerate Broadcast Schedule";

    /// <inheritdoc />
    public string Key => "BroadcastRegenerateSchedule";

    /// <inheritdoc />
    public string Description => "Regenerates the Broadcast channel's schedule from its Programming Blocks.";

    /// <inheritdoc />
    public string Category => "Broadcast";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _regenerationService.Regenerate(Plugin.Instance!.Configuration, DateTime.UtcNow);
        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Strict HH:mm parse — loose TimeSpan.Parse would happily read a stray "4" as 4 *days*.
        // Note: this only takes effect the first time the task is created; Jellyfin persists the trigger
        // after that, so changing RegenerationTime later must be followed up under Dashboard > Scheduled Tasks.
        var timeOfDay = TimeSpan.TryParseExact(Plugin.Instance?.Configuration.RegenerationTime, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : new TimeSpan(4, 0, 0);

        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = timeOfDay.Ticks
        };
    }
}
