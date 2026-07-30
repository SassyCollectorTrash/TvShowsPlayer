using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class CarouselTests
{
    [Fact]
    public void Build_SlidingWindow_OverlapsByWindowMinusStep()
    {
        // 8 серий, окно 4, шаг 2: 1-2-3-4 → 3-4-5-6 → 5-6-7-8 → 7-8-1-2, период 4.
        var show = new Show("S", new[] { "e0", "e1", "e2", "e3", "e4", "e5", "e6", "e7" });

        var result = Carousel.Build(new[] { show }, window: 4, step: 2, capRotations: 1000);

        result.Rotations.Should().Be(4);
        result.Capped.Should().BeFalse();
        result.Playlist.Should().Equal(
            "e0", "e1", "e2", "e3",
            "e2", "e3", "e4", "e5",
            "e4", "e5", "e6", "e7",
            "e6", "e7", "e0", "e1");
    }

    [Fact]
    public void Build_HugePeriod_StopsAtCap()
    {
        // Длины 7 и 5 (взаимно простые) → полный период LCM = 35 ротаций.
        var a = new Show("A", Enumerable.Range(0, 7).Select(i => "a" + i).ToArray());
        var b = new Show("B", Enumerable.Range(0, 5).Select(i => "b" + i).ToArray());

        var result = Carousel.Build(new[] { a, b }, window: 4, step: 2, capRotations: 10);

        result.Rotations.Should().Be(10);
        result.Capped.Should().BeTrue();
    }

    [Fact]
    public void Build_PeriodFitsUnderCap_RunsFullPeriod()
    {
        var a = new Show("A", Enumerable.Range(0, 7).Select(i => "a" + i).ToArray());
        var b = new Show("B", Enumerable.Range(0, 5).Select(i => "b" + i).ToArray());

        var result = Carousel.Build(new[] { a, b }, window: 4, step: 2, capRotations: 1000);

        result.Rotations.Should().Be(35);
        result.Capped.Should().BeFalse();
    }

    [Fact]
    public void Build_WithStartCursors_StartsEachShowAtItsSeed()
    {
        var show = new Show("S", new[] { "e0", "e1", "e2", "e3", "e4", "e5", "e6", "e7" });

        var result = Carousel.Build(new[] { show }, new[] { 4 }, window: 4, step: 2, capRotations: 1000);

        result.Playlist.Take(4).Should().Equal("e4", "e5", "e6", "e7");
    }

    [Fact]
    public void Build_WithStartCursors_PeriodReturnsToSeedNotZero()
    {
        // засев не меняет длину периода (курсоры возвращаются к старту, а не к 0).
        var show = new Show("S", new[] { "e0", "e1", "e2", "e3", "e4", "e5", "e6", "e7" });

        var seeded = Carousel.Build(new[] { show }, new[] { 4 }, window: 4, step: 2, capRotations: 1000);
        var plain = Carousel.Build(new[] { show }, window: 4, step: 2, capRotations: 1000);

        seeded.Rotations.Should().Be(plain.Rotations);
        seeded.Capped.Should().BeFalse();
    }
}
