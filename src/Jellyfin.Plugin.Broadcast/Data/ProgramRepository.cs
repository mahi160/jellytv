using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// Reads and writes the generated Schedule (a sequence of Programs).
/// </summary>
public class ProgramRepository
{
    private readonly BroadcastDbContext _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgramRepository"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    public ProgramRepository(BroadcastDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Wipes the entire schedule and inserts the given Programs (full regeneration; manual edits are not preserved — see ADR).
    /// </summary>
    /// <param name="programs">The freshly generated Programs.</param>
    public void ReplaceAll(IEnumerable<Program> programs)
    {
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM Programs";
            clear.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO Programs (BlockName, ItemId, StartUtc, EndUtc) VALUES ($blockName, $itemId, $start, $end)";
            var blockNameParam = insert.CreateParameter();
            blockNameParam.ParameterName = "$blockName";
            insert.Parameters.Add(blockNameParam);

            var itemIdParam = insert.CreateParameter();
            itemIdParam.ParameterName = "$itemId";
            insert.Parameters.Add(itemIdParam);

            var startParam = insert.CreateParameter();
            startParam.ParameterName = "$start";
            insert.Parameters.Add(startParam);

            var endParam = insert.CreateParameter();
            endParam.ParameterName = "$end";
            insert.Parameters.Add(endParam);

            foreach (var program in programs)
            {
                blockNameParam.Value = program.BlockName;
                itemIdParam.Value = program.ItemId.ToString();
                startParam.Value = SqliteDateTime.ToText(program.StartUtc);
                endParam.Value = SqliteDateTime.ToText(program.EndUtc);
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <summary>
    /// Finds the Program airing at the given UTC instant, if any.
    /// </summary>
    /// <param name="atUtc">The instant to resolve.</param>
    /// <returns>The Program airing at that time, or null.</returns>
    public Program? GetProgramAt(DateTime atUtc)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, BlockName, ItemId, StartUtc, EndUtc FROM Programs WHERE StartUtc <= $now AND EndUtc > $now ORDER BY StartUtc LIMIT 1";
        command.AddParam("$now", SqliteDateTime.ToText(atUtc));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProgram(reader) : null;
    }

    /// <summary>
    /// Returns all Programs starting at or after <paramref name="fromUtc"/>, ordered by start time.
    /// </summary>
    /// <param name="fromUtc">Lower bound (inclusive) on start time.</param>
    /// <param name="toUtc">Upper bound (exclusive) on start time.</param>
    /// <returns>The matching Programs.</returns>
    public IReadOnlyList<Program> GetRange(DateTime fromUtc, DateTime toUtc)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, BlockName, ItemId, StartUtc, EndUtc FROM Programs WHERE StartUtc >= $from AND StartUtc < $to ORDER BY StartUtc";
        command.AddParam("$from", SqliteDateTime.ToText(fromUtc));
        command.AddParam("$to", SqliteDateTime.ToText(toUtc));

        var results = new List<Program>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadProgram(reader));
        }

        return results;
    }

    /// <summary>
    /// Finds the first Program starting at or after the given UTC instant.
    /// </summary>
    /// <param name="fromUtc">Lower bound (inclusive) on start time.</param>
    /// <returns>The next Program, or null if none is scheduled after that time.</returns>
    public Program? GetNextAfter(DateTime fromUtc)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, BlockName, ItemId, StartUtc, EndUtc FROM Programs WHERE StartUtc >= $from ORDER BY StartUtc LIMIT 1";
        command.AddParam("$from", SqliteDateTime.ToText(fromUtc));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProgram(reader) : null;
    }

    private static Program ReadProgram(SqliteDataReader reader)
    {
        return new Program
        {
            Id = reader.GetInt64(0),
            BlockName = reader.GetString(1),
            ItemId = Guid.Parse(reader.GetString(2)),
            StartUtc = SqliteDateTime.Parse(reader.GetString(3)),
            EndUtc = SqliteDateTime.Parse(reader.GetString(4))
        };
    }
}
