using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class PlaylistWriterTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"tvsp_m3u_{Guid.NewGuid():N}");

    public PlaylistWriterTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Write_M3u_HasHeaderThenOnePathPerLine()
    {
        var m3u = Path.Combine(_dir, "channel.m3u");

        PlaylistWriter.Write(m3u, new[] { @"D:\C\a.mkv", @"D:\C\b.mkv" }, "sig");

        File.ReadAllText(m3u).Should().Be("#EXTM3U\n" + @"D:\C\a.mkv" + "\n" + @"D:\C\b.mkv" + "\n");
    }

    [Fact]
    public void Write_CreatesSigFileWithSignature()
    {
        var m3u = Path.Combine(_dir, "channel.m3u");

        PlaylistWriter.Write(m3u, new[] { "x" }, "abc123");

        File.ReadAllText(m3u + ".sig").Should().Be("abc123");
    }

    [Fact]
    public void Write_CreatesMissingDirectory()
    {
        var m3u = Path.Combine(_dir, "nested", "channel.m3u");

        PlaylistWriter.Write(m3u, new[] { "x" }, "sig");

        File.Exists(m3u).Should().BeTrue();
    }

    [Fact]
    public void IsUpToDate_WhenM3uMissing_ReturnsFalse()
    {
        var m3u = Path.Combine(_dir, "channel.m3u");

        PlaylistWriter.IsUpToDate(m3u, "sig").Should().BeFalse();
    }

    [Fact]
    public void IsUpToDate_WhenSignatureMatches_ReturnsTrue()
    {
        var m3u = Path.Combine(_dir, "channel.m3u");
        PlaylistWriter.Write(m3u, new[] { "x" }, "sig123");

        PlaylistWriter.IsUpToDate(m3u, "sig123").Should().BeTrue();
    }

    [Fact]
    public void IsUpToDate_WhenSignatureDiffers_ReturnsFalse()
    {
        var m3u = Path.Combine(_dir, "channel.m3u");
        PlaylistWriter.Write(m3u, new[] { "x" }, "old");

        PlaylistWriter.IsUpToDate(m3u, "new").Should().BeFalse();
    }
}
