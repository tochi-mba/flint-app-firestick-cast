using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>What closing the window, quitting and starting at sign-in do.</summary>
public sealed class WindowClosePolicyTests
{
    private static readonly TraySettings WithIcon = new();
    private static readonly TraySettings WithoutIcon = new() { ShowIcon = false };

    [Theory]
    [InlineData(CloseWindowOutcome.Ask, false, CloseStep.AskKeepRunning)]
    [InlineData(CloseWindowOutcome.Ask, true, CloseStep.AskKeepRunning)]
    [InlineData(CloseWindowOutcome.KeepRunning, true, CloseStep.Hide)]
    [InlineData(CloseWindowOutcome.Quit, false, CloseStep.Quit)]
    [InlineData(CloseWindowOutcome.Quit, true, CloseStep.ConfirmQuit)]
    public void ClosingTheWindow_DoesWhatTheSettingSays(CloseWindowOutcome outcome, bool onTheTv, CloseStep expected)
    {
        WindowClosePolicy.OnClose(new GeneralSettings { CloseWindow = outcome }, WithIcon, onTheTv).ShouldBe(expected);
    }

    [Theory]
    [InlineData(CloseWindowOutcome.Ask)]
    [InlineData(CloseWindowOutcome.KeepRunning)]
    public void WithoutATrayIcon_ClosingAlwaysQuits(CloseWindowOutcome outcome)
    {
        var general = new GeneralSettings { CloseWindow = outcome };

        WindowClosePolicy.OnClose(general, WithoutIcon, somethingOnTheTv: false).ShouldBe(CloseStep.Quit);
        WindowClosePolicy.OnClose(general, WithoutIcon, somethingOnTheTv: true).ShouldBe(CloseStep.ConfirmQuit);
    }

    [Fact]
    public void Quitting_AsksFirstOnlyWhileSomethingIsOnTheTv_AndOnlyWhenTheSettingSays()
    {
        var asks = new GeneralSettings();
        var doesNotAsk = new GeneralSettings { ConfirmQuitWhileActive = false };

        WindowClosePolicy.OnQuit(asks, somethingOnTheTv: true).ShouldBe(CloseStep.ConfirmQuit);
        WindowClosePolicy.OnQuit(asks, somethingOnTheTv: false).ShouldBe(CloseStep.Quit);
        WindowClosePolicy.OnQuit(doesNotAsk, somethingOnTheTv: true).ShouldBe(CloseStep.Quit);
    }

    [Fact]
    public void FlintStartsHidden_OnlyWhenAskedTo_AndOnlyWithAnIconToFindItBy()
    {
        WindowClosePolicy.StartsHidden(WithIcon, minimizedArgument: true).ShouldBeTrue();
        WindowClosePolicy.StartsHidden(WithIcon, minimizedArgument: false).ShouldBeFalse();
        WindowClosePolicy.StartsHidden(WithoutIcon, minimizedArgument: true).ShouldBeFalse("never an invisible app with no icon");
    }

    [Fact]
    public void MissingSettings_AreRefused()
    {
        Should.Throw<ArgumentNullException>(() => WindowClosePolicy.OnClose(null!, WithIcon, false));
        Should.Throw<ArgumentNullException>(() => WindowClosePolicy.OnClose(new GeneralSettings(), null!, false));
        Should.Throw<ArgumentNullException>(() => WindowClosePolicy.OnQuit(null!, false));
        Should.Throw<ArgumentNullException>(() => WindowClosePolicy.StartsHidden(null!, true));
    }
}
