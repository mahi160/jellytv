using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <returns>The XMLTV document as a string.</returns>
    public static string Generate(string channelId, string channelName, IEnumerable<ProgramMetadata> programs)
    {
        var tv = new XElement(
            "tv",
            new XElement(
                "channel",
                new XAttribute("id", channelId),
                new XElement("display-name", channelName)));

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
        return document.ToString();
    }
}
