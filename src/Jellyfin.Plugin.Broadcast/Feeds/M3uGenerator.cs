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
    /// <param name="logoUrl">Optional channel logo URL (tvg-logo), or null.</param>
    /// <returns>The M3U playlist as a string.</returns>
    public static string Generate(string channelId, string channelName, string streamUrl, string? logoUrl = null)
    {
        // An admin-entered channel name containing a comma would otherwise break #EXTINF's
        // "attributes,display-name" format for every M3U/IPTV client parsing this line.
        var safeName = channelName.Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ');
        var logoAttr = string.IsNullOrEmpty(logoUrl) ? string.Empty : $" tvg-logo=\"{logoUrl}\"";
        return $"#EXTM3U\n#EXTINF:-1 tvg-id=\"{channelId}\"{logoAttr},{safeName}\n{streamUrl}\n";
    }
}
