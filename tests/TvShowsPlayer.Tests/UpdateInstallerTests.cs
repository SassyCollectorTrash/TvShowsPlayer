using System.IO.Compression;
using FluentAssertions;
using TvShowsPlayer.Core;

namespace TvShowsPlayer.Tests;

/// <summary>
/// Подготовка обновления. Сама подмена файлов — работа сценария PowerShell и
/// проверяется запуском, а здесь проверяем всё, что происходит до неё: куда
/// распаковывается новая версия, что считается программой и что остаётся на диске.
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "localtv-tests-" + Guid.NewGuid().ToString("N"));

    public UpdateInstallerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // временная папка — не повод ронять тест
        }
    }

    [Fact]
    public void DownloadFolder_ShouldStayInsideInstallFolder()
    {
        var installDir = Path.Combine(_root, "LocalTV");

        var folder = UpdateInstaller.DownloadFolder(installDir);

        folder.Should().StartWith(installDir);
    }

    [Fact]
    public void PrepareNewVersion_WhenArchiveHasProgram_ShouldUnpackNextToInstallFolder()
    {
        var installDir = CreateInstall();
        var zip = CreateArchive("LocalTV/TvShowsPlayer.App.exe");

        var newVersion = UpdateInstaller.PrepareNewVersion(zip, installDir);

        newVersion.Should().Be(Path.Combine(installDir + ".new", "LocalTV"));
        File.Exists(Path.Combine(newVersion!, "TvShowsPlayer.App.exe")).Should().BeTrue();
    }

    [Fact]
    public void PrepareNewVersion_WhenArchiveIsNotProgram_ShouldReturnNull()
    {
        var installDir = CreateInstall();
        var zip = CreateArchive("что-то-другое/readme.txt");

        var newVersion = UpdateInstaller.PrepareNewVersion(zip, installDir);

        newVersion.Should().BeNull();
    }

    [Fact]
    public void PrepareNewVersion_WhenArchiveIsNotProgram_ShouldLeaveNothingBehind()
    {
        var installDir = CreateInstall();
        var zip = CreateArchive("что-то-другое/readme.txt");

        UpdateInstaller.PrepareNewVersion(zip, installDir);

        Directory.Exists(installDir + ".new").Should().BeFalse();
    }

    [Fact]
    public void PrepareNewVersion_WhenUnpacked_ShouldRemoveDownloadedArchive()
    {
        var installDir = CreateInstall();
        var zip = CreateArchive("LocalTV/TvShowsPlayer.App.exe", inDownloadFolderOf: installDir);

        UpdateInstaller.PrepareNewVersion(zip, installDir);

        Directory.Exists(UpdateInstaller.DownloadFolder(installDir)).Should().BeFalse();
    }

    [Fact]
    public void PrepareNewVersion_WhenEarlierUpdateLeftFolders_ShouldRemoveThem()
    {
        var installDir = CreateInstall();
        var abandoned = Directory.CreateDirectory(installDir + ".old-20200101-000000");
        var broken = Directory.CreateDirectory(installDir + ".broken-20200101-000000");
        var zip = CreateArchive("LocalTV/TvShowsPlayer.App.exe");

        UpdateInstaller.PrepareNewVersion(zip, installDir);

        abandoned.Exists.Should().BeFalse(because: "прежние обновления не должны копиться на диске");
        broken.Refresh();
        broken.Exists.Should().BeFalse();
    }

    [Fact]
    public void PrepareNewVersion_WhenPreviousAttemptLeftUnpackedFolder_ShouldReplaceIt()
    {
        var installDir = CreateInstall();
        Directory.CreateDirectory(installDir + ".new");
        File.WriteAllText(Path.Combine(installDir + ".new", "хлам.txt"), "от прошлой попытки");
        var zip = CreateArchive("LocalTV/TvShowsPlayer.App.exe");

        UpdateInstaller.PrepareNewVersion(zip, installDir);

        File.Exists(Path.Combine(installDir + ".new", "хлам.txt")).Should().BeFalse();
    }

    [Fact]
    public void CanUpdateInPlace_WhenFolderIsWritable_ShouldBeTrue()
    {
        var installDir = CreateInstall();

        var canUpdate = UpdateInstaller.CanUpdateInPlace(installDir);

        canUpdate.Should().BeTrue();
    }

    [Fact]
    public void CanUpdateInPlace_WhenFolderIsMissing_ShouldBeFalse()
    {
        var installDir = Path.Combine(_root, "нет-такой-папки");

        var canUpdate = UpdateInstaller.CanUpdateInPlace(installDir);

        canUpdate.Should().BeFalse();
    }

    private string CreateInstall()
    {
        var installDir = Path.Combine(_root, "LocalTV");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "TvShowsPlayer.App.exe"), "прежняя версия");

        return installDir;
    }

    private string CreateArchive(string entry, string? inDownloadFolderOf = null)
    {
        var folder = inDownloadFolderOf is null
            ? _root
            : UpdateInstaller.DownloadFolder(inDownloadFolderOf);
        Directory.CreateDirectory(folder);

        var zip = Path.Combine(folder, "LocalTV.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(entry).Open()))
        {
            writer.Write("новая версия");
        }

        return zip;
    }
}
