using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class UpdateCheckerTests
{
    private static string ReleaseJson(string tag, string url = "https://example/releases/tag/x") =>
        $"{{\"tag_name\":\"{tag}\",\"html_url\":\"{url}\"}}";

    [Fact]
    public void Parse_TagWithVPrefix_ReturnsVersionAndUrl()
    {
        var info = UpdateChecker.Parse(ReleaseJson("v1.2.0", "https://example/rel"));

        info.Should().NotBeNull();
        info!.Version.Should().Be(new Version(1, 2, 0));
        info.ReleaseUrl.Should().Be("https://example/rel");
    }

    [Fact]
    public void Parse_TagWithoutPrefix_ReturnsVersion()
    {
        UpdateChecker.Parse(ReleaseJson("2.0"))!.Version.Should().Be(new Version(2, 0));
    }

    [Fact]
    public void Parse_PrereleaseTag_UsesNumericCore()
    {
        UpdateChecker.Parse(ReleaseJson("v1.1.0-rc.1"))!.Version.Should().Be(new Version(1, 1, 0));
    }

    [Fact]
    public void Parse_MissingTag_ReturnsNull()
    {
        UpdateChecker.Parse("{\"html_url\":\"x\"}").Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull()
    {
        UpdateChecker.Parse("not json at all").Should().BeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"строка\"")]
    [InlineData("123")]
    [InlineData("null")]
    public void Parse_JsonThatIsNotAnObject_ReturnsNull_NotThrows(string json)
    {
        // прокси/капча могут вернуть 200 с чем угодно — метод обязан быть тотальным
        UpdateChecker.Parse(json).Should().BeNull();
    }

    [Fact]
    public void Parse_NonStringTag_ReturnsNull()
    {
        UpdateChecker.Parse("{\"tag_name\":123}").Should().BeNull();
    }

    [Fact]
    public void Parse_NonVersionTag_ReturnsNull()
    {
        UpdateChecker.Parse(ReleaseJson("latest")).Should().BeNull();
    }

    // --- ссылка на архив: без неё обновиться одной кнопкой нельзя ---

    private const string WithAsset = """
    {
      "tag_name": "v1.3.0",
      "html_url": "https://example/release",
      "assets": [
        { "name": "исходники.txt", "browser_download_url": "https://example/txt", "size": 10 },
        { "name": "LocalTV.zip", "browser_download_url": "https://example/LocalTV.zip", "size": 89599293 }
      ]
    }
    """;

    [Fact]
    public void Parse_ReleaseWithArchive_ExposesDirectLink()
    {
        var info = UpdateChecker.Parse(WithAsset)!;

        info.DownloadUrl.Should().Be("https://example/LocalTV.zip");
        info.FileName.Should().Be("LocalTV.zip");
        info.DownloadSize.Should().Be(89599293);
        info.CanInstall.Should().BeTrue();
    }

    [Fact]
    public void Parse_ReleaseWithoutArchive_CannotInstallItself()
    {
        var info = UpdateChecker.Parse(ReleaseJson("v1.3.0"))!;

        info.CanInstall.Should().BeFalse("тогда остаётся только открыть страницу релиза");
    }

    [Fact]
    public void Parse_AssetsBroken_DoesNotThrow()
    {
        var json = "{\"tag_name\":\"v1.3.0\",\"assets\":\"не список\"}";

        UpdateChecker.Parse(json)!.CanInstall.Should().BeFalse();
    }

    [Fact]
    public void Check_NoReleasesPublishedYet_IsNotANetworkProblem()
    {
        // GitHub отвечает 404, пока не выложен ни один релиз — врать про интернет нельзя
        var check = UpdateCheck.NoReleases;

        check.Reachable.Should().BeTrue();
        check.Latest.Should().BeNull();
    }

    [Fact]
    public void Check_Unreachable_SaysSo()
    {
        UpdateCheck.Unreachable.Reachable.Should().BeFalse();
    }

    [Fact]
    public void HasUpdate_WhenLatestNewer_IsTrue()
    {
        var latest = new UpdateInfo(new Version(1, 1, 0), null);

        UpdateChecker.HasUpdate(new Version(1, 0, 0), latest).Should().BeTrue();
    }

    [Fact]
    public void HasUpdate_WhenSameVersion_IsFalse()
    {
        var latest = new UpdateInfo(new Version(1, 0, 0), null);

        UpdateChecker.HasUpdate(new Version(1, 0, 0), latest).Should().BeFalse();
    }

    [Fact]
    public void HasUpdate_IgnoresRevisionComponentMismatch()
    {
        // Сборка репортит 1.0.0.0, тег релиза — v1.0.0: это одно и то же, не «обновление».
        var latest = new UpdateInfo(new Version(1, 0, 0), null);

        UpdateChecker.HasUpdate(new Version(1, 0, 0, 0), latest).Should().BeFalse();
    }

    [Fact]
    public void HasUpdate_WhenLatestOlder_IsFalse()
    {
        var latest = new UpdateInfo(new Version(0, 9, 0), null);

        UpdateChecker.HasUpdate(new Version(1, 0, 0), latest).Should().BeFalse();
    }
}
