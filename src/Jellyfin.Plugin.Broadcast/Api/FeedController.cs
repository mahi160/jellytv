using System;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Feeds;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Broadcast.Api;

/// <summary>
/// The channel's M3U playlist and XMLTV guide, for IPTV clients and Jellyfin's own Live TV M3U tuner.
/// </summary>
[Authorize]
[ApiController]
[Route("Broadcast")]
public class FeedController : ControllerBase
{
    private readonly ProgramRepository _programs;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<FeedController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeedController"/> class.
    /// </summary>
    /// <param name="programs">The generated schedule.</param>
    /// <param name="libraryManager">Used to resolve title/description/artwork for each Program.</param>
    /// <param name="logger">Logger.</param>
    public FeedController(ProgramRepository programs, ILibraryManager libraryManager, ILogger<FeedController> logger)
    {
        _programs = programs;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets the channel's M3U playlist.
    /// </summary>
    /// <returns>The playlist, as plain text, or 500 if it couldn't be built (never a raw unhandled exception
    /// with a stack trace to an IPTV client sitting on the other end of this URL).</returns>
    [HttpGet("m3u")]
    public IActionResult GetM3u()
    {
        try
        {
            var config = Plugin.Instance!.Configuration;
            var streamUrl = $"{Request.Scheme}://{Request.Host}/Broadcast/Channel/Stream{Request.QueryString}";
            var body = M3uGenerator.Generate(Plugin.Instance.Id.ToString(), config.ChannelName, streamUrl, config.ChannelLogoUrl);
            return Content(body, "audio/x-mpegurl");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build M3U playlist");
            return Problem("Failed to build the M3U playlist — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Gets the channel's XMLTV guide, covering the currently generated schedule.
    /// </summary>
    /// <returns>The guide, as XML, or 500 if it couldn't be built.</returns>
    [HttpGet("xmltv")]
    public IActionResult GetXmlTv()
    {
        try
        {
            var config = Plugin.Instance!.Configuration;
            var now = DateTime.UtcNow;
            var range = _programs.GetRange(now, now.AddDays(config.ScheduleDurationDays));

            // GetRange only returns programs starting at/after "now" — without this, whatever's airing right
            // now (which started before "now") is missing from the guide entirely.
            var current = _programs.GetProgramAt(now);
            var programs = current is null ? range : new[] { current }.Concat(range.Where(p => p.Id != current.Id));

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

            var body = XmlTvGenerator.Generate(Plugin.Instance.Id.ToString(), config.ChannelName, metadata, config.ChannelLogoUrl);
            return Content(body, "application/xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build XMLTV guide");
            return Problem("Failed to build the XMLTV guide — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
