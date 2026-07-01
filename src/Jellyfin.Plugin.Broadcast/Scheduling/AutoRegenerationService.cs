using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// Triggers schedule regeneration automatically when the library or plugin settings change,
/// per the PRD's "Schedules regenerate automatically when: the library changes / settings change".
/// Library scans fire many rapid item events, so changes are debounced into a single regeneration.
/// </summary>
public class AutoRegenerationService : IHostedService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly ScheduleRegenerationService _regenerationService;
    private readonly ILogger<AutoRegenerationService> _logger;
    private readonly object _timerLock = new();
    private Timer? _debounceTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoRegenerationService"/> class.
    /// </summary>
    /// <param name="libraryManager">Used to detect library changes.</param>
    /// <param name="regenerationService">Regenerates the schedule.</param>
    /// <param name="logger">Logger.</param>
    public AutoRegenerationService(ILibraryManager libraryManager, ScheduleRegenerationService regenerationService, ILogger<AutoRegenerationService> logger)
    {
        _libraryManager = libraryManager;
        _regenerationService = regenerationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemUpdated += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;

        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemUpdated -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;

        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e) => ScheduleDebouncedRegeneration();

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e) => ScheduleDebouncedRegeneration();

    private void ScheduleDebouncedRegeneration()
    {
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => RunRegeneration(), null, DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void RunRegeneration()
    {
        try
        {
            if (Plugin.Instance is not null)
            {
                _regenerationService.Regenerate(Plugin.Instance.Configuration, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            // ponytail: broad catch is deliberate here — this runs on a background timer with no caller to
            // propagate to; a bad regeneration must never crash the server, just get logged and retried next trigger.
            _logger.LogError(ex, "Automatic schedule regeneration failed");
        }
    }
}
