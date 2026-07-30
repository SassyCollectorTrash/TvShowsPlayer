using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class ShowOrderingTests
{
    private static IReadOnlyList<Show> Shows(params string[] names) =>
        names.Select(n => new Show(n, new[] { "01.mkv" })).ToList();

    [Fact]
    public void Apply_OrdersShowsByGivenNames()
    {
        var result = ShowOrdering.Apply(Shows("A", "B", "C"), new[] { "C", "A", "B" });

        result.Select(s => s.Name).Should().Equal("C", "A", "B");
    }

    [Fact]
    public void Apply_ShowsNotInOrder_AppendedInOriginalOrder()
    {
        var result = ShowOrdering.Apply(Shows("A", "B", "C"), new[] { "B" });

        result.Select(s => s.Name).Should().Equal("B", "A", "C");
    }

    [Fact]
    public void Apply_EmptyOrder_ReturnsUnchanged()
    {
        var result = ShowOrdering.Apply(Shows("A", "B"), Array.Empty<string>());

        result.Select(s => s.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Apply_OrderNamesNotPresent_AreIgnored()
    {
        var result = ShowOrdering.Apply(Shows("A", "B"), new[] { "X", "B", "A" });

        result.Select(s => s.Name).Should().Equal("B", "A");
    }
}
