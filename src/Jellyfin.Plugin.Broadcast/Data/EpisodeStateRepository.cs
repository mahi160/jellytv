using System;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// Reads and writes each Programming Block's Active Series.
/// </summary>
public class EpisodeStateRepository
{
    private readonly BroadcastDbContext _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpisodeStateRepository"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    public EpisodeStateRepository(BroadcastDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets the given block's Active Series, if it has one yet.
    /// </summary>
    /// <param name="blockName">The Programming Block's name.</param>
    /// <returns>The Active Series state, or null if the block hasn't aired anything yet.</returns>
    public ActiveSeriesState? Get(string blockName)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SeriesId, SeasonNumber, EpisodeNumber FROM EpisodeState WHERE BlockName = $blockName";
        var param = command.CreateParameter();
        param.ParameterName = "$blockName";
        param.Value = blockName;
        command.Parameters.Add(param);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ActiveSeriesState
        {
            BlockName = blockName,
            SeriesId = Guid.Parse(reader.GetString(0)),
            SeasonNumber = reader.GetInt32(1),
            EpisodeNumber = reader.GetInt32(2)
        };
    }

    /// <summary>
    /// Sets (upserts) the given block's Active Series.
    /// </summary>
    /// <param name="state">The new state.</param>
    public void Set(ActiveSeriesState state)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EpisodeState (BlockName, SeriesId, SeasonNumber, EpisodeNumber)
            VALUES ($blockName, $seriesId, $season, $episode)
            ON CONFLICT(BlockName) DO UPDATE SET
                SeriesId = excluded.SeriesId,
                SeasonNumber = excluded.SeasonNumber,
                EpisodeNumber = excluded.EpisodeNumber
            """;

        AddParam(command, "$blockName", state.BlockName);
        AddParam(command, "$seriesId", state.SeriesId.ToString());
        AddParam(command, "$season", state.SeasonNumber);
        AddParam(command, "$episode", state.EpisodeNumber);

        command.ExecuteNonQuery();
    }

    private static void AddParam(Microsoft.Data.Sqlite.SqliteCommand command, string name, object value)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        command.Parameters.Add(param);
    }
}
