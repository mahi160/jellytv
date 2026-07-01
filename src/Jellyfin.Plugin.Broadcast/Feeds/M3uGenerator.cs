namespace Jellyfin.Plugin.Broadcast.Feeds;

/// <summary>
/// Builds the channel's M3U playlist.
/// </summary>
public static class M3uGenerator
{
    /// <summary>
    /// Generates a single-channel M3U playlist.
    /// </summary>
    /// <param name="channelId">A stable id for the channel (tvg-id).</param>
    /// <param name="channelName">The channel's display name.</param>
    /// <param name="streamUrl">The URL clients should open to watch (see /Broadcast/Channel/Stream).</param>
    /// <returns>The M3U playlist as a string.</returns>
    public static string Generate(string channelId, string channelName, string streamUrl) =>
        $"#EXTM3U\n#EXTINF:-1 tvg-id=\"{channelId}\",{channelName}\n{streamUrl}\n";
}
