using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Broadcast.Data;

/// <summary>
/// Small shared helpers for the hand-rolled ADO.NET used by the repositories in this namespace —
/// avoids re-typing CreateParameter/Add and the round-trip DateTime format in every method.
/// </summary>
internal static class SqliteCommandExtensions
{
    /// <summary>
    /// Adds a named parameter to the command.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name, including its '$' prefix.</param>
    /// <param name="value">The parameter value.</param>
    public static void AddParam(this SqliteCommand command, string name, object value)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        command.Parameters.Add(param);
    }
}

/// <summary>
/// Converts <see cref="DateTime"/> to/from the round-trip ("O") text format used to store
/// UTC timestamps as SQLite TEXT columns.
/// </summary>
internal static class SqliteDateTime
{
    private const string Format = "O";

    /// <summary>
    /// Formats a UTC <see cref="DateTime"/> for storage.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The round-trip text representation.</returns>
    public static string ToText(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a value previously produced by <see cref="ToText"/>.
    /// </summary>
    /// <param name="text">The stored text.</param>
    /// <returns>The parsed UTC <see cref="DateTime"/>.</returns>
    public static DateTime Parse(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
