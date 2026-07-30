using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class DryRunTests
{
    [Theory]
    [InlineData("Show S01E02.mkv", "SxxExx")]
    [InlineData("Show 1x05.mkv", "NxNN")]
    [InlineData(@"Сезон 2\005 серия.mkv", "папка-сезон + № в имени")]
    [InlineData("005 серия.mkv", "№ в имени файла")]
    public void DetectionLabel_RecognizesScheme(string rel, string expected)
    {
        EpisodeOrdering.DetectionLabel(rel).Should().Be(expected);
    }

    [Fact]
    public void FindSeasonAnomalies_SeasonInNameDiffersFromFolder_FlagsFile()
    {
        var rels = new[] { @"Сезон 2\He-Man S21E07.mkv" };

        var anomalies = EpisodeOrdering.FindSeasonAnomalies(rels);

        anomalies.Should().ContainSingle();
        anomalies[0].FileName.Should().Be("He-Man S21E07.mkv");
        anomalies[0].SeasonInName.Should().Be(21);
        anomalies[0].SeasonFolder.Should().Be(2);
    }

    [Fact]
    public void FindSeasonAnomalies_WhenConsistent_ReturnsEmpty()
    {
        var rels = new[] { @"Сезон 2\He-Man S02E07.mkv" };

        EpisodeOrdering.FindSeasonAnomalies(rels).Should().BeEmpty();
    }

    [Fact]
    public void Build_ReportsCountLabelAndFirstEpisodes()
    {
        var root = @"D:\Root";
        var show = new Show("Pokemon",
            new[] { @"D:\Root\Pokemon\Pokemon S01E01.mkv", @"D:\Root\Pokemon\Pokemon S01E02.mkv" });

        var report = DryRun.Build(root, new[] { show }, head: 1);

        report.Should().ContainSingle();
        report[0].Name.Should().Be("Pokemon");
        report[0].EpisodeCount.Should().Be(2);
        report[0].DetectionLabel.Should().Be("SxxExx");
        report[0].FirstEpisodes.Should().ContainSingle().Which.Should().Be("Pokemon S01E01.mkv");
    }

    [Fact]
    public void Build_IncludesSeasonAnomalies()
    {
        var root = @"D:\Root";
        var show = new Show("He-Man", new[] { @"D:\Root\He-Man\Сезон 2\He-Man S21E07.mkv" });

        var report = DryRun.Build(root, new[] { show });

        report[0].Anomalies.Should().ContainSingle();
        report[0].Anomalies[0].SeasonInName.Should().Be(21);
    }
}
