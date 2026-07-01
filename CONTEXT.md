# Jellyfin Broadcast

Turns a Jellyfin media library into a single always-on virtual TV channel with a generated schedule, M3U playlist, and XMLTV guide.

## Language

**Channel**:
The single always-on virtual TV station the plugin exposes.

**Programming Block**:
A recurring rule defining a time range, media filters, and ordering strategy (e.g. "Prime Time, 19:00-23:00, Movies, rating 7+"). Used both for the rule definition and, by context, the concrete slot it produces in a schedule.

**Program**:
One concrete scheduled item in the timeline — a specific piece of media at a specific start/end time (e.g. "20:00 Oppenheimer").
_Avoid_: Schedule Entry, Airing, Broadcast (for this concept)

**Schedule**:
The generated sequence of Programs covering a configured time range (24h / 7d / 30d).

**Active Series**:
The one TV series a given Programming Block is currently playing through, sequentially by episode. A block plays a single Active Series to completion before selecting the next one.

**Cooldown**:
The minimum time a movie must wait after airing before it can be scheduled again.
_Avoid_: Replay Delay
