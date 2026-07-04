using System;
using System.Globalization;
using Jellyfin.Plugin.Broadcast.Configuration;

namespace Jellyfin.Plugin.Broadcast.Scheduling;

/// <summary>
/// A Programming Block's daily time window — the single canonical definition of window semantics,
/// shared by <see cref="ScheduleGenerator"/> (slot lookup) and <see cref="BlockValidator"/> (overlap checks).
/// Rules: Start == End means the full 24 hours; End before Start means the window wraps past midnight.
/// </summary>
public readonly struct DailyWindow
{
    private const string TimeFormat = "hh\\:mm";

    /// <summary>
    /// Initializes a new instance of the <see cref="DailyWindow"/> struct.
    /// </summary>
    /// <param name="start">Daily start time.</param>
    /// <param name="end">Daily end time (equal to <paramref name="start"/> = 24h; earlier = wraps midnight).</param>
    public DailyWindow(TimeSpan start, TimeSpan end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Gets the daily start time.</summary>
    public TimeSpan Start { get; }

    /// <summary>Gets the daily end time.</summary>
    public TimeSpan End { get; }

    /// <summary>
    /// Builds the window for a Programming Block. The block's times must already be valid
    /// (see <see cref="BlockValidator"/>) — this throws on malformed input.
    /// </summary>
    /// <param name="block">The Programming Block.</param>
    /// <returns>The block's daily window.</returns>
    public static DailyWindow Of(ProgrammingBlock block) =>
        new(ParseTime(block.StartTime), ParseTime(block.EndTime));

    /// <summary>
    /// Parses a strict HH:mm time of day. (Strict: loose TimeSpan.Parse would read a stray "4" as 4 days.)
    /// </summary>
    /// <param name="value">The HH:mm text.</param>
    /// <returns>The time of day.</returns>
    public static TimeSpan ParseTime(string value) =>
        TimeSpan.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Tries to parse a strict HH:mm time of day.
    /// </summary>
    /// <param name="value">The HH:mm text.</param>
    /// <param name="result">The parsed time of day.</param>
    /// <returns>Whether the text was a valid HH:mm time.</returns>
    public static bool TryParseTime(string value, out TimeSpan result) =>
        TimeSpan.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Whether a time of day falls inside this window (start-inclusive, end-exclusive).
    /// </summary>
    /// <param name="timeOfDay">The time of day to test.</param>
    /// <returns>True if the window is active at that time.</returns>
    public bool Contains(TimeSpan timeOfDay) =>
        Start == End || (Start <= End
            ? timeOfDay >= Start && timeOfDay < End
            : timeOfDay >= Start || timeOfDay < End);

    /// <summary>
    /// Whether two daily windows overlap: either one's start falls inside the other.
    /// Back-to-back windows (one's end == the other's start) do not overlap.
    /// </summary>
    /// <param name="other">The other window.</param>
    /// <returns>True if the windows share any minute of the day.</returns>
    public bool Overlaps(DailyWindow other) =>
        Contains(other.Start) || other.Contains(Start);
}
