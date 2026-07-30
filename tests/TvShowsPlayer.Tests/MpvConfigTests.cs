using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class MpvConfigTests
{
    [Fact]
    public void Generate_ContainsChannelEssentials()
    {
        var conf = MpvConfig.Generate(new AppConfig());

        conf.Should().Contain("loop-playlist=inf");
        conf.Should().Contain("alang=rus,ru");
        conf.Should().Contain("save-position-on-quit=no");
        conf.Should().Contain("dynaudnorm");
        conf.Should().Contain("input-default-bindings=no");
        conf.Should().Contain("audio-file-auto=fuzzy");
    }

    [Fact]
    public void Generate_UsesConfigValues()
    {
        var conf = MpvConfig.Generate(new AppConfig
        {
            FsScreen = 2,
            Volume = 55,
            AudioDevice = "wasapi/{abc}",
        });

        conf.Should().Contain("fs-screen=2");
        conf.Should().Contain("volume=55");
        conf.Should().Contain("audio-device=wasapi/{abc}");
    }

    [Fact]
    public void Generate_WithoutAudioDevice_OmitsIt()
    {
        var conf = MpvConfig.Generate(new AppConfig { AudioDevice = null });

        conf.Should().NotContain("audio-device=");
    }

    [Fact]
    public void Generate_DoesNotSetIpcServer_AppOwnsPipePerMode()
    {
        var conf = MpvConfig.Generate(new AppConfig());

        conf.Should().NotContain("input-ipc-server");
    }
}
