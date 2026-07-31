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
