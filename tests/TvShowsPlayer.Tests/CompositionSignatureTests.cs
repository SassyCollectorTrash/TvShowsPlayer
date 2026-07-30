using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class CompositionSignatureTests
{
    private static Show Make(string name, params string[] eps)
    {
        return new Show(name, eps);
    }

    [Fact]
    public void Compute_ReturnsLowercaseHexOf40Chars()
    {
        var shows = new[] { Make("A", "x") };

        var sig = CompositionSignature.Compute(shows);

        sig.Should().MatchRegex("^[0-9a-f]{40}$");
    }

    [Fact]
    public void Compute_SameComposition_IsStable()
    {
        var a = new[] { Make("Pokemon", "e1", "e2"), Make("Naruto", "n1") };
        var b = new[] { Make("Pokemon", "e1", "e2"), Make("Naruto", "n1") };

        CompositionSignature.Compute(a).Should().Be(CompositionSignature.Compute(b));
    }

    [Fact]
    public void Compute_AddedEpisode_ChangesSignature()
    {
        var before = new[] { Make("Pokemon", "e1", "e2") };
        var after = new[] { Make("Pokemon", "e1", "e2", "e3") };

        CompositionSignature.Compute(after).Should().NotBe(CompositionSignature.Compute(before));
    }

    [Fact]
    public void Compute_RenamedShow_ChangesSignature()
    {
        var before = new[] { Make("Pokemon", "e1") };
        var after = new[] { Make("Pokémon", "e1") };

        CompositionSignature.Compute(after).Should().NotBe(CompositionSignature.Compute(before));
    }
}
