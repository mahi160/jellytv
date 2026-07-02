using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Playback;
using Jellyfin.Plugin.Broadcast.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Broadcast.Api;

/// <summary>
/// The channel's REST API: what's on now/next, the full schedule, and manual regeneration.
/// Requires an authenticated Jellyfin user — not public. See M3uGenerator/XmlTvGenerator for how
/// clients authenticate to the actual feed URLs (an api_key query param, same as any other Jellyfin API route).
/// </summary>
[Authorize]
[ApiController]
[Route("Broadcast/Channel")]
public class ChannelController : ControllerBase
{
    private readonly PlaybackResolver _playbackResolver;
    private readonly ProgramRepository _programs;
    private readonly ScheduleRegenerationService _regenerationService;
    private readonly ILogger<ChannelController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelController"/> class.
    /// </summary>
    /// <param name="playbackResolver">Resolves current/next Program.</param>
    /// <param name="programs">The generated schedule.</param>
    /// <param name="regenerationService">Regenerates the schedule.</param>
    /// <param name="logger">Logger.</param>
    public ChannelController(PlaybackResolver playbackResolver, ProgramRepository programs, ScheduleRegenerationService regenerationService, ILogger<ChannelController> logger)
    {
        _playbackResolver = playbackResolver;
        _programs = programs;
        _regenerationService = regenerationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the Program currently airing and the offset to start playback at.
    /// </summary>
    /// <returns>The current Program, or 404 if nothing is scheduled right now.</returns>
    [HttpGet("Current")]
    public ActionResult<ProgramDto> GetCurrent()
    {
        try
        {
            var resolved = _playbackResolver.GetCurrent(DateTime.UtcNow);
            return resolved is null ? NotFound() : ProgramDto.From(resolved.Program, resolved.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve the current Program");
            return Problem("Failed to resolve the current Program — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Gets the Program that will air next.
    /// </summary>
    /// <returns>The next Program, or 404 if none is scheduled.</returns>
    [HttpGet("Next")]
    public ActionResult<ProgramDto> GetNext()
    {
        try
        {
            var next = _playbackResolver.GetNext(DateTime.UtcNow);
            return next is null ? NotFound() : ProgramDto.From(next, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve the next Program");
            return Problem("Failed to resolve the next Program — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Gets the full generated schedule from now through the configured schedule duration.
    /// </summary>
    /// <returns>The upcoming Programs, in order.</returns>
    [HttpGet("Schedule")]
    public ActionResult<IEnumerable<ProgramDto>> GetSchedule()
    {
        try
        {
            var config = Plugin.Instance!.Configuration;
            var now = DateTime.UtcNow;
            var programs = _programs.GetRange(now, now.AddDays(config.ScheduleDurationDays));
            return Ok(programs.Select(p => ProgramDto.From(p, null)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the schedule");
            return Problem("Failed to read the schedule — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Regenerates the schedule immediately (full wipe-and-rebuild — manual overrides are not preserved).
    /// A regeneration already in progress (daily task, auto-trigger, or another request) is reported as such
    /// rather than run twice concurrently.
    /// </summary>
    /// <returns>200 OK if it ran, 409 Conflict if one was already in progress, 500 if it failed.</returns>
    [Authorize(Policy = "RequiresElevation")]
    [HttpPost("Regenerate")]
    public IActionResult Regenerate()
    {
        try
        {
            var ran = _regenerationService.Regenerate(Plugin.Instance!.Configuration, DateTime.UtcNow);
            return ran ? Ok() : Conflict("A regeneration is already in progress.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual schedule regeneration failed");
            return Problem("Schedule regeneration failed — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Redirects to whatever's airing right now, at the resolved playback offset.
    /// Per ADR 0002, this is a best-effort convenience for generic IPTV players — Jellyfin's own apps
    /// should call <see cref="GetCurrent"/> and drive playback via the normal session API instead.
    /// Does not force direct-file streaming, so Jellyfin can still transcode on the fly for incompatible
    /// codecs — a bare redirect can't run the full PlaybackInfo/device-profile handshake first either way.
    /// </summary>
    /// <returns>A redirect to the currently airing item's stream URL.</returns>
    [HttpGet("Stream")]
    public IActionResult Stream()
    {
        try
        {
            var resolved = _playbackResolver.GetCurrent(DateTime.UtcNow);
            if (resolved is null)
            {
                return NotFound();
            }

            var startTicks = resolved.Offset.Ticks;
            var target = $"/Videos/{resolved.Program.ItemId}/stream?startTimeTicks={startTicks}";
            var apiKey = Request.Query["api_key"].ToString();
            if (!string.IsNullOrEmpty(apiKey))
            {
                // Escaped: this value is attacker-controlled (arbitrary query string) and is otherwise
                // reflected straight into a redirect Location header — unescaped, it's a header-injection/open-redirect vector.
                target += $"&api_key={Uri.EscapeDataString(apiKey)}";
            }

            return Redirect(target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve the stream redirect");
            return Problem("Failed to resolve playback — check server logs.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
