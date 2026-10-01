using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flint.App.Controls;
using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Media;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>
/// The Media page through its real template: the card appears when it should, and every button,
/// key and the seek bar reach the TV.
/// </summary>
public sealed class MediaPageTests
{
    private readonly ManualTime clock = new();

    [AvaloniaFact]
    public void TheCard_IsHiddenWithNothingLoaded()
    {
        using var shown = Show(SnapshotFixtures.Media(SnapshotFixtures.ViewModel()));

        shown.Find<InfoCard>("NowPlayingCard").IsEffectivelyVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task TheCard_ShowsOncePlaying()
    {
        await using var receiver = new LoopbackReceiver();
        using var shown = Show(await PlayingAsync(receiver));

        shown.Find<InfoCard>("NowPlayingCard").IsEffectivelyVisible.ShouldBeTrue();
        shown.Find<TextBlock>("NowPlayingTitle").Text.ShouldBe("holiday.mp4");
    }

    [AvaloniaFact]
    public async Task EachButton_SendsItsControl()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);

        shown.Click("Pause");
        await receiver.WaitUntilAsync(sent => Controls(sent).Contains(new TransportControl(TransportAction.Pause)));

        shown.Click("Forward 30 seconds");
        clock.Advance(NowPlayingViewModel.SkipGather);
        await receiver.WaitUntilAsync(sent => Controls(sent).Contains(new TransportControl(TransportAction.SeekTo, 790_000)));

        shown.Click("Back 10 seconds");
        clock.Advance(NowPlayingViewModel.SkipGather);
        await receiver.WaitUntilAsync(sent => Controls(sent).Contains(new TransportControl(TransportAction.SeekTo, 780_000)));

        media.NowPlaying.VolumePercent = 40;
        shown.Click("Mute the TV");
        await receiver.WaitUntilAsync(sent => Controls(sent).Contains(new VolumeControl(0f)));

        shown.Click("Stop playback");
        await receiver.WaitUntilAsync(sent => sent.OfType<MediaCommandMessage>().Any(command => command.Action == MediaAction.Clear));
    }

    [AvaloniaFact]
    public async Task TheRemainingTime_SwitchesToTheLength_WhenClicked()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);
        shown.Find<Button>("RemainingTime").Theme.ShouldNotBeNull("a missing theme resource fails silently");
        shown.Find<Button>("RemainingTime").Content.ShouldBe("-1:19:20");

        shown.Click("Switch between time left and total length");

        shown.Find<Button>("RemainingTime").Content.ShouldBe("1:32:00");
    }

    [AvaloniaFact]
    public async Task PreviousAndNext_AreShownButOff_UntilThereIsAQueue()
    {
        await using var receiver = new LoopbackReceiver();
        using var shown = Show(await PlayingAsync(receiver));

        shown.Named("Previous item").IsEffectivelyEnabled.ShouldBeFalse();
        shown.Named("Next item").IsEffectivelyEnabled.ShouldBeFalse();
        shown.Named("Previous item").IsEffectivelyVisible.ShouldBeTrue();
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.Space, "pause")]
    [InlineData(PhysicalKey.S, "stop")]
    [InlineData(PhysicalKey.M, "mute")]
    [InlineData(PhysicalKey.ArrowUp, "up")]
    [InlineData(PhysicalKey.ArrowDown, "down")]
    [InlineData(PhysicalKey.ArrowLeft, "back")]
    [InlineData(PhysicalKey.ArrowRight, "forward")]
    public async Task Keys_OnThePage_DoWhatTheirButtonsDo(PhysicalKey key, string expected)
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        media.NowPlaying.VolumePercent = 40;
        using var shown = Show(media);
        shown.Find<Button>("PlayPause").Focus();

        shown.Press(key);
        clock.Advance(NowPlayingViewModel.SkipGather);

        WireMessage wanted = expected switch
        {
            "pause" => Control(new TransportControl(TransportAction.Pause)),
            "stop" => new MediaCommandMessage(MediaAction.Clear),
            "mute" => Control(new VolumeControl(0f)),
            "up" => Control(new VolumeControl(0.45f)),
            "down" => Control(new VolumeControl(0.35f)),
            "back" => Control(new TransportControl(TransportAction.SeekTo, 750_000)),
            _ => Control(new TransportControl(TransportAction.SeekTo, 790_000)),
        };
        await receiver.WaitUntilAsync(sent => sent.Any(message => Same(message, wanted)));
    }

    [AvaloniaFact]
    public async Task Keys_AreLeftAlone_WhileATextBoxHasFocus()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);
        var typing = new TextBox();
        shown.Page.GetVisualDescendants().OfType<StackPanel>().First().Children.Add(typing);
        shown.Settle();
        typing.Focus();

        shown.Press(PhysicalKey.Space);
        shown.Press(PhysicalKey.S);

        media.NowPlaying.Phase.ShouldBe(PlaybackPhase.Playing);
        receiver.Received.OfType<ControlMessage>().ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Keys_WithAModifier_AreLeftAlone()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);
        shown.Find<Button>("RemainingTime").Focus();

        shown.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        shown.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.Control);
        shown.Settle();

        media.NowPlaying.Phase.ShouldBe(PlaybackPhase.Playing);
    }

    [AvaloniaFact]
    public async Task DraggingTheBar_SendsOneSeek_WhereItWasLetGo()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);
        var bar = shown.Find<SeekBar>("Seek");
        var start = shown.PointOn(bar, 0.25);
        var end = shown.PointOn(bar, 0.75);

        shown.Window.MouseDown(start, MouseButton.Left);
        shown.Settle();
        bar.IsScrubbing.ShouldBeTrue();
        media.NowPlaying.IsScrubbing.ShouldBeTrue();
        shown.Window.MouseMove(end);
        shown.Settle();
        shown.Window.MouseUp(end, MouseButton.Left);
        shown.Settle();

        bar.IsScrubbing.ShouldBeFalse();
        await receiver.WaitForAsync<ControlMessage>();
        var seeks = receiver.Received.OfType<ControlMessage>().Select(control => control.Event).OfType<TransportControl>().ToList();
        seeks.Count.ShouldBe(1);
        seeks[0].Action.ShouldBe(TransportAction.SeekTo);
        seeks[0].PositionMs.ShouldBeInRange(5_520_000 * 70 / 100, 5_520_000 * 80 / 100);
    }

    [AvaloniaTheory]
    [InlineData(PhysicalKey.ArrowRight, 790_000)]
    [InlineData(PhysicalKey.ArrowUp, 790_000)]
    [InlineData(PhysicalKey.ArrowLeft, 750_000)]
    [InlineData(PhysicalKey.ArrowDown, 750_000)]
    [InlineData(PhysicalKey.Home, 0)]
    [InlineData(PhysicalKey.End, 5_510_000)]
    public async Task TheFocusedBar_StepsBySkipDistances_AndCommitsAtOnce(PhysicalKey key, long expectedMs)
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);
        shown.Find<SeekBar>("Seek").Focus();

        shown.Press(key);

        await receiver.WaitForAsync<ControlMessage>();
        receiver.Received.OfType<ControlMessage>().Single().Event
            .ShouldBe(new TransportControl(TransportAction.SeekTo, expectedMs));
    }

    [AvaloniaFact]
    public async Task TheFocusedBar_LeavesOtherKeysToTheSlider()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);
        shown.Find<SeekBar>("Seek").Focus();

        shown.Press(PhysicalKey.PageUp);

        receiver.Received.OfType<ControlMessage>().ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void ADisabledBar_IgnoresItsKeysAndThePointer()
    {
        var bar = new SeekBar { Maximum = 100, Value = 50, IsEnabled = false };
        var committed = new List<double>();
        bar.SeekCommitted += (_, seconds) => committed.Add(seconds);
        var window = new Window { Width = 400, Height = 100, Content = bar };
        window.Show();
        try
        {
            window.UpdateLayout();
            bar.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            window.MouseDown(new Point(200, 50), MouseButton.Left);
            window.MouseUp(new Point(200, 50), MouseButton.Left);

            committed.ShouldBeEmpty();
            bar.IsScrubbing.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ARightClick_IsNotADrag()
    {
        var bar = new SeekBar { Maximum = 100, Value = 50 };
        var window = new Window { Width = 400, Height = 100, Content = bar };
        window.Show();
        try
        {
            window.UpdateLayout();
            window.MouseDown(new Point(200, 50), MouseButton.Right);

            bar.IsScrubbing.ShouldBeFalse();
            window.MouseUp(new Point(200, 50), MouseButton.Right);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EveryControl_HasAName_AndTheBarIsSpokenInWords()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        using var shown = Show(media);

        var card = shown.Find<InfoCard>("NowPlayingCard");
        foreach (var control in card.GetVisualDescendants().OfType<Control>()
                     .Where(control => control is Button or Slider && control.TemplatedParent is null && control.IsEffectivelyVisible))
        {
            AutomationProperties.GetName(control).ShouldNotBeNullOrWhiteSpace($"{control.GetType().Name} has no name");
        }

        var peer = ControlAutomationPeer.CreatePeerForElement(shown.Find<SeekBar>("Seek"));
        var spoken = peer.GetProvider<IValueProvider>()!;
        spoken.Value.ShouldBe("12 minutes 40 seconds of 1 hour 32 minutes");
        spoken.IsReadOnly.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => spoken.SetValue("1 minute"));
    }

    [Theory]
    [InlineData(Key.Space, false)]
    [InlineData(Key.Left, false)]
    [InlineData(Key.M, false)]
    public void WithNothingFocused_ThePageTakesItsKeys(Key key, bool left) =>
        MediaPage.LeftToFocused(null, key).ShouldBe(left);

    [AvaloniaTheory]
    [InlineData(Key.Space, true)]
    [InlineData(Key.Enter, false)]
    [InlineData(Key.Left, false)]
    public void AFocusedButton_KeepsSpace(Key key, bool left) =>
        MediaPage.LeftToFocused(new Button(), key).ShouldBe(left);

    [AvaloniaTheory]
    [InlineData(Key.Left, true)]
    [InlineData(Key.Right, true)]
    [InlineData(Key.Up, true)]
    [InlineData(Key.Down, true)]
    [InlineData(Key.Space, false)]
    [InlineData(Key.M, false)]
    public void AFocusedSlider_KeepsTheArrows(Key key, bool left) =>
        MediaPage.LeftToFocused(new Slider(), key).ShouldBe(left);

    [AvaloniaTheory]
    [InlineData(Key.Space)]
    [InlineData(Key.M)]
    [InlineData(Key.S)]
    public void AFocusedTextBox_KeepsEveryKey(Key key) =>
        MediaPage.LeftToFocused(new TextBox(), key).ShouldBeTrue();

    [AvaloniaFact]
    public void TheSeekBar_KeepsWhatItIsGiven()
    {
        var bar = new SeekBar { BubbleText = "0:40", SpokenValue = "40 seconds of 1 minute", StepBack = 5, StepForward = 60 };

        bar.BubbleText.ShouldBe("0:40");
        bar.SpokenValue.ShouldBe("40 seconds of 1 minute");
        bar.StepBack.ShouldBe(5);
        bar.StepForward.ShouldBe(60);
    }

    [AvaloniaFact]
    public void ABarNobodyListensTo_StillDragsAndStepsQuietly()
    {
        var bar = new SeekBar { Maximum = 100, Value = 50 };
        var window = new Window { Width = 400, Height = 100, Content = bar };
        window.Show();
        try
        {
            window.UpdateLayout();
            Should.NotThrow(() =>
            {
                bar.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                window.MouseDown(new Point(200, 50), MouseButton.Left);
                window.MouseUp(new Point(200, 50), MouseButton.Left);
            });
            bar.IsScrubbing.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void APageWithNothingToDrive_LeavesKeysAlone()
    {
        var page = new MediaPage();
        var window = new Window { Width = 800, Height = 600, Content = page };
        window.Show();
        try
        {
            window.UpdateLayout();
            Should.NotThrow(() =>
            {
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ThePageLetsGoOfItsCard_WhenGivenAnother()
    {
        await using var receiver = new LoopbackReceiver();
        var first = await PlayingAsync(receiver);
        var second = SnapshotFixtures.Media(SnapshotFixtures.ViewModel());
        using var shown = Show(first);

        shown.Page.DataContext = second;
        shown.Settle();
        shown.Find<SeekBar>("Seek").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });

        receiver.Received.OfType<ControlMessage>().ShouldBeEmpty("the first card no longer hears the bar");
    }

    [AvaloniaFact]
    public async Task AChosenFile_IsSent_AndNoChoiceSendsNothing()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        var file = Path.Combine(Path.GetTempPath(), $"flint-pick-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(file, new byte[1024], TestContext.Current.CancellationToken);
        try
        {
            using var shown = Show(media);
            shown.Page.PickFile = _ => Task.FromResult<string?>(null);
            media.Cast.ChooseMediaFileCommand.Execute(null);
            shown.Settle();
            receiver.Received.OfType<MediaDataMessage>().ShouldBeEmpty();

            shown.Page.PickFile = _ => Task.FromResult<string?>(file);
            media.Cast.ChooseMediaFileCommand.Execute(null);
            await receiver.WaitForAsync<MediaDataMessage>();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task TheSystemPicker_WithNothingChosen_GivesNoPath()
    {
        var window = new Window { Content = new MediaPage() };
        window.Show();
        try
        {
            var page = (MediaPage)window.Content!;
            (await page.PickFile(window)).ShouldBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ChoosingAFile_AsksThePageForAPicker_AndAPageWithoutOneDoesNothing()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver);
        var page = new MediaPage { DataContext = media };

        // Not in a window, so there is no picker to open: the request is answered with nothing.
        Should.NotThrow(() => media.Cast.ChooseMediaFileCommand.Execute(null));

        page.DataContext = null;
        Should.NotThrow(() => media.Cast.ChooseMediaFileCommand.Execute(null));
    }

    private static IEnumerable<ControlEvent> Controls(IReadOnlyCollection<WireMessage> sent) =>
        sent.OfType<ControlMessage>().Select(control => control.Event);

    private static ControlMessage Control(ControlEvent controlEvent) => new(0, controlEvent);

    private static bool Same(WireMessage sent, WireMessage wanted) => (sent, wanted) switch
    {
        (ControlMessage a, ControlMessage b) => a.Event == b.Event,
        (MediaCommandMessage a, MediaCommandMessage b) => a.Action == b.Action,
        _ => false,
    };

    private async Task<MediaPageViewModel> PlayingAsync(LoopbackReceiver receiver)
    {
        var cast = await PairedAsync(receiver, time: clock);
        var media = SnapshotFixtures.Media(cast);
        media.NowPlaying.BeginSending("holiday.mp4", isPicture: false);
        media.NowPlaying.SendFinished(new PlaybackSnapshot(PlaybackPhase.Playing, 760_000, 5_520_000, "", clock.GetUtcNow()));
        return media;
    }

    private static ShownPage Show(MediaPageViewModel media) => new(new MediaPage { DataContext = media });

    /// <summary>The page in a window, and ways to reach into it.</summary>
    private sealed class ShownPage : IDisposable
    {
        public ShownPage(MediaPage page)
        {
            Page = page;
            Window = new Window { Width = 1024, Height = 1100, Content = page };
            Window.Show();
            Settle();
        }

        public MediaPage Page { get; }

        public Window Window { get; }

        public T Find<T>(string name)
            where T : Control => Page.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

        public Control Named(string automationName) => Page.GetVisualDescendants().OfType<Control>()
            .First(control => AutomationProperties.GetName(control) == automationName && control.IsEffectivelyVisible);

        public void Click(string automationName)
        {
            var control = Named(automationName);
            var center = PointOn(control, 0.5);
            Window.MouseDown(center, MouseButton.Left);
            Window.MouseUp(center, MouseButton.Left);
            Settle();
        }

        public void Press(PhysicalKey key)
        {
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Settle();
        }

        public Point PointOn(Control control, double across) =>
            control.TranslatePoint(new Point(control.Bounds.Width * across, control.Bounds.Height / 2), Window)!.Value;

        public void Settle()
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Window.Close();
    }
}
