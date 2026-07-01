using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// Records movie airings and answers Cooldown queries.
/// </summary>
public class MovieHistoryRepository
{
    private readonly BroadcastDbContext _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieHistoryRepository"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    public MovieHistoryRepository(BroadcastDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Records that a movie aired.
    /// </summary>
    /// <param name="itemId">The movie's Jellyfin item id.</param>
    /// <param name="airedUtc">When it aired.</param>
    public void RecordAired(Guid itemId, DateTime airedUtc) => RecordAiredBatch(new[] { (itemId, airedUtc) });

    /// <summary>
    /// Records that a batch of movies aired, in a single transaction. Used after a full schedule
    /// regeneration so history isn't written via one SQLite connection per airing (see <see cref="ProgramRepository.ReplaceAll"/>
    /// for the same batching pattern applied to Programs).
    /// </summary>
    /// <param name="airings">The item id and aired time of each airing.</param>
    public void RecordAiredBatch(IEnumerable<(Guid ItemId, DateTime AiredUtc)> airings)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO MovieHistory (ItemId, AiredUtc) VALUES ($itemId, $airedUtc)";
        var itemIdParam = command.CreateParameter();
        itemIdParam.ParameterName = "$itemId";
        command.Parameters.Add(itemIdParam);

        var airedParam = command.CreateParameter();
        airedParam.ParameterName = "$airedUtc";
        command.Parameters.Add(airedParam);

        foreach (var (itemId, airedUtc) in airings)
        {
            itemIdParam.Value = itemId.ToString();
            airedParam.Value = SqliteDateTime.ToText(airedUtc);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Gets the most recent time a movie aired, if ever.
    /// </summary>
    /// <param name="itemId">The movie's Jellyfin item id.</param>
    /// <returns>The last aired time (UTC), or null if it has never aired.</returns>
    public DateTime? GetLastAired(Guid itemId)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(AiredUtc) FROM MovieHistory WHERE ItemId = $itemId";
        command.AddParam("$itemId", itemId.ToString());

        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : SqliteDateTime.Parse((string)result);
    }

    /// <summary>
    /// Gets how many times a movie has aired (used for LeastPlayed/NeverPlayed ordering).
    /// Note: this is airings on the channel, not Jellyfin per-user watch history — state is global (see ADR 0001).
    /// </summary>
    /// <param name="itemId">The movie's Jellyfin item id.</param>
    /// <returns>The number of times it has aired.</returns>
    public int GetAiredCount(Guid itemId)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MovieHistory WHERE ItemId = $itemId";
        command.AddParam("$itemId", itemId.ToString());

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Deletes airings recorded before the given cutoff. Without this, MovieHistory grows forever on a
    /// long-running server — entries older than any block's Cooldown are no longer needed for anything.
    /// </summary>
    /// <param name="cutoffUtc">Airings strictly before this time are deleted.</param>
    /// <returns>How many rows were deleted.</returns>
    public int PruneOlderThan(DateTime cutoffUtc)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MovieHistory WHERE AiredUtc < $cutoff";
        command.AddParam("$cutoff", SqliteDateTime.ToText(cutoffUtc));

        return command.ExecuteNonQuery();
    }
}
