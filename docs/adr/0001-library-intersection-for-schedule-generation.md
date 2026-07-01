# Schedule generation uses the intersection of all users' visible libraries

V1 has a single shared Channel with one Schedule watched by everyone, and multi-user personalized schedules are an explicit non-goal. Jellyfin still enforces per-user library permissions, so a naive implementation could schedule a Program from a library some users can't see, leaking its existence/metadata via the shared EPG and M3U.

Decision: schedule generation only draws from libraries visible to **every** Jellyfin user on the server (the intersection), not the union. This keeps state and scheduling fully global — no per-user schedule variants — at the cost of excluding content that's only visible to a subset of users.
