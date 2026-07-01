using System;
using System.Globalization;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// Records movie airings and answers Cooldown queries.
/// </summary>
public class MovieHistoryRepository
{
    private const string DateFormat = "O";
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
    public void RecordAired(Guid itemId, DateTime airedUtc)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO MovieHistory (ItemId, AiredUtc) VALUES ($itemId, $airedUtc)";
        var itemIdParam = command.CreateParameter();
        itemIdParam.ParameterName = "$itemId";
        itemIdParam.Value = itemId.ToString();
        command.Parameters.Add(itemIdParam);

        var airedParam = command.CreateParameter();
        airedParam.ParameterName = "$airedUtc";
        airedParam.Value = airedUtc.ToString(DateFormat, CultureInfo.InvariantCulture);
        command.Parameters.Add(airedParam);

        command.ExecuteNonQuery();
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
        var param = command.CreateParameter();
        param.ParameterName = "$itemId";
        param.Value = itemId.ToString();
        command.Parameters.Add(param);

        var result = command.ExecuteScalar();
        if (result is null or DBNull)
        {
            return null;
        }

        return DateTime.Parse((string)result, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
