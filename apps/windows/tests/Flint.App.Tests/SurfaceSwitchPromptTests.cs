using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>What the switch question says, for every switch there is.</summary>
public sealed class SurfaceSwitchCopyTests
{
    [Theory]
    [InlineData(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, "Switch the TV to the browser?",
        "Living Room is showing your screen. Opening the browser stops the mirror.", "SWITCH TO BROWSER", "KEEP MIRRORING")]
    [InlineData(TvSurfaceKind.Media, TvSurfaceKind.Browser, "Switch the TV to the browser?",
        "Living Room is playing a file from this PC. Opening the browser stops it.", "SWITCH TO BROWSER", "KEEP PLAYING")]
    [InlineData(TvSurfaceKind.Browser, TvSurfaceKind.Mirror, "Mirror your screen instead?",
        "Living Room is showing the browser. Mirroring closes it; your tabs are kept for when you come back.",
        "START MIRRORING", "KEEP THE BROWSER")]
    [InlineData(TvSurfaceKind.Media, TvSurfaceKind.Mirror, "Mirror your screen instead?",
        "Living Room is playing a file from this PC. Mirroring stops it.", "START MIRRORING", "KEEP PLAYING")]
    [InlineData(TvSurfaceKind.Mirror, TvSurfaceKind.Media, "Play this on the TV instead?",
        "Living Room is showing your screen. Playing this stops the mirror.", "PLAY ON TV", "KEEP MIRRORING")]
    [InlineData(TvSurfaceKind.Browser, TvSurfaceKind.Media, "Play this on the TV instead?",
        "Living Room is showing the browser. Playing this closes it; your tabs are kept for when you come back.",
        "PLAY ON TV", "KEEP THE BROWSER")]
    public void EverySwitch_SaysWhatIsOnTheTvAndWhatSwitchingStops(
        TvSurfaceKind current,
        TvSurfaceKind next,
        string title,
        string body,
        string confirm,
        string keep)
    {
        var copy = SurfaceSwitchCopy.For(current, next, "Living Room");

        copy.ShouldBe(new SurfaceSwitchCopy(title, body, confirm, keep));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnnamedTv_IsCalledTheTv(string? name)
    {
        SurfaceSwitchCopy.For(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, name).Body
            .ShouldBe("The TV is showing your screen. Opening the browser stops the mirror.");
    }

    [Fact]
    public void ATvNameIsTrimmed()
    {
        SurfaceSwitchCopy.For(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, "  Den  ").Body.ShouldStartWith("Den is showing");
    }

    [Theory]
    [InlineData(TvSurfaceKind.None, TvSurfaceKind.Browser)]
    [InlineData(TvSurfaceKind.Mirror, TvSurfaceKind.None)]
    [InlineData(TvSurfaceKind.Mirror, TvSurfaceKind.Mirror)]
    public void SomethingThatIsNotASwitch_IsAskedAboutByMistake(TvSurfaceKind current, TvSurfaceKind next)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SurfaceSwitchCopy.For(current, next, "Living Room"));
    }
}

/// <summary>The switch question: one at a time, and nothing changes until it is answered.</summary>
public sealed class SurfaceSwitchPromptTests
{
    private static readonly SurfaceSwitchCopy MirrorToBrowser =
        SurfaceSwitchCopy.For(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, "Living Room");

    [Fact]
    public async Task AskingShowsTheQuestionAndSwitchingAnswersYes()
    {
        var prompt = new SurfaceSwitchPrompt();

        var answer = prompt.AskAsync(MirrorToBrowser);

        prompt.IsOpen.ShouldBeTrue();
        prompt.Title.ShouldBe(MirrorToBrowser.Title);
        prompt.Body.ShouldBe(MirrorToBrowser.Body);
        prompt.ConfirmLabel.ShouldBe("SWITCH TO BROWSER");
        prompt.KeepLabel.ShouldBe("KEEP MIRRORING");
        answer.IsCompleted.ShouldBeFalse("nothing is decided until the person answers");

        prompt.ConfirmCommand.Execute(null);

        (await answer).ShouldBeTrue();
        prompt.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task KeepingAnswersNo()
    {
        var prompt = new SurfaceSwitchPrompt();
        var answer = prompt.AskAsync(MirrorToBrowser);

        prompt.KeepCommand.Execute(null);

        (await answer).ShouldBeFalse();
        prompt.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task DismissingAnswersNo_AndIsHarmlessWithNothingOpen()
    {
        var prompt = new SurfaceSwitchPrompt();
        prompt.Dismiss();
        var answer = prompt.AskAsync(MirrorToBrowser);

        prompt.Dismiss();

        (await answer).ShouldBeFalse();
        prompt.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task ANewQuestionWithdrawsTheOldOneAsKeep()
    {
        var prompt = new SurfaceSwitchPrompt();
        var first = prompt.AskAsync(MirrorToBrowser);

        var second = prompt.AskAsync(SurfaceSwitchCopy.For(TvSurfaceKind.Browser, TvSurfaceKind.Mirror, "Living Room"));

        (await first).ShouldBeFalse();
        prompt.IsOpen.ShouldBeTrue();
        prompt.Title.ShouldBe("Mirror your screen instead?");
        prompt.ConfirmCommand.Execute(null);
        (await second).ShouldBeTrue();
    }

    [Fact]
    public void AnsweringWithNothingOpen_ChangesNothing()
    {
        var prompt = new SurfaceSwitchPrompt();

        prompt.ConfirmCommand.Execute(null);
        prompt.KeepCommand.Execute(null);

        prompt.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void OpeningAndClosing_AreAnnounced()
    {
        var prompt = new SurfaceSwitchPrompt();
        var changes = new List<string?>();
        prompt.PropertyChanged += (_, change) => changes.Add(change.PropertyName);

        _ = prompt.AskAsync(MirrorToBrowser);
        prompt.KeepCommand.Execute(null);

        changes.Count(name => name == nameof(SurfaceSwitchPrompt.IsOpen)).ShouldBe(2);
    }

    [Fact]
    public void AQuestionWithoutWords_IsRefused()
    {
        Should.Throw<ArgumentNullException>(() => new SurfaceSwitchPrompt().AskAsync(null!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerGivenTheMomentTheQuestionOpens_IsTheAnswer(bool switching)
    {
        // Opening the question notifies listeners synchronously. One that answers right there used
        // to clear the question before AskAsync returned it, and AskAsync threw.
        var prompt = new SurfaceSwitchPrompt();
        prompt.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SurfaceSwitchPrompt.IsOpen) && prompt.IsOpen)
            {
                (switching ? prompt.ConfirmCommand : prompt.KeepCommand).Execute(null);
            }
        };

        var answer = prompt.AskAsync(MirrorToBrowser);

        answer.IsCompleted.ShouldBeTrue();
        (await answer).ShouldBe(switching);
        prompt.IsOpen.ShouldBeFalse();
    }
}
