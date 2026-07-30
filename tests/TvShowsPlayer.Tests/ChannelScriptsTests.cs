using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class ChannelScriptsTests
{
    [Fact]
    public void ForMode_Dev_IncludesChannelOsd()
    {
        var scripts = ChannelScripts.ForMode(ChannelMode.Dev);

        scripts.Should().Contain("channel-osd.lua");
    }

    [Fact]
    public void ForMode_Dev_ExcludesResume()
    {
        var scripts = ChannelScripts.ForMode(ChannelMode.Dev);

        scripts.Should().NotContain("resume.lua");
    }

    [Fact]
    public void ForMode_Production_IncludesBothScripts()
    {
        var scripts = ChannelScripts.ForMode(ChannelMode.Production);

        scripts.Should().Contain("channel-osd.lua");
        scripts.Should().Contain("resume.lua");
    }
}
