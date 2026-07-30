using System.Linq;
using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

public class HotkeysTests
{
    [Fact]
    public void ForMode_Production_UsesControlAltForEveryBinding()
    {
        var bindings = Hotkeys.ForMode(HotkeyMode.Production);

        bindings.Should().OnlyContain(b =>
            b.Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt));
    }

    [Fact]
    public void ForMode_Dev_AddsShiftToEveryBinding()
    {
        var bindings = Hotkeys.ForMode(HotkeyMode.Dev);

        bindings.Should().OnlyContain(b =>
            b.Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));
    }

    [Fact]
    public void ForMode_Any_HasSevenBindingsWithUniqueIds()
    {
        var bindings = Hotkeys.ForMode(HotkeyMode.Production);

        bindings.Should().HaveCount(7);
        bindings.Select(b => b.Id).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(HotkeyAction.Pause, 0x20u)]          // Space
    [InlineData(HotkeyAction.NextEpisode, 0x27u)]    // Right
    [InlineData(HotkeyAction.ToggleMute, 0x4Du)]     // M
    [InlineData(HotkeyAction.Resync, 0x52u)]         // R
    [InlineData(HotkeyAction.ShowNow, 0x4Eu)]        // N
    public void ForMode_Production_MapsActionToExpectedVirtualKey(HotkeyAction action, uint vk)
    {
        var bindings = Hotkeys.ForMode(HotkeyMode.Production);

        bindings.Single(b => b.Action == action).VirtualKey.Should().Be(vk);
    }
}
