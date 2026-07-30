using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class MpvIpcClientTests
{
    [Fact]
    public async Task SendCommandAsync_WithArgs_WritesJsonCommandLine()
    {
        var connection = new FakeMpvConnection();
        var client = new MpvIpcClient(connection);

        await client.SendCommandAsync(new object[] { "cycle", "pause" }, CancellationToken.None);

        connection.SentLines.Should().ContainSingle()
            .Which.Should().Be("{\"command\":[\"cycle\",\"pause\"]}");
    }

    [Fact]
    public async Task GetPropertyAsync_ReplyMatchesRequestId_ReturnsData()
    {
        var connection = new FakeMpvConnection(
            "{\"error\":\"success\",\"request_id\":1,\"data\":70}");
        var client = new MpvIpcClient(connection);

        var volume = await client.GetPropertyAsync<int>("volume", CancellationToken.None);

        volume.Should().Be(70);
        connection.SentLines.Single().Should()
            .Be("{\"command\":[\"get_property\",\"volume\"],\"request_id\":1}");
    }

    [Fact]
    public async Task GetPropertyAsync_EventsPrecedeReply_SkipsThemAndReturnsData()
    {
        var connection = new FakeMpvConnection(
            "{\"event\":\"property-change\",\"name\":\"volume\"}",
            "{\"error\":\"success\",\"request_id\":1,\"data\":42}");
        var client = new MpvIpcClient(connection);

        var volume = await client.GetPropertyAsync<int>("volume", CancellationToken.None);

        volume.Should().Be(42);
    }

    [Fact]
    public async Task GetPropertyAsync_SecondCall_IncrementsRequestId()
    {
        var connection = new FakeMpvConnection(
            "{\"error\":\"success\",\"request_id\":1,\"data\":true}",
            "{\"error\":\"success\",\"request_id\":2,\"data\":false}");
        var client = new MpvIpcClient(connection);

        await client.GetPropertyAsync<bool>("pause", CancellationToken.None);
        await client.GetPropertyAsync<bool>("pause", CancellationToken.None);

        connection.SentLines[1].Should()
            .Be("{\"command\":[\"get_property\",\"pause\"],\"request_id\":2}");
    }

    [Fact]
    public async Task GetPropertyAsync_ReplyIsGenuineError_ThrowsMpvIpcException()
    {
        var connection = new FakeMpvConnection(
            "{\"error\":\"invalid parameter\",\"request_id\":1}");
        var client = new MpvIpcClient(connection);

        var act = () => client.GetPropertyAsync<int>("volume", CancellationToken.None);

        await act.Should().ThrowAsync<MpvIpcException>();
    }

    [Fact]
    public async Task GetPropertyAsync_PropertyUnavailable_ReturnsDefaultWithoutThrowing()
    {
        // mpv отдаёт «property unavailable», напр. по 'path' когда файл не загружен
        // (переход при loadlist) — это «нет значения», не фатальная ошибка.
        var connection = new FakeMpvConnection(
            "{\"error\":\"property unavailable\",\"request_id\":1}");
        var client = new MpvIpcClient(connection);

        var path = await client.GetPropertyAsync<string>("path", CancellationToken.None);

        path.Should().BeNull();
    }

    /// <summary>Поддельный транспорт: копит отправленное, отдаёт заранее заданные ответы.</summary>
    private sealed class FakeMpvConnection : IMpvConnection
    {
        private readonly Queue<string> _incoming;

        public FakeMpvConnection(params string[] incoming)
        {
            _incoming = new Queue<string>(incoming);
        }

        public List<string> SentLines { get; } = new();

        public Task SendLineAsync(string line, CancellationToken cancellationToken)
        {
            SentLines.Add(line);
            return Task.CompletedTask;
        }

        public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(_incoming.Count > 0 ? _incoming.Dequeue() : null);
        }

        public void Dispose()
        {
        }
    }
}
