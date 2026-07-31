using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

/// <summary>
/// «Отстоявшиеся» файлы: в эфир не должны попадать серии, которые прямо сейчас
/// качаются — иначе канал споткнётся о недописанный файл.
/// </summary>
public class LibraryReadinessTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    [Fact]
    public void Settled_FileUntouchedLongEnough_IsReady()
    {
        LibraryReadiness.IsSettled("Серия 01.mkv", Now.AddMinutes(-30), Now, Quiet).Should().BeTrue();
    }

    [Fact]
    public void Settled_FileJustWritten_IsNotReady()
    {
        // торрент дописывает файл прямо сейчас
        LibraryReadiness.IsSettled("Серия 02.mkv", Now.AddMinutes(-2), Now, Quiet).Should().BeFalse();
    }

    [Theory]
    [InlineData("Серия 03.mkv.part")]
    [InlineData("Серия 03.mkv.!qB")]
    [InlineData("Серия 03.mkv.crdownload")]
    [InlineData("Серия 03.mkv.aria2")]
    public void Settled_PartialDownloadMarker_IsNotReady(string name)
    {
        LibraryReadiness.IsSettled(name, Now.AddDays(-1), Now, Quiet).Should().BeFalse();
    }

    [Fact]
    public void Settled_ZeroQuietPeriod_AcceptsEverything()
    {
        // 0 = «не ждать»: пользователь сам отвечает за состав
        LibraryReadiness.IsSettled("Серия.mkv", Now, Now, TimeSpan.Zero).Should().BeTrue();
    }

    [Fact]
    public void Settled_FutureTimestamp_IsNotReady()
    {
        // криво выставленное время файла не должно пускать недокачанное в эфир
        LibraryReadiness.IsSettled("Серия.mkv", Now.AddHours(1), Now, Quiet).Should().BeFalse();
    }
}

/// <summary>Скан с учётом докачки: ждём только незрелые файлы, а не весь сериал.</summary>
public class ShowScannerSettlingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tvsp_settle_{Guid.NewGuid():N}");

    public ShowScannerSettlingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeShow(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static void Touch(string dir, string file, TimeSpan age)
    {
        var path = Path.Combine(dir, file);
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
    }

    [Fact]
    public void Scan_ShowWithDownloadingEpisodes_KeepsReadyOnes()
    {
        var show = MakeShow("Геркулес");
        Touch(show, "S01E01.mkv", TimeSpan.FromHours(5));
        Touch(show, "S01E02.mkv", TimeSpan.FromHours(5));
        Touch(show, "S02E01.mkv", TimeSpan.FromSeconds(30));   // качается прямо сейчас

        var shows = ShowScanner.Scan(_root, excluded: null, settleAfter: TimeSpan.FromMinutes(10));

        shows.Should().ContainSingle("сериал из эфира не убираем — ждёт только новая серия");
        shows[0].Episodes.Should().HaveCount(2);
    }

    [Fact]
    public void Scan_BrandNewShowStillDownloading_IsSkippedEntirely()
    {
        var ready = MakeShow("Геркулес");
        Touch(ready, "S01E01.mkv", TimeSpan.FromHours(5));
        var fresh = MakeShow("Новый сериал");
        Touch(fresh, "S01E01.mkv", TimeSpan.FromSeconds(10));

        var shows = ShowScanner.Scan(_root, excluded: null, settleAfter: TimeSpan.FromMinutes(10));

        shows.Select(s => s.Name).Should().ContainSingle().Which.Should().Be("Геркулес");
    }

    [Fact]
    public void Scan_WithoutSettlePeriod_BehavesAsBefore()
    {
        var show = MakeShow("Геркулес");
        Touch(show, "S01E01.mkv", TimeSpan.FromSeconds(1));

        ShowScanner.Scan(_root).Should().ContainSingle();
    }

    [Fact]
    public void Scan_ReportsSkippedFiles_SoUserCanBeTold()
    {
        var show = MakeShow("Геркулес");
        Touch(show, "S01E01.mkv", TimeSpan.FromHours(5));
        Touch(show, "S02E01.mkv", TimeSpan.FromSeconds(30));
        Touch(show, "S02E02.mkv", TimeSpan.FromSeconds(30));

        ShowScanner.Scan(_root, out var skipped, excluded: null, settleAfter: TimeSpan.FromMinutes(10));

        skipped.Should().Be(2, "о пропущенных файлах программа обязана сказать вслух");
    }
}
