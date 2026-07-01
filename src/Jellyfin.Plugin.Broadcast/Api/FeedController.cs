using System.Linq;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Feeds;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Broadcast.Api;

/// <summary>
/// The channel's M3U playlist and XMLTV guide, for IPTV clients and Jellyfin's own Live TV M3U tuner.
/// </summary>
[ApiController]
[Route("Broadcast")]
public class FeedController : ControllerBase
{
    private readonly ProgramRepository _programs;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedController"/> class.
    /// </summary>
    /// <param name="programs">The generated schedule.</param>
    /// <param name="libraryManager">Used to resolve title/description/artwork for each Program.</param>
    public FeedController(ProgramRepository programs, ILibraryManager libraryManager)
    {
        _programs = programs;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the channel's M3U playlist.
    /// </summary>
    /// <returns>The playlist, as plain text.</returns>
    [HttpGet("m3u")]
    public ContentResult GetM3u()
    {
        var config = Plugin.Instance!.Configuration;
        var streamUrl = $"{Request.Scheme}://{Request.Host}/Broadcast/Channel/Stream{Request.QueryString}";
        var body = M3uGenerator.Generate(Plugin.Instance.Id.ToString(), config.ChannelName, streamUrl);
        return Content(body, "audio/x-mpegurl");
    }

    /// <summary>
    /// Gets the channel's XMLTV guide, covering the currently generated schedule.
    /// </summary>
    /// <returns>The guide, as XML.</returns>
    [HttpGet("xmltv")]
    public ContentResult GetXmlTv()
    {
        var config = Plugin.Instance!.Configuration;
        var now = System.DateTime.UtcNow;
        var programs = _programs.GetRange(now, now.AddDays(config.ScheduleDurationDays));

        var metadata = programs.Select(p =>
        {
            var item = _libraryManager.GetItemById(p.ItemId);
            return new ProgramMetadata
            {
                StartUtc = p.StartUtc,
                EndUtc = p.EndUtc,
                Title = item?.Name ?? "Unknown",
                Description = item?.Overview,
                ArtworkUrl = item is not null && item.HasImage(ImageType.Primary, 0)
                    ? $"{Request.Scheme}://{Request.Host}/Items/{item.Id}/Images/Primary"
                    : null
            };
        });

        var body = XmlTvGenerator.Generate(Plugin.Instance.Id.ToString(), config.ChannelName, metadata);
        return Content(body, "application/xml");
    }
}
