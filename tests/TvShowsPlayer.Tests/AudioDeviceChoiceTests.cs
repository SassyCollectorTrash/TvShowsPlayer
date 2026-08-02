using FluentAssertions;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.Tests;

/// <summary>
/// Список звуковых выходов для окна настроек. Выбранное устройство может быть
/// временно недоступно (монитор спит, наушники отключены) — тогда его нельзя ни
/// потерять, ни молча подменить «автовыбором»: пропавшая настройка означает
/// пропавший звук после следующего включения.
/// </summary>
public sealed class AudioDeviceChoiceTests
{
    private static readonly AudioDevice Auto = new("auto", "Autoselect device");
    private static readonly AudioDevice Monitor = new("wasapi/{dell}", "DELL S2721D");
    private static readonly AudioDevice Headphones = new("wasapi/{major}", "Наушники");

    [Fact]
    public void WithStored_WhenStoredDeviceIsAvailable_ShouldReturnSameList()
    {
        var available = new[] { Auto, Monitor, Headphones };

        var result = AudioDevices.WithStored(available, Monitor.Id);

        result.Should().BeEquivalentTo(available, o => o.WithStrictOrdering());
    }

    [Fact]
    public void WithStored_WhenStoredDeviceIsGone_ShouldKeepItInList()
    {
        var available = new[] { Auto, Headphones };

        var result = AudioDevices.WithStored(available, Monitor.Id);

        result.Select(d => d.Id).Should().Contain(Monitor.Id);
    }

    [Fact]
    public void WithStored_WhenStoredDeviceIsGone_ShouldMarkItAsUnavailable()
    {
        var available = new[] { Auto, Headphones };

        var result = AudioDevices.WithStored(available, Monitor.Id);

        result.Single(d => d.Id == Monitor.Id).Description.Should().Contain("не подключ");
    }

    [Fact]
    public void WithStored_WhenNothingChosenYet_ShouldReturnSameList()
    {
        var available = new[] { Auto, Monitor };

        var result = AudioDevices.WithStored(available, null);

        result.Should().BeEquivalentTo(available, o => o.WithStrictOrdering());
    }

    [Fact]
    public void WithStored_WhenPlayerAnsweredNothing_ShouldStillKeepStoredDevice()
    {
        var result = AudioDevices.WithStored(Array.Empty<AudioDevice>(), Monitor.Id);

        result.Select(d => d.Id).Should().Contain(Monitor.Id);
    }
}
