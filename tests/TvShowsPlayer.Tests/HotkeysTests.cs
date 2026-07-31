using System.Linq;
using FluentAssertions;
using TvShowsPlayer.Core;
using Xunit;

namespace TvShowsPlayer.Tests;

/// <summary>Пользовательский набор модификаторов (комбо может занять другая программа).</summary>
public class HotkeyModifiersTests
{
    [Fact]
    public void Parse_CtrlShiftAlt_ReturnsAllThree()
    {
        Hotkeys.ParseModifiers("Ctrl+Shift+Alt")
            .Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt);
    }

    [Fact]
    public void Parse_WinAlt_ReturnsWinAndAlt()
    {
        Hotkeys.ParseModifiers("Win+Alt").Should().Be(HotkeyModifiers.Win | HotkeyModifiers.Alt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ерунда")]
    public void Parse_Unknown_FallsBackToCtrlAlt(string? text)
    {
        Hotkeys.ParseModifiers(text).Should().Be(HotkeyModifiers.Control | HotkeyModifiers.Alt);
    }

    [Fact]
    public void ForMode_UsesChosenModifiers()
    {
        var bindings = Hotkeys.ForMode(HotkeyMode.Production, "Ctrl+Shift");

        bindings.Should().OnlyContain(b => b.Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Shift));
    }

    [Fact]
    public void ForMode_Dev_AlwaysAddsShift_ToAvoidClashWithLiveChannel()
    {
        var bindings = Hotkeys.ForMode(HotkeyMode.Dev, "Ctrl+Alt");

        bindings.Should().OnlyContain(b => b.Modifiers.HasFlag(HotkeyModifiers.Shift));
    }

    [Fact]
    public void ChoicesForUi_AreOffered()
    {
        Hotkeys.ModifierChoices.Should().Contain("Ctrl+Alt").And.Contain("Ctrl+Shift+Alt");
    }
}

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
