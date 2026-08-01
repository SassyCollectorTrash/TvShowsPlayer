using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

/// <summary>
/// Где лежат настройки и состояние канала. Правило должны одинаково понимать запуск
/// канала, кнопка «Применить к эфиру» и скрипт закладки — разъезд означает
/// потерянный прогресс просмотра.
/// </summary>
public class ChannelPathsTests : IDisposable
{
    private readonly string _localAppData =
        Path.Combine(Path.GetTempPath(), $"tvsp_paths_{Guid.NewGuid():N}");

    public ChannelPathsTests()
    {
        Directory.CreateDirectory(_localAppData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_localAppData))
            Directory.Delete(_localAppData, recursive: true);
    }

    [Fact]
    public void ConfigDir_IsNamedAfterTheApp()
    {
        ChannelPaths.ResolveConfigDir(_localAppData)
            .Should().Be(Path.Combine(_localAppData, Branding.AppName));
    }

    [Fact]
    public void StatePath_IsInsideConfigDir()
    {
        var configDir = ChannelPaths.ResolveConfigDir(_localAppData);

        ChannelPaths.ResolveStatePath(configDir)
            .Should().Be(Path.Combine(configDir, Branding.StateFileName));
    }

    [Fact]
    public void StatePath_MatchesWhatTheResumeScriptWrites()
    {
        // Скрипт закладки пишет файл с этим именем в рабочую папку канала. Если
        // приложение станет искать другое имя, прогресс просмотра «пропадёт».
        Branding.StateFileName.Should().Be("localtv-channel-state.json");
    }
}
