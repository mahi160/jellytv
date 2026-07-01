using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Broadcast.Data;
using Jellyfin.Plugin.Broadcast.Playback;
using Jellyfin.Plugin.Broadcast.Scheduling;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Broadcast.Api;

/// <summary>
/// The channel's public REST API: what's on now/next, the full schedule, and manual regeneration.
/// </summary>
[ApiController]
[Route("Broadcast/Channel")]
public class ChannelController : ControllerBase
{
    private readonly PlaybackResolver _playbackResolver;
    private readonly ProgramRepository _programs;
    private readonly ScheduleRegenerationService _regenerationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelController"/> class.
    /// </summary>
    /// <param name="playbackResolver">Resolves current/next Program.</param>
    /// <param name="programs">The generated schedule.</param>
    /// <param name="regenerationService">Regenerates the schedule.</param>
    public ChannelController(PlaybackResolver playbackResolver, ProgramRepository programs, ScheduleRegenerationService regenerationService)
    {
        _playbackResolver = playbackResolver;
        _programs = programs;
        _regenerationService = regenerationService;
    }

    /// <summary>
    /// Gets the Program currently airing and the offset to start playback at.
    /// </summary>
    /// <returns>The current Program, or 404 if nothing is scheduled right now.</returns>
    [HttpGet("Current")]
    public ActionResult<ProgramDto> GetCurrent()
    {
        var resolved = _playbackResolver.GetCurrent(DateTime.UtcNow);
        return resolved is null ? NotFound() : ProgramDto.From(resolved.Program, resolved.Offset);
    }

    /// <summary>
    /// Gets the Program that will air next.
    /// </summary>
    /// <returns>The next Program, or 404 if none is scheduled.</returns>
    [HttpGet("Next")]
    public ActionResult<ProgramDto> GetNext()
    {
        var next = _playbackResolver.GetNext(DateTime.UtcNow);
        return next is null ? NotFound() : ProgramDto.From(next, null);
    }

    /// <summary>
    /// Gets the full generated schedule from now through the configured schedule duration.
    /// </summary>
    /// <returns>The upcoming Programs, in order.</returns>
    [HttpGet("Schedule")]
    public ActionResult<IEnumerable<ProgramDto>> GetSchedule()
    {
        var config = Plugin.Instance!.Configuration;
        var now = DateTime.UtcNow;
        var programs = _programs.GetRange(now, now.AddDays(config.ScheduleDurationDays));
        return Ok(programs.Select(p => ProgramDto.From(p, null)));
    }

    /// <summary>
    /// Regenerates the schedule immediately (full wipe-and-rebuild — manual overrides are not preserved).
    /// </summary>
    /// <returns>204 No Content on success.</returns>
    [HttpPost("Regenerate")]
    public IActionResult Regenerate()
    {
        _regenerationService.Regenerate(Plugin.Instance!.Configuration, DateTime.UtcNow);
        return NoContent();
    }

    /// <summary>
    /// Redirects to whatever's airing right now, at the resolved playback offset.
    /// Per ADR 0002, this is a best-effort convenience for generic IPTV players — Jellyfin's own apps
    /// should call <see cref="GetCurrent"/> and drive playback via the normal session API instead.
    /// Forces direct-file streaming (no transcode negotiation), since a bare redirect can't run Jellyfin's
    /// PlaybackInfo/device-profile handshake first.
    /// </summary>
    /// <returns>A redirect to the currently airing item's stream URL.</returns>
    [HttpGet("Stream")]
    public IActionResult Stream()
    {
        var resolved = _playbackResolver.GetCurrent(DateTime.UtcNow);
        if (resolved is null)
        {
            return NotFound();
        }

        var startTicks = resolved.Offset.Ticks;
        var target = $"/Videos/{resolved.Program.ItemId}/stream?static=true&startTimeTicks={startTicks}";
        var apiKey = Request.Query["api_key"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            target += $"&api_key={apiKey}";
        }

        return Redirect(target);
    }
}
