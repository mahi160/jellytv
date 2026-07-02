using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Broadcast.Feeds;

/// <summary>
/// Builds a valid XMLTV guide document for the channel's schedule.
/// </summary>
public static class XmlTvGenerator
{
    private static string FormatXmlTvDate(DateTime utc) =>
        utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + " +0000";

    /// <summary>
    /// Generates an XMLTV document for the given channel and Programs.
    /// </summary>
    /// <param name="channelId">A stable id for the channel (used as tvg-id / xmltv channel id).</param>
    /// <param name="channelName">The channel's display name.</param>
    /// <param name="programs">The Programs to include, with display metadata.</param>
    /// <param name="logoUrl">Optional channel logo URL, or null.</param>
    /// <returns>The XMLTV document as a string.</returns>
    public static string Generate(string channelId, string channelName, IEnumerable<ProgramMetadata> programs, string? logoUrl = null)
    {
        var channelElement = new XElement(
            "channel",
            new XAttribute("id", channelId),
            new XElement("display-name", channelName));

        if (!string.IsNullOrEmpty(logoUrl))
        {
            channelElement.Add(new XElement("icon", new XAttribute("src", logoUrl)));
        }

        var tv = new XElement("tv", channelElement);

        foreach (var program in programs)
        {
            var programmeElement = new XElement(
                "programme",
                new XAttribute("start", FormatXmlTvDate(program.StartUtc)),
                new XAttribute("stop", FormatXmlTvDate(program.EndUtc)),
                new XAttribute("channel", channelId),
                new XElement("title", program.Title));

            if (!string.IsNullOrEmpty(program.Description))
            {
                programmeElement.Add(new XElement("desc", program.Description));
            }

            if (!string.IsNullOrEmpty(program.ArtworkUrl))
            {
                programmeElement.Add(new XElement("icon", new XAttribute("src", program.ArtworkUrl)));
            }

            tv.Add(programmeElement);
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), tv);

        // XDocument.ToString() silently drops the <?xml ?> declaration; some XMLTV consumers expect it.
        using var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
