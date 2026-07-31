using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class AppConfigTests
{
    [Fact]
    public void New_HasExpectedDefaults()
    {
        var c = new AppConfig();

        c.Window.Should().Be(4);
        c.Step.Should().Be(2);
        c.CapRotations.Should().Be(200);
        c.Volume.Should().Be(70);
        c.FsScreen.Should().Be(0);   // основной монитор: безопасно и на одноэкранной машине
        c.ChannelName.Should().Be("LocalTV");
        c.ExcludedShows.Should().BeEmpty();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tvsp_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var c = new AppConfig
            {
                CartoonsRoot = @"C:\Cartoons",
                Window = 6,
                Step = 3,
                AudioDevice = "wasapi/{abc}",
                RetroTheme = true,
                ExcludedShows = { "Ранма 1-2", "Попсовый Эпос" },
            };

            c.Save(path);
            var loaded = AppConfig.Load(path);

            loaded.CartoonsRoot.Should().Be(@"C:\Cartoons");
            loaded.Window.Should().Be(6);
            loaded.Step.Should().Be(3);
            loaded.AudioDevice.Should().Be("wasapi/{abc}");
            loaded.RetroTheme.Should().BeTrue();
            loaded.ExcludedShows.Should().Equal("Ранма 1-2", "Попсовый Эпос");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"tvsp_missing_{Guid.NewGuid():N}.json");

        var loaded = AppConfig.Load(missing);

        loaded.Window.Should().Be(4);
    }
}
