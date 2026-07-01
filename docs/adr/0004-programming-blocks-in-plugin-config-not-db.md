# Programming Blocks live in PluginConfiguration, not a DB table

The original schema planned a `ProgrammingBlocks` table. Blocks are admin-authored rules edited occasionally through the dashboard, not transactional or high-volume data — a natural fit for Jellyfin's standard `PluginConfiguration` (XML-serialized settings blob), the same mechanism every other Jellyfin plugin uses for its settings.

Decision: Programming Blocks are a list on `PluginConfiguration`. The SQLite database is reserved for genuinely runtime/generated state that changes on its own: `Schedules` (generated Programs), `EpisodeState`, and `MovieHistory`. This avoids building CRUD/migration machinery for what is really just settings.
