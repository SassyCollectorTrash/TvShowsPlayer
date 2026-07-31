using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

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

    private string NewDir => Path.Combine(_localAppData, Branding.AppName);
    private string LegacyDir => Path.Combine(_localAppData, Branding.LegacyAppName);

    [Fact]
    public void ResolveConfigDir_WhenNothingExists_ReturnsNewDir()
    {
        ChannelPaths.ResolveConfigDir(_localAppData).Should().Be(NewDir);
    }

    [Fact]
    public void ResolveConfigDir_WhenNewDirExists_ReturnsNewDir()
    {
        Directory.CreateDirectory(NewDir);
        Directory.CreateDirectory(LegacyDir);

        ChannelPaths.ResolveConfigDir(_localAppData).Should().Be(NewDir);
    }

    [Fact]
    public void ResolveConfigDir_WhenOnlyLegacyExists_ReturnsLegacyDir()
    {
        // Перенос папки не удался (занята) — работаем со старой, прогресс не теряем.
        Directory.CreateDirectory(LegacyDir);

        ChannelPaths.ResolveConfigDir(_localAppData).Should().Be(LegacyDir);
    }

    [Fact]
    public void ResolveStatePath_WhenCanonicalExists_ReturnsCanonical()
    {
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, Branding.StateFileName), "{}");

        ChannelPaths.ResolveStatePath(NewDir).Should().Be(Path.Combine(NewDir, Branding.StateFileName));
    }

    [Fact]
    public void ResolveStatePath_WhenOnlyLegacyExists_ReturnsLegacy()
    {
        Directory.CreateDirectory(LegacyDir);
        File.WriteAllText(Path.Combine(LegacyDir, Branding.LegacyStateFileName), "{}");

        ChannelPaths.ResolveStatePath(LegacyDir)
            .Should().Be(Path.Combine(LegacyDir, Branding.LegacyStateFileName));
    }

    [Fact]
    public void ResolveStatePath_WhenNothingExists_ReturnsCanonical()
    {
        Directory.CreateDirectory(NewDir);

        ChannelPaths.ResolveStatePath(NewDir).Should().Be(Path.Combine(NewDir, Branding.StateFileName));
    }

    [Fact]
    public void ResolveStatePath_PrefersCanonical_WhenBothExist()
    {
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, Branding.StateFileName), "{}");
        File.WriteAllText(Path.Combine(NewDir, Branding.LegacyStateFileName), "{}");

        ChannelPaths.ResolveStatePath(NewDir).Should().Be(Path.Combine(NewDir, Branding.StateFileName));
    }

    // --- главная регрессия: C#-путь и путь, куда пишет resume.lua, должны совпасть ---

    [Fact]
    public void AfterFailedDirMove_StateFileIsNormalized_SoAppAndLuaAgree()
    {
        // Папку перенести не удалось: старая на месте, новой нет, файл со старым именем.
        Directory.CreateDirectory(LegacyDir);
        File.WriteAllText(Path.Combine(LegacyDir, Branding.LegacyStateFileName), "{\"playlist_pos\":18}");

        var configDir = ChannelPaths.ResolveConfigDir(_localAppData);
        LegacyConfigMigration.RenameStateFile(configDir);   // нормализуем имя в РАБОЧЕЙ папке

        // resume.lua всегда пишет каноничное имя в config-dir — оно и должно резолвиться.
        var luaWrites = Path.Combine(configDir, Branding.StateFileName);
        ChannelPaths.ResolveStatePath(configDir).Should().Be(luaWrites);
        File.ReadAllText(luaWrites).Should().Contain("18");   // прогресс на месте
    }
}
