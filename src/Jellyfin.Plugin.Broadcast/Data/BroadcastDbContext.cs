using System.IO;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// Owns the SQLite connection string and schema for runtime/generated state
/// (Programs, EpisodeState, MovieHistory). Programming Blocks are config, not DB — see ADR 0004.
/// </summary>
public class BroadcastDbContext
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="BroadcastDbContext"/> class and ensures the schema exists.
    /// </summary>
    /// <param name="dataDirectory">Directory the plugin may write its database into.</param>
    public BroadcastDbContext(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "broadcast.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    /// <summary>
    /// Opens a new connection to the database.
    /// </summary>
    /// <returns>An open <see cref="SqliteConnection"/>.</returns>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Programs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BlockName TEXT NOT NULL,
                ItemId TEXT NOT NULL,
                StartUtc TEXT NOT NULL,
                EndUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Programs_StartUtc ON Programs (StartUtc);

            CREATE TABLE IF NOT EXISTS EpisodeState (
                BlockName TEXT PRIMARY KEY,
                SeriesId TEXT NOT NULL,
                SeasonNumber INTEGER NOT NULL,
                EpisodeNumber INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MovieHistory (
                ItemId TEXT NOT NULL,
                AiredUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_MovieHistory_ItemId ON MovieHistory (ItemId);
            """;
        command.ExecuteNonQuery();
    }
}
