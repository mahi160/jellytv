# Schedule regeneration re-rolls Random/WeightedRandom blocks fresh each time

Regenerating a Schedule with no library/settings changes could either reproduce the same result (seeded/deterministic) or re-roll Random/WeightedRandom Programming Blocks fresh each time. We chose the latter for a more authentic "shuffle" feel.

Consequence: combined with the default daily auto-regeneration and the decision that manual edits don't survive regeneration, upcoming (not-yet-aired) Programs from Random/WeightedRandom blocks can change on every regeneration — a client that fetched `/api/channel/schedule` yesterday may see different upcoming picks today even though nothing was configured differently. This is expected behavior, not a bug.
