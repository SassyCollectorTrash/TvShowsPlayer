using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class MpvPathResolverTests : IDisposable
{
    private readonly string _appDir = Path.Combine(Path.GetTempPath(), $"tvsp_mpv_{Guid.NewGuid():N}");

    public MpvPathResolverTests() => Directory.CreateDirectory(_appDir);

    public void Dispose()
    {
        if (Directory.Exists(_appDir))
            Directory.Delete(_appDir, recursive: true);
    }

    private string MakeBundled()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_appDir, "mpv")).FullName;
        var path = Path.Combine(dir, "mpv.exe");
        File.WriteAllText(path, "");

        return path;
    }

    private string MakeOwn(string name)
    {
        var path = Path.Combine(_appDir, name);
        File.WriteAllText(path, "");

        return path;
    }

    [Fact]
    public void Resolve_NothingConfigured_UsesBundled()
    {
        var bundled = MakeBundled();

        MpvPathResolver.Resolve("", _appDir).Should().Be(bundled);
    }

    [Fact]
    public void Resolve_LegacyDefaultPath_PrefersBundled()
    {
        // прежний дефолт указывал на mpv в системе; на машине автора он существует,
        // и программа незаметно играла чужим плеером вместо комплектного
        var bundled = MakeBundled();

        MpvPathResolver.Resolve(MpvPathResolver.LegacyDefaultPath, _appDir).Should().Be(bundled);
    }

    [Fact]
    public void Resolve_PathChosenByUser_IsRespected()
    {
        MakeBundled();
        var own = MakeOwn("свой-mpv.exe");

        MpvPathResolver.Resolve(own, _appDir).Should().Be(own);
    }

    [Fact]
    public void Resolve_ConfiguredPathMissing_FallsBackToBundled()
    {
        var bundled = MakeBundled();

        MpvPathResolver.Resolve(@"Z:\нет\mpv.exe", _appDir).Should().Be(bundled);
    }

    [Fact]
    public void Resolve_NoBundled_KeepsConfiguredPath()
    {
        var own = MakeOwn("свой-mpv.exe");

        MpvPathResolver.Resolve(own, _appDir).Should().Be(own);
    }

    [Fact]
    public void Resolve_NothingAvailable_DoesNotThrow()
    {
        MpvPathResolver.Resolve(null, _appDir).Should().BeEmpty();
    }
}
