using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class EpisodeOrderingTests
{
    [Fact]
    public void Order_DragonBallSagas_OrdersByFilenameNumberNotFolderName()
    {
        // Папки-саги названы русским текстом, номер серии — в имени файла.
        string[] rels =
        {
            @"Император Пилаф Сага [001-013]\Dragon Ball HD 001.mkv",
            @"Турнир Сага [014-028]\Dragon Ball HD 014.mkv",
            @"Армия Красной Ленты Сага [029-045]\Dragon Ball HD 029.mkv",
            @"Император Пилаф Сага [001-013]\Dragon Ball HD 002.mkv",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            @"Император Пилаф Сага [001-013]\Dragon Ball HD 001.mkv",
            @"Император Пилаф Сага [001-013]\Dragon Ball HD 002.mkv",
            @"Турнир Сага [014-028]\Dragon Ball HD 014.mkv",
            @"Армия Красной Ленты Сага [029-045]\Dragon Ball HD 029.mkv");
    }

    [Fact]
    public void Order_SeasonFolder_OverridesSxxExxTypo()
    {
        // Опечатка S21E07 в папке «Сезон 2» — папка задаёт сезон.
        string[] rels =
        {
            @"Сезон 1\He-Man S01E01 A.mkv",
            @"Сезон 1\He-Man S01E02 B.mkv",
            @"Сезон 2\He-Man S02E01 C.mkv",
            @"Сезон 2\He-Man S21E07 D.mkv",
            @"Сезон 2\He-Man S02E08 E.mkv",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            @"Сезон 1\He-Man S01E01 A.mkv",
            @"Сезон 1\He-Man S01E02 B.mkv",
            @"Сезон 2\He-Man S02E01 C.mkv",
            @"Сезон 2\He-Man S21E07 D.mkv",
            @"Сезон 2\He-Man S02E08 E.mkv");
    }

    [Fact]
    public void Order_GummiSeasonFolders_ThenLeadingNumber()
    {
        string[] rels =
        {
            @"2_season\001_s02_Up.avi",
            @"1_season\002_s01_The_Sinister.avi",
            @"1_season\001_s01_A_New_Beginning.avi",
            @"2_season\002_s02_Loopy.avi",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            @"1_season\001_s01_A_New_Beginning.avi",
            @"1_season\002_s01_The_Sinister.avi",
            @"2_season\001_s02_Up.avi",
            @"2_season\002_s02_Loopy.avi");
    }

    [Fact]
    public void Order_CharodeykiSeasonFolders_RestartingNumbers()
    {
        string[] rels =
        {
            @"2 sezon\01. A znachit.avi",
            @"1 sezon\02. Nachalos.avi",
            @"1 sezon\01. Istoriya.avi",
            @"2 sezon\02. B znachit.avi",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            @"1 sezon\01. Istoriya.avi",
            @"1 sezon\02. Nachalos.avi",
            @"2 sezon\01. A znachit.avi",
            @"2 sezon\02. B znachit.avi");
    }

    [Fact]
    public void Order_SxxExxMultiSeasonInFolders()
    {
        string[] rels =
        {
            @"Season 2\S02E01. Through The Rabbit Hole.mkv",
            @"Season 1\S01E13. Day Of The Dragon.mkv",
            @"Season 1\S01E01. The Dark Hand.mkv",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            @"Season 1\S01E01. The Dark Hand.mkv",
            @"Season 1\S01E13. Day Of The Dragon.mkv",
            @"Season 2\S02E01. Through The Rabbit Hole.mkv");
    }

    [Fact]
    public void Order_LowercaseSeUnpaddedNumeric()
    {
        // s1e0, s1e2, s1e10 — числа, а не алфавит.
        string[] rels =
        {
            "s1e10.The garden.avi",
            "s1e2.The eye.avi",
            "s1e0.The begining.avi",
            "s1e13.Presto.avi",
            "s1e1.the night.avi",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            "s1e0.The begining.avi",
            "s1e1.the night.avi",
            "s1e2.The eye.avi",
            "s1e10.The garden.avi",
            "s1e13.Presto.avi");
    }

    [Fact]
    public void Order_FlatLeadingNumber()
    {
        string[] rels =
        {
            "010  Bulbasaur (Rus hi).mp4",
            "001  Pokemon I Choose You (Rus hi).mp4",
            "002  Pokemon Emergency (Rus hi).mp4",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            "001  Pokemon I Choose You (Rus hi).mp4",
            "002  Pokemon Emergency (Rus hi).mp4",
            "010  Bulbasaur (Rus hi).mp4");
    }

    [Fact]
    public void Order_ConstantTitleNumber_DoesNotBreakOrder()
    {
        // «Gundam 0079 [01] ... 720p» — 0079 и 720 не номер серии.
        string[] rels =
        {
            "Kidou Senshi Gundam 0079 [10] [BDRip 720p].mkv",
            "Kidou Senshi Gundam 0079 [01] [BDRip 720p].mkv",
            "Kidou Senshi Gundam 0079 [02] [BDRip 720p].mkv",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            "Kidou Senshi Gundam 0079 [01] [BDRip 720p].mkv",
            "Kidou Senshi Gundam 0079 [02] [BDRip 720p].mkv",
            "Kidou Senshi Gundam 0079 [10] [BDRip 720p].mkv");
    }

    [Fact]
    public void Order_NxNnFormat()
    {
        string[] rels =
        {
            "My Show 2x01.mkv",
            "My Show 1x10.mkv",
            "My Show 1x01.mkv",
            "My Show 1x02.mkv",
        };

        var ordered = EpisodeOrdering.Order(rels);

        ordered.Should().Equal(
            "My Show 1x01.mkv",
            "My Show 1x02.mkv",
            "My Show 1x10.mkv",
            "My Show 2x01.mkv");
    }
}
