using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Playback;
using Jellyfin.Plugin.Broadcast.Scheduling;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Broadcast;

/// <summary>
/// Registers Broadcast's services with Jellyfin's DI container so the API controllers can use them.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(_ => Plugin.Instance!.Db);
        serviceCollection.AddSingleton<ProgramRepository>();
        serviceCollection.AddSingleton<MovieHistoryRepository>();
        serviceCollection.AddSingleton<EpisodeStateRepository>();
        serviceCollection.AddSingleton<LibraryPoolResolver>();
        serviceCollection.AddSingleton<IMediaPoolResolver>(sp => sp.GetRequiredService<LibraryPoolResolver>());
        serviceCollection.AddSingleton<ScheduleGenerator>();
        serviceCollection.AddSingleton<ScheduleRegenerationService>();
        serviceCollection.AddSingleton<PlaybackResolver>();

        // Automatic regeneration: daily via Scheduled Tasks, plus on library/settings changes (debounced).
        serviceCollection.AddHostedService<AutoRegenerationService>();
    }
}
