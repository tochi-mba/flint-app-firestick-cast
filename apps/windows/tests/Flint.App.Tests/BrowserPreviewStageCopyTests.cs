using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The empty stage is the largest thing on the Web page, and the first thing a new user reads.
/// </summary>
/// <remarks>
/// It used to say "waiting for TV preview" for every frameless state — including the two where no
/// frame is ever coming: preview switched off, and a receiver that cannot capture. Telling someone
/// to wait for something that will never arrive is the specific failure these tests exist to stop.
/// </remarks>
public sealed class BrowserPreviewStageCopyTests
{
    [Fact]
    public void WithPreviewOff_TheStageDoesNotAskAnyoneToWait()
    {
        var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true);
        preview.SetRequested(false);

        preview.StageHeadline.ShouldBe("Preview is off");
        preview.StageHeadline.ShouldNotContain("Starting");
        preview.StageDetail.ShouldContain("The TV is showing the page");
    }

    [Fact]
    public void WithAReceiverThatCannotCapture_TheStageSaysSoRatherThanWaiting()
    {
        var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: false);

        preview.StageHeadline.ShouldBe("This receiver cannot send a preview");
        preview.StageDetail.ShouldContain("cannot mirror");
    }

    [Fact]
    public void WhileAPreviewIsGenuinelyComing_TheStageSaysItIsStarting()
    {
        var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true);

        preview.IsRequested.ShouldBeTrue();
        preview.StageHeadline.ShouldContain("Starting preview");
        preview.StageDetail.ShouldContain("mirrors it here");
    }

    [Fact]
    public void TheReceiverReportingUnavailableOverridesALocalRequest()
    {
        // The receiver is the authority. A local request must not keep the stage claiming a
        // preview is on its way after the television has said it cannot send one.
        var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true);
        preview.ApplyState(BrowserPreviewState.Unavailable);

        preview.StageHeadline.ShouldBe("This receiver cannot send a preview");
    }

    [AvaloniaFact]
    public void HintsDisappearOnceAFrameArrives()
    {
        // The stage is for the picture once there is one; guidance must not sit over live video.
        // Needs the Avalonia platform, because a frame is a real bitmap.
        var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true);
        preview.ShowStageHints.ShouldBeTrue();

        preview.Frame = new WriteableBitmap(
            new PixelSize(2, 2),
            new Vector(96, 96),
            PixelFormat.Bgra8888);

        preview.ShowStageHints.ShouldBeFalse();
    }
}
