using System.Text.RegularExpressions;
using Flint.Core;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>Sharing from the command line: what it sends, what it says, and how it ends.</summary>
public sealed partial class MirrorCommandTests
{
    private static readonly DisplayInfo Second =
        new(1, 2, "DELL U2720Q", "dell", 1920, 0, 2560, 1440, DisplayRotation.Upright, IsMain: false);

    [Fact]
    public async Task AShare_SendsWhatWasAsked_WithItsSound_AndSaysWhatItSent()
    {
        var screen = new ScriptedScreen();
        var sound = new PacketSound();
        var tv = new CountingTv();
        var output = new StringWriter();
        using var stop = new CancellationTokenSource();

        var run = new MirrorCommand(screen, sound, output)
            .RunAsync(tv, new MirrorChoices(Mode: PictureMode.Game), Second, 1280, "Desk", stop.Token);
        await Until.TrueAsync(() => tv.Frames > 3 * MirrorCommand.FramesPerLine && tv.Packets > 3);
        await stop.CancelAsync();

        (await run).ShouldBe(FlintExitCode.Success);
        screen.Started.ToArray().ShouldBe([new MirrorSessionOptions(1, 60, 16_000_000, 1280)], "the game mode, no wider than the TV");
        sound.Running.ShouldBeFalse("sound stops with the picture");
        var text = output.ToString();
        text.ShouldContain("  Sharing 2 · DELL U2720Q · 2560 × 1440 and its sound. Ctrl+C to stop.");
        text.ShouldContain("  Smoother motion. Up to 720p, 60 frames a second, 16 Mbps.");
        text.ShouldContain("  Sound: ");
        text.ShouldContain(" packets sent, 0 dropped.");
        text.ShouldNotContain("The TV's remote");

        var sent = long.Parse(Stopped().Match(text).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var progress = Progress().Matches(text);
        progress.Count.ShouldBeGreaterThanOrEqualTo(3);
        progress.Count.ShouldBeLessThanOrEqualTo((int)(sent / MirrorCommand.FramesPerLine), "one line for every thirty frames, not every frame");
    }

    [Fact]
    public async Task WithoutSound_OnlyThePictureIsShared()
    {
        var sound = new PacketSound();
        var tv = new CountingTv();
        var output = new StringWriter();
        using var stop = new CancellationTokenSource();

        var screen = new ScriptedScreen();
        var run = new MirrorCommand(screen, sound, output)
            .RunAsync(tv, new MirrorChoices(Sound: false), Second, null, "Desk", stop.Token);
        await Until.TrueAsync(() => tv.Frames > 3);
        await stop.CancelAsync();

        (await run).ShouldBe(FlintExitCode.Success);
        sound.Started.ShouldBeFalse();
        tv.Packets.ShouldBe(0);
        screen.Pointer.ToArray().ShouldBe([true], "the pointer shows unless asked not to");
        var text = output.ToString();
        text.ShouldContain("  Sharing 2 · DELL U2720Q · 2560 × 1440. Ctrl+C to stop.");
        text.ShouldNotContain("Sound");
    }

    [Fact]
    public async Task WithoutThePointer_TheShareIsToldToLeaveItOut()
    {
        var screen = new ScriptedScreen();
        var tv = new CountingTv();
        using var stop = new CancellationTokenSource();

        var run = new MirrorCommand(screen, new PacketSound(), new StringWriter())
            .RunAsync(tv, new MirrorChoices(Sound: false, Pointer: false), Second, null, "Desk", stop.Token);
        await Until.TrueAsync(() => tv.Frames > 3);
        await stop.CancelAsync();

        (await run).ShouldBe(FlintExitCode.Success);
        screen.Pointer.ToArray().ShouldBe([false]);
    }

    [Fact]
    public async Task TheTvsRemote_StopsTheShare_AndItSaysSo()
    {
        var tv = new CountingTv();
        var output = new StringWriter();
        var command = new MirrorCommand(new ScriptedScreen(), new PacketSound(), output);
        command.StopForTv();

        var run = command.RunAsync(tv, new MirrorChoices(), Second, null, "Desk", TestContext.Current.CancellationToken);
        await Until.TrueAsync(() => tv.Frames > 3);
        command.StopForTv();

        (await run).ShouldBe(FlintExitCode.Success, "the share ran; the person ended it");
        output.ToString().ShouldContain("  The TV's remote stopped the share.");
        command.StopForTv();
    }

    [Fact]
    public async Task SoundThatCannotPlay_IsSaid_AndThePictureCarriesOn()
    {
        var tv = new CountingTv();
        var output = new StringWriter();
        using var stop = new CancellationTokenSource();

        var run = new MirrorCommand(new ScriptedScreen(), new UnavailableAudioEngine(), output)
            .RunAsync(tv, new MirrorChoices(), Second, null, "Desk", stop.Token);
        await Until.TrueAsync(() => tv.Frames > 3);
        await stop.CancelAsync();

        (await run).ShouldBe(FlintExitCode.Success);
        output.ToString().ShouldContain("  Sound did not play: ");
    }

    [Fact]
    public async Task AScreenThatSendsNothing_IsAFailure_EvenThoughNothingThrew()
    {
        var screen = new ScriptedScreen(encodes: false);
        var output = new StringWriter();
        using var stop = new CancellationTokenSource();

        var run = new MirrorCommand(screen, new PacketSound(), output)
            .RunAsync(new CountingTv(), new MirrorChoices(Sound: false), null, null, "Desk", stop.Token);
        await Until.TrueAsync(() => screen.Ticks > 5);
        await stop.CancelAsync();

        (await run).ShouldBe(FlintExitCode.MirrorProducedNoFrames);
        screen.Started.ToArray().ShouldBe([new MirrorSessionOptions(0, 30, 12_000_000, 1920)], "the first display, balanced");
        var text = output.ToString();
        text.ShouldContain("  Sharing the first display capture finds. Ctrl+C to stop.");
        text.ShouldContain("  No frames were encoded, so the television never showed anything.");
    }

    [Fact]
    public async Task APcThatCannotShare_SaysWhy_AndStopsItsSound()
    {
        var sound = new PacketSound();
        var output = new StringWriter();
        var screen = new ScriptedScreen(refusal: new MirrorEngineException("No encoder"));

        var exit = await new MirrorCommand(screen, sound, output)
            .RunAsync(new CountingTv(), new MirrorChoices(), Second, null, "Desk", TestContext.Current.CancellationToken);

        exit.ShouldBe(FlintExitCode.MirrorUnsupported);
        sound.Running.ShouldBeFalse();
        output.ToString().ShouldContain("  This PC cannot mirror: No encoder");
    }

    [GeneratedRegex(@"Mirror stopped: (\d+) frames")]
    private static partial Regex Stopped();

    [GeneratedRegex(@"^  \d+ frames, \d+ KiB, 0 idle ticks, 0 recoveries\r?$", RegexOptions.Multiline)]
    private static partial Regex Progress();
}
