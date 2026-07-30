using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class AudioDevicesTests
{
    [Fact]
    public void Parse_ExtractsIdAndDescription()
    {
        var help = "Available audio devices:\n" +
                   "  'auto' (Autoselect device)\n" +
                   "  'wasapi/{abc}' (DELL S2721D)\n";

        var devices = AudioDevices.Parse(help);

        devices.Should().HaveCount(2);
        devices[0].Id.Should().Be("auto");
        devices[0].Description.Should().Be("Autoselect device");
        devices[1].Id.Should().Be("wasapi/{abc}");
        devices[1].Description.Should().Be("DELL S2721D");
    }

    [Fact]
    public void Parse_IgnoresNonDeviceLines()
    {
        var help = "Available audio devices:\nмусор\n  'auto' (Autoselect device)\n";

        AudioDevices.Parse(help).Should().ContainSingle().Which.Id.Should().Be("auto");
    }

    [Fact]
    public void Parse_EmptyOrNull_ReturnsEmpty()
    {
        AudioDevices.Parse("").Should().BeEmpty();
        AudioDevices.Parse(null).Should().BeEmpty();
    }
}
