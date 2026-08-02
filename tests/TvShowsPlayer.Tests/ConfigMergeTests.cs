using FluentAssertions;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.Tests;

/// <summary>
/// Сохранение настроек из окна. Окно держит снимок конфига с момента открытия, а
/// программа в это же время пишет в тот же файл: запоминает громкость с пульта и
/// уводит найденные новинки в исключения. Сохранение «как есть» затирало бы её
/// работу — в первую очередь выпускало бы в эфир сериал, который ещё качается.
/// </summary>
public sealed class ConfigMergeTests
{
    [Fact]
    public void KeepBackgroundChanges_WhenShowFoundAfterWindowOpened_ShouldStayExcluded()
    {
        var window = new AppConfig { ExcludedShows = { "Старый сериал" } };
        var disk = new AppConfig { ExcludedShows = { "Старый сериал", "Свежая закачка" } };

        ConfigMerge.KeepBackgroundChanges(window, disk, volumeWhenOpened: 70, showsInWindow: new[] { "Старый сериал" });

        window.ExcludedShows.Should().Contain("Свежая закачка");
    }

    [Fact]
    public void KeepBackgroundChanges_WhenUserIncludedShow_ShouldNotExcludeItBack()
    {
        var window = new AppConfig();                                  // человек снял исключение
        var disk = new AppConfig { ExcludedShows = { "Геркулес" } };

        ConfigMerge.KeepBackgroundChanges(window, disk, volumeWhenOpened: 70, showsInWindow: new[] { "Геркулес" });

        window.ExcludedShows.Should().BeEmpty();
    }

    [Fact]
    public void KeepBackgroundChanges_WhenShowFoundAfterWindowOpened_ShouldStayKnown()
    {
        var window = new AppConfig { KnownShows = { "Геркулес" } };
        var disk = new AppConfig { KnownShows = { "Геркулес", "Свежая закачка" } };

        ConfigMerge.KeepBackgroundChanges(window, disk, volumeWhenOpened: 70, showsInWindow: new[] { "Геркулес" });

        window.KnownShows.Should().Contain("Свежая закачка");
    }

    [Fact]
    public void KeepBackgroundChanges_WhenVolumeUntouchedInWindow_ShouldKeepVolumeFromRemote()
    {
        var window = new AppConfig { Volume = 70 };    // в окне не трогали
        var disk = new AppConfig { Volume = 95 };      // накрутили с клавиатуры

        ConfigMerge.KeepBackgroundChanges(window, disk, volumeWhenOpened: 70, showsInWindow: Array.Empty<string>());

        window.Volume.Should().Be(95);
    }

    [Fact]
    public void KeepBackgroundChanges_WhenVolumeChangedInWindow_ShouldKeepWindowVolume()
    {
        var window = new AppConfig { Volume = 40 };    // человек выставил в окне
        var disk = new AppConfig { Volume = 95 };

        ConfigMerge.KeepBackgroundChanges(window, disk, volumeWhenOpened: 70, showsInWindow: Array.Empty<string>());

        window.Volume.Should().Be(40);
    }

    [Fact]
    public void KeepBackgroundChanges_ShouldNotDuplicateNames()
    {
        var window = new AppConfig { ExcludedShows = { "Геркулес" } };
        var disk = new AppConfig { ExcludedShows = { "геркулес" } };

        ConfigMerge.KeepBackgroundChanges(window, disk, volumeWhenOpened: 70, showsInWindow: Array.Empty<string>());

        window.ExcludedShows.Should().HaveCount(1);
    }
}
