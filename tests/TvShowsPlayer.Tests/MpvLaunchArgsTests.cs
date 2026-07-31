using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class MpvLaunchArgsTests
{
    private static MpvLaunchOptions DevOptions() => new()
    {
        ConfigDir = @"D:\dev-config",
        Playlist = @"C:\Media\channel.m3u",
        PipePath = @"\\.\pipe\localtvmpv-dev",
    };

    [Fact]
    public void Build_Always_IncludesConfigDirAndPipe()
    {
        var args = MpvLaunchArgs.Build(DevOptions());

        args.Should().Contain(@"--config-dir=D:\dev-config");
        args.Should().Contain(@"--input-ipc-server=\\.\pipe\localtvmpv-dev");
    }

    [Fact]
    public void Build_Always_PutsPlaylistLast()
    {
        var args = MpvLaunchArgs.Build(DevOptions());

        args[^1].Should().Be(@"C:\Media\channel.m3u");
    }

    [Fact]
    public void Build_WhenNotFullscreen_AddsNoFullscreenFlag()
    {
        var options = DevOptions() with { Fullscreen = false };

        var args = MpvLaunchArgs.Build(options);

        args.Should().Contain("--fullscreen=no");
    }

    [Fact]
    public void Build_WhenFullscreen_OmitsFullscreenFlag()
    {
        var options = DevOptions() with { Fullscreen = true };

        var args = MpvLaunchArgs.Build(options);

        args.Should().NotContain(a => a.StartsWith("--fullscreen"));
    }

    [Fact]
    public void Build_WithChannelOsdRoot_AddsScriptOpts()
    {
        var options = DevOptions() with { ChannelOsdRoot = @"C:\Cartoons" };

        var args = MpvLaunchArgs.Build(options);

        args.Should().Contain(@"--script-opts=channelosd-root=C:\Cartoons");
    }

    [Fact]
    public void Build_WithoutChannelOsdRoot_OmitsScriptOpts()
    {
        var args = MpvLaunchArgs.Build(DevOptions());

        args.Should().NotContain(a => a.StartsWith("--script-opts"));
    }
}
