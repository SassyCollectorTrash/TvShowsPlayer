using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class LegacyConfigMigrationTests : IDisposable
{
    private readonly string _localAppData =
        Path.Combine(Path.GetTempPath(), $"tvsp_mig_{Guid.NewGuid():N}");

    public LegacyConfigMigrationTests()
    {
        Directory.CreateDirectory(_localAppData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_localAppData))
            Directory.Delete(_localAppData, recursive: true);
    }

    private string LegacyDir => Path.Combine(_localAppData, Branding.LegacyAppName);
    private string NewDir => Path.Combine(_localAppData, Branding.AppName);

    [Fact]
    public void Run_WhenLegacyDirExistsAndNewMissing_MovesDirAndPreservesFiles()
    {
        Directory.CreateDirectory(LegacyDir);
        File.WriteAllText(Path.Combine(LegacyDir, "appconfig.json"), "{}");
        File.WriteAllText(Path.Combine(LegacyDir, Branding.LegacyStateFileName), "{\"current\":\"Геркулес\"}");

        var moved = LegacyConfigMigration.Run(_localAppData);

        moved.Should().BeTrue();
        Directory.Exists(LegacyDir).Should().BeFalse();
        Directory.Exists(NewDir).Should().BeTrue();
        File.Exists(Path.Combine(NewDir, "appconfig.json")).Should().BeTrue();
    }

    [Fact]
    public void Run_RenamesLegacyStateFileToCanonical_PreservingProgress()
    {
        Directory.CreateDirectory(LegacyDir);
        File.WriteAllText(Path.Combine(LegacyDir, Branding.LegacyStateFileName), "{\"current\":\"Геркулес\"}");

        LegacyConfigMigration.Run(_localAppData);

        var canonical = Path.Combine(NewDir, Branding.StateFileName);
        File.Exists(canonical).Should().BeTrue();
        File.Exists(Path.Combine(NewDir, Branding.LegacyStateFileName)).Should().BeFalse();
        File.ReadAllText(canonical).Should().Contain("Геркулес");
    }

    [Fact]
    public void Run_WhenNewDirAlreadyExists_DoesNotOverwrite()
    {
        Directory.CreateDirectory(LegacyDir);
        File.WriteAllText(Path.Combine(LegacyDir, "appconfig.json"), "OLD");
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, "appconfig.json"), "CURRENT");

        LegacyConfigMigration.Run(_localAppData);

        File.ReadAllText(Path.Combine(NewDir, "appconfig.json")).Should().Be("CURRENT");
        Directory.Exists(LegacyDir).Should().BeTrue();   // старую не трогаем, раз новая уже есть
    }

    [Fact]
    public void Run_RenamesStateFile_EvenWhenOnlyNewDirExists()
    {
        // Пользователь уже на новой папке, но файл состояния ещё со старым именем.
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, Branding.LegacyStateFileName), "{}");

        LegacyConfigMigration.Run(_localAppData);

        File.Exists(Path.Combine(NewDir, Branding.StateFileName)).Should().BeTrue();
    }

    [Fact]
    public void Run_WhenNothingLegacy_IsNoOp()
    {
        LegacyConfigMigration.Run(_localAppData).Should().BeFalse();
    }

    [Fact]
    public void Run_DoesNotRenameStateFile_WhenCanonicalAlreadyPresent()
    {
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, Branding.StateFileName), "CANONICAL");
        File.WriteAllText(Path.Combine(NewDir, Branding.LegacyStateFileName), "LEGACY");

        LegacyConfigMigration.Run(_localAppData);

        File.ReadAllText(Path.Combine(NewDir, Branding.StateFileName)).Should().Be("CANONICAL");
    }
}
