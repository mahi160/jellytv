# Program transitions use native Jellyfin session handoff, not HLS stitching

Two options existed for switching between Programs: (a) client polls `/api/channel/current` and re-initiates native Jellyfin playback at the resolved offset when the Program changes, or (b) server-side HLS playlist stitching for a seamless single stream.

Decision: use native session handoff (a). It reuses Jellyfin's Direct Play/Direct Stream/transcode pipeline untouched, satisfying the "zero idle transcoding" and "playback identical to native Jellyfin" goals. HLS stitching would require remuxing/transcoding across arbitrary source codecs at every boundary, directly conflicting with the "avoid unnecessary transcoding" goal and the "Live video encoding" non-goal.

Consequence: transitions show a brief reload/black flash rather than a seamless broadcast-style cut. Client cooperation is required, so V1 targets Jellyfin's own apps (which already use the session/playback API correctly) as the primary supported clients. Generic IPTV players that only open the static `/live/channel` M3U URL once are best-effort — they may not transition between Programs automatically.
