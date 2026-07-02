using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Broadcast.Feeds;
using Xunit;

namespace Jellyfin.Plugin.Broadcast.Tests;

public class FeedGeneratorTests
{
    [Fact]
    public void M3uGenerator_ProducesValidHeaderAndEntry()
    {
        var m3u = M3uGenerator.Generate("broadcast", "My TV", "http://server/Broadcast/Channel/Stream");

        Assert.StartsWith("#EXTM3U", m3u);
        Assert.Contains("#EXTINF:-1 tvg-id=\"broadcast\",My TV", m3u);
        Assert.Contains("http://server/Broadcast/Channel/Stream", m3u);
    }

    [Fact]
    public void XmlTvGenerator_ProducesChannelAndProgrammeElements()
    {
        var programs = new List<ProgramMetadata>
        {
            new()
            {
                StartUtc = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 1, 1, 22, 0, 0, DateTimeKind.Utc),
                Title = "Oppenheimer",
                Description = "A biopic.",
                ArtworkUrl = "http://server/Items/abc/Images/Primary"
            }
        };

        var xml = XmlTvGenerator.Generate("broadcast", "My TV", programs);

        Assert.Contains("<channel id=\"broadcast\">", xml);
        Assert.Contains("<display-name>My TV</display-name>", xml);
        Assert.Contains("start=\"20260101200000 +0000\"", xml);
        Assert.Contains("stop=\"20260101220000 +0000\"", xml);
        Assert.Contains("<title>Oppenheimer</title>", xml);
        Assert.Contains("<desc>A biopic.</desc>", xml);
        Assert.Contains("<icon src=\"http://server/Items/abc/Images/Primary\" />", xml);
    }

    [Fact]
    public void M3uGenerator_EscapesCommaInChannelName_SoExtinfDoesNotBreak()
    {
        var m3u = M3uGenerator.Generate("broadcast", "News, Weather", "http://server/Broadcast/Channel/Stream");

        // A literal comma in the name would otherwise be parsed by IPTV clients as the
        // attributes/display-name separator, corrupting the entry.
        Assert.DoesNotContain("News, Weather", m3u);
        Assert.Contains("News  Weather", m3u);
    }

    [Fact]
    public void M3uGenerator_IncludesLogoWhenProvided()
    {
        var m3u = M3uGenerator.Generate("broadcast", "My TV", "http://server/stream", "http://server/logo.png");

        Assert.Contains("tvg-logo=\"http://server/logo.png\"", m3u);
    }

    [Fact]
    public void XmlTvGenerator_IncludesXmlDeclaration()
    {
        var xml = XmlTvGenerator.Generate("broadcast", "My TV", Array.Empty<ProgramMetadata>());

        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void XmlTvGenerator_EscapesSpecialCharactersInTitles()
    {
        var programs = new List<ProgramMetadata>
        {
            new()
            {
                StartUtc = DateTime.UtcNow,
                EndUtc = DateTime.UtcNow.AddHours(1),
                Title = "Tom & Jerry <Movie>"
            }
        };

        var xml = XmlTvGenerator.Generate("broadcast", "My TV", programs);

        Assert.DoesNotContain("<title>Tom & Jerry <Movie></title>", xml);
        Assert.Contains("Tom &amp; Jerry &lt;Movie&gt;", xml);
    }
}
