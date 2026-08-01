using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class ChannelStateTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"tvsp_state_{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempPath();
        try
        {
            var state = new ChannelState
            {
                PlaylistPos = 7,
                TimePos = 12.5,
                Current = "Чародейки",
                Shows =
                {
                    ["Чародейки"] = @"1 sezon\05. Na polzu obschestvu.avi",
                    ["Геркулес"] = "Hercules.S01E05.mkv",
                },
            };

            state.Save(path);
            var loaded = ChannelState.Load(path);

            loaded.PlaylistPos.Should().Be(7);
            loaded.TimePos.Should().Be(12.5);
            loaded.Current.Should().Be("Чародейки");
            loaded.Shows.Should().HaveCount(2);
            loaded.Shows["Чародейки"].Should().Be(@"1 sezon\05. Na polzu obschestvu.avi");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = ChannelState.Load(TempPath());

        loaded.PlaylistPos.Should().Be(0);
        loaded.Current.Should().BeNull();
        loaded.Shows.Should().BeEmpty();
    }

    [Fact]
    public void Load_KitFormatWithoutShowsOrCurrent_ReturnsEmptyProgress()
    {
        // сокращённый файл состояния — только позиция, без карты сериалов
        var path = TempPath();
        File.WriteAllText(path, """{"playlist_pos":83,"time_pos":506.32}""");
        try
        {
            var loaded = ChannelState.Load(path);

            loaded.PlaylistPos.Should().Be(83);
            loaded.Shows.Should().BeEmpty();
            loaded.Current.Should().BeNull();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_ShowsAsEmptyArray_IsToleratedAsEmpty()
    {
        // mpv format_json пишет пустую таблицу как [] — не должно ронять загрузку
        var path = TempPath();
        File.WriteAllText(path, """{"playlist_pos":0,"time_pos":0,"shows":[]}""");
        try
        {
            var loaded = ChannelState.Load(path);

            loaded.Shows.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
