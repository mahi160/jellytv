# Product Requirements Document (PRD)

## Project Name

**Jellyfin Broadcast** _(Working Title)_

---

# Overview

Jellyfin Broadcast is a Jellyfin plugin that transforms a user's media library into a continuously running television channel.

Instead of selecting individual movies or TV shows, users tune into a single always-on channel that follows a generated schedule, similar to traditional television networks such as Star Movies, AXN, or HBO.

The plugin generates schedules automatically, exposes an M3U playlist and XMLTV Electronic Program Guide (EPG), and integrates with Jellyfin's existing playback engine so that media plays from the correct live position.

The goal is to create a "lean-back" viewing experience where users simply open the channel and watch whatever is currently airing.

---

# Vision

Turn every Jellyfin server into a personalized television station.

---

# Problem Statement

Modern media servers require users to constantly choose what to watch.

Many users spend more time browsing than actually watching content.

Traditional television solves this by providing curated programming and continuous playback.

Jellyfin Broadcast recreates this experience using the user's own media library.

---

# Goals

- Create one continuously running virtual TV channel.
- Automatically generate daily or weekly programming schedules.
- Mimic the behavior of real television networks.
- Support IPTV clients through M3U and XMLTV.
- Reuse Jellyfin's playback engine whenever possible.
- Avoid unnecessary transcoding.
- Require minimal CPU usage while idle.
- Make programming fully configurable.

---

# Non-Goals (Version 1)

- Multiple channels
- DVR / Recording
- Live video encoding
- Commercial insertion
- Cloud synchronization
- Multi-user personalized schedules
- Streaming outside Jellyfin authentication

---

# User Experience

A user opens an IPTV client or compatible application and selects "My TV".

Instead of browsing a media library, playback immediately starts at the current position in the scheduled program.

Example:

```
Current Time: 8:17 PM

Now Playing

The Dark Knight

Started: 8:00 PM

Playback begins at 00:17:00
```

If another user joins five minutes later, they begin five minutes further into the same movie.

Everyone watches the same broadcast.

---

# Core Features

## 1. Single Broadcast Channel

The plugin exposes exactly one television channel.

Example:

```
My TV
```

or

```
Living Room TV
```

---

## 2. Schedule Generator

Generate programming for a configurable time period.

Supported ranges:

- 24 hours
- 7 days
- 30 days

Schedules regenerate automatically when:

- The library changes
- Settings change
- Manually requested
- Scheduled regeneration occurs (default: daily)

---

## 3. Programming Blocks

Programming is built from reusable blocks.

Example:

| Block            | Time          | Content           |
| ---------------- | ------------- | ----------------- |
| Morning Cartoons | 06:00 - 08:00 | Cartoons          |
| Sitcom Hour      | 08:00 - 10:00 | TV Episodes       |
| Movie Block      | 10:00 - 16:00 | Movies            |
| Prime Time       | 19:00 - 23:00 | High Rated Movies |
| Late Night       | 23:00 - 02:00 | Comedy            |

Each block defines:

- Start time
- End time
- Media source
- Filters
- Ordering strategy
- Repeat rules

---

## 4. Rules Engine

Programming blocks select media using filters.

Supported filters include:

- Library
- Genre
- Collection
- Tags
- Year
- Studio
- Director
- Actor
- Rating
- Runtime
- Resolution
- Favorite
- User-defined metadata
- Plugin metadata

Filters may be combined.

Example:

```
Movies

Genre:
Action

Rating:
7+

Year:
After 2010
```

---

## 5. Playback Ordering

Supported ordering methods:

- Sequential
- Random
- Weighted Random
- Chronological
- Newest First
- Oldest First
- Least Played
- Never Played
- Custom Ordering

---

## 6. Episode Tracking

Television series maintain their current episode.

Example:

Yesterday

```
Friends S03E18
```

Today

```
Friends S03E19
```

When the final episode finishes:

Configurable options:

- Restart series
- Select another matching series
- Random series

---

## 7. Movie Rotation

Movies should not repeat frequently.

Configurable cooldown.

Example:

```
Replay Delay

30 Days
```

---

## 8. Schedule Timeline

Generated schedule example:

```
06:00 Tom & Jerry

06:30 Friends

07:00 Friends

07:30 Modern Family

08:00 The Matrix

10:16 Brooklyn Nine-Nine

10:38 The Office

11:00 Finding Nemo

12:42 Interstellar

15:31 Toy Story

17:05 Avengers

20:00 Oppenheimer

23:05 Family Guy
```

---

## 9. Live Position Resolution

When playback begins:

1. Determine current time.
2. Find scheduled item.
3. Calculate elapsed playback time.
4. Request Jellyfin playback starting from calculated offset.

Example:

```
Current Time

20:17

Schedule

20:00 The Matrix

Playback Offset

00:17:00
```

---

## 10. XMLTV Generator

Generate a valid XMLTV guide.

Supports:

- Current program
- Upcoming programs
- Program descriptions
- Artwork
- Episode metadata

Refresh automatically when schedules regenerate.

---

## 11. M3U Generator

Expose a standard IPTV playlist.

Example:

```
#EXTM3U

#EXTINF:-1 tvg-id="mytv",My TV

http://server/live/channel
```

---

## 12. REST API

Public endpoints:

```
GET /api/channel/current

GET /api/channel/next

GET /api/channel/schedule

POST /api/channel/regenerate

GET /m3u

GET /xmltv
```

---

## 13. Admin Dashboard

Plugin configuration interface.

Sections:

### General

- Channel name
- Logo
- Channel number
- Timezone
- Schedule duration

### Programming

- Programming blocks
- Rules
- Ordering
- Repeat behavior

### Schedule

- Calendar
- Timeline
- Manual regeneration

### Metadata

- XMLTV settings
- Artwork
- Branding

---

# Playback Architecture

The plugin **does not** perform continuous video transcoding.

Instead:

- Generates schedules.
- Resolves current program.
- Calculates playback offset.
- Delegates streaming to Jellyfin.

Jellyfin remains responsible for:

- Direct Play
- Direct Stream
- Hardware Transcoding
- Audio selection
- Subtitle handling
- HDR
- Dolby Vision

This ensures playback behaves identically to native Jellyfin playback.

---

# Database Schema

Tables:

```
Channels

ProgrammingBlocks

Schedules

EpisodeState

MovieHistory

Settings

ScheduleHistory
```

---

# Configuration

Example configuration:

```yaml
channel:
  name: My TV
  number: 1

schedule:
  days: 7

movieRepeatDays: 30

episodeMode: Continue

blocks:
  - name: Morning Cartoons
    start: "06:00"
    end: "08:00"
    library: Kids
    order: Sequential

  - name: Sitcom
    start: "08:00"
    end: "10:00"
    genres:
      - Comedy
    type: Episode

  - name: Prime Time
    start: "19:00"
    end: "23:00"
    library: Movies
    rating: 7
    order: WeightedRandom
```

---

# Technical Architecture

```
Admin UI
      │
      ▼
Configuration
      │
      ▼
Rules Engine
      │
      ▼
Schedule Generator
      │
      ▼
SQLite Database
      │
      ▼
Playback Resolver
      │
      ▼
Jellyfin Playback API
      │
      ▼
Client
```

---

# Success Metrics

- Schedule generation completes in under 2 seconds for typical libraries.
- Zero idle transcoding.
- No additional CPU usage when no clients are connected.
- Playback startup under 2 seconds.
- Compatible with IPTV players supporting M3U and XMLTV.
- Playback quality identical to native Jellyfin playback whenever Direct Play is available.

---

# Future Roadmap

## Version 2

- Multiple channels
- AI-generated schedules
- Holiday programming
- Weekend marathons
- Theme weeks
- Intro bumpers
- Network idents
- "Next Up" overlays
- Live watermark
- Program ratings
- Channel branding packages

## Version 3

- Shared schedule marketplace
- Import/export schedules
- Community-created channel templates
- Smart recommendations based on watch history
- Multiple simultaneous broadcast channels
- Dynamic seasonal scheduling

---

# Risks

### Seamless Playback

Switching between scheduled items may introduce a short interruption depending on the playback protocol and client capabilities.

Mitigation:

- Investigate HLS playlist stitching for seamless transitions.
- Fall back to standard Jellyfin playback when necessary.

### Library Changes

Removing media may invalidate schedules.

Mitigation:

Automatically regenerate affected schedule segments.

### Metadata Consistency

Incorrect or missing metadata may reduce scheduling quality.

Mitigation:

Provide robust filtering and validation during schedule generation.

---

# Open Questions

- What is the best mechanism for seamless transitions between scheduled items (native Jellyfin session handoff vs. HLS playlist stitching)?
- Should schedule generation prioritize variety, randomness, or deterministic repeatability?
- Should programming blocks support nested rules (e.g., "Movies except horror on weekdays")?
- How should manual schedule overrides interact with automatic regeneration?
- Should clients be able to view the full future schedule or only a configurable time window?
