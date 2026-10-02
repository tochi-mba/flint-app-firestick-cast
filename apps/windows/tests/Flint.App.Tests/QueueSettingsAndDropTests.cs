using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>The queue's settings, dropping files on the window, and what a sent file comes to.</summary>
public sealed class QueueSettingsAndDropTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"flint-drop-{Guid.NewGuid():N}");

    public QueueSettingsAndDropTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void EachQueueSetting_ChangesThroughTheService_AndReadsBack()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        var media = new MediaSettingsViewModel(service);
        var raised = new List<string?>();
        media.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        media.AutoPlayNext = false;
        media.Shuffle = true;
        media.RememberPlayback = false;
        media.IncludeSubfolders = true;
        media.Repeat = media.RepeatChoices.Single(choice => choice.Value == RepeatMode.One);
        media.PlayedBefore = media.PlayedBeforeChoices.Single(choice => choice.Value == ResumeMode.StartOver);
        media.DroppedOrder = media.DropOrderChoices.Single(choice => choice.Value == DropOrder.AsDropped);
        media.PictureSeconds = media.PictureChoices.Single(choice => choice.Value == 15);
        media.AtQueueEnd = media.QueueEndChoices.Single(choice => choice.Value == QueueEnd.TvHome);

        var saved = service.Current.Media;
        saved.AutoPlayNext.ShouldBeFalse();
        saved.Shuffle.ShouldBeTrue();
        saved.RememberPlayback.ShouldBeFalse();
        saved.IncludeSubfolders.ShouldBeTrue();
        saved.Repeat.ShouldBe(RepeatMode.One);
        saved.PlayedBefore.ShouldBe(ResumeMode.StartOver);
        saved.DropOrder.ShouldBe(DropOrder.AsDropped);
        saved.PictureSeconds.ShouldBe(15);
        saved.QueueEnd.ShouldBe(QueueEnd.TvHome);

        media.AutoPlayNext.ShouldBeFalse();
        media.Shuffle.ShouldBeTrue();
        media.RememberPlayback.ShouldBeFalse();
        media.IncludeSubfolders.ShouldBeTrue();
        media.Repeat!.Label.ShouldBe("The same file");
        media.PlayedBefore!.ToString().ShouldBe("Start from the beginning");
        media.DroppedOrder!.Label.ShouldBe("As dropped");
        media.PictureSeconds!.Label.ShouldBe("15 seconds");
        media.AtQueueEnd!.Label.ShouldBe("Go back to the TV's home screen");
        raised.ShouldContain(string.Empty, "every change redraws the section");
    }

    [Fact]
    public void NoChoice_ChangesNothing()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        var media = new MediaSettingsViewModel(service);

        media.Repeat = null;
        media.PlayedBefore = null;
        media.DroppedOrder = null;
        media.PictureSeconds = null;
        media.AtQueueEnd = null;

        service.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void APictureTimeSetByHand_ShowsAsTheNearestOffered()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        service.Update(current => current with { Media = current.Media with { PictureSeconds = 12 } });

        new MediaSettingsViewModel(service).PictureSeconds!.Value.ShouldBe(10);
    }

    [Fact]
    public void ThePrivacySection_ClearsThePlaybackHistory_AfterAsking()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        var history = new InMemoryMediaHistoryStore();
        history.Save(new MediaHistoryEntry("k", 60_000, 0, DateTimeOffset.UnixEpoch));
        var privacy = new PrivacySettingsViewModel(service, new ExplorerFolderOpener(), "d", "l", history);

        privacy.HasHistory.ShouldBeTrue();
        privacy.ClearHistory.AskCommand.Execute(null);
        privacy.ClearHistory.ConfirmCommand.Execute(null);

        history.Entries.ShouldBeEmpty();
        privacy.Status.ShouldBe("Playback history cleared.");
        privacy.KeptItems.ShouldContain(item => item.StartsWith("Where files you played stopped", StringComparison.Ordinal));
    }

    [Fact]
    public void ClearingWithNoHistory_StillSaysItIsDone()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        var privacy = new PrivacySettingsViewModel(service, new ExplorerFolderOpener(), "d", "l");

        privacy.ClearHistory.AskCommand.Execute(null);
        privacy.ClearHistory.ConfirmCommand.Execute(null);

        privacy.Status.ShouldBe("Playback history cleared.");
    }

    [Fact]
    public async Task TheDropLine_SaysWhatDroppingWouldDo()
    {
        using var idle = Shell();
        idle.DropText.ShouldBe("Drop to add to the queue. Connect to your TV to play them.");

        await using var tv = new LoopbackReceiver();
        await Pair(idle.Cast, tv);
        idle.DropText.ShouldBe("Drop to play on Living Room");

        idle.Media.NowPlaying.BeginSending("a.mp4", isPicture: false);
        idle.DropText.ShouldBe("Drop to add to the queue");
    }

    [Fact]
    public async Task Dropping_GoesToTheMediaPage_AndQueuesTheFiles()
    {
        using var shell = Shell();
        var file = Make("a.mp4");
        shell.IsDropTarget = true;

        await shell.DropFilesAsync([file]);

        shell.IsDropTarget.ShouldBeFalse();
        shell.IsMediaSelected.ShouldBeTrue();
        shell.Media.Queue.Rows.Single().Item.Path.ShouldBe(file);

        shell.Selected = shell.Destinations[0];
        await shell.DropFilesAsync([]);
        shell.IsMediaSelected.ShouldBeFalse("an empty drop goes nowhere");
        await Should.ThrowAsync<ArgumentNullException>(() => shell.DropFilesAsync(null!));
    }

    [AvaloniaFact]
    public async Task DraggingFilesOverTheWindow_ShowsTheDropLine_AndDroppingQueuesThem()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        var file = Make("a.mp4");
        try
        {
            window.Show();
            Settle(window);
            var item = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(file));
            var drag = new DataTransfer();
            drag.Add(DataTransferItem.CreateFile(item!));
            var middle = new Point(590, 390);

            window.DragDrop(middle, RawDragEventType.DragEnter, drag, DragDropEffects.Copy, RawInputModifiers.None);
            Settle(window);
            shell.IsDropTarget.ShouldBeTrue();
            window.FindControl<Border>("DropOverlay")!.IsVisible.ShouldBeTrue();

            window.DragDrop(middle, RawDragEventType.DragLeave, drag, DragDropEffects.Copy, RawInputModifiers.None);
            Settle(window);
            shell.IsDropTarget.ShouldBeFalse();

            window.DragDrop(middle, RawDragEventType.DragOver, drag, DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(middle, RawDragEventType.Drop, drag, DragDropEffects.Copy, RawInputModifiers.None);
            await Until(() => shell.Media.Queue.HasItems);
            Settle(window);

            shell.IsDropTarget.ShouldBeFalse();
            shell.IsMediaSelected.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DraggingTextOverTheWindow_IsRefused()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            Settle(window);
            var drag = new DataTransfer();
            drag.Add(DataTransferItem.CreateText("https://example.test/film.mp4"));

            window.DragDrop(new Point(590, 390), RawDragEventType.DragEnter, drag, DragDropEffects.Copy, RawInputModifiers.None);
            window.DragDrop(new Point(590, 390), RawDragEventType.Drop, drag, DragDropEffects.Copy, RawInputModifiers.None);
            Settle(window);

            shell.IsDropTarget.ShouldBeFalse();
            shell.Media.Queue.HasItems.ShouldBeFalse();
            DroppedFiles.PathsOf(drag).ShouldBeEmpty();
            DroppedFiles.PathsOf(null).ShouldBeEmpty();
            DroppedFiles.HasFiles(null).ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AWindowWithNoShell_IgnoresDrags()
    {
        var window = new MainWindow { Width = 1180, Height = 780 };
        try
        {
            window.Show();
            var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(Make("a.mp4")));
            var drag = new DataTransfer();
            drag.Add(DataTransferItem.CreateFile(file!));

            Should.NotThrow(() =>
            {
                window.DragDrop(new Point(10, 10), RawDragEventType.DragEnter, drag, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(new Point(10, 10), RawDragEventType.DragLeave, drag, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(new Point(10, 10), RawDragEventType.DragEnter, drag, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(new Point(10, 10), RawDragEventType.DragOver, drag, DragDropEffects.Copy, RawInputModifiers.None);
                window.DragDrop(new Point(10, 10), RawDragEventType.Drop, drag, DragDropEffects.Copy, RawInputModifiers.None);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task KeepingWhatTheTvShows_SendsNothing_AndSaysTheFileWasKept()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var prompt = new SurfaceSwitchPrompt();
        _ = new ModeSessionCoordinator(cast, new BrowserPageViewModel(cast), prompt);
        typeof(CastPageViewModel).GetField("_isMirroring", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(cast, true);

        var playing = cast.PlayFileAsync(Make("a.mp4"), 0, MediaTakeover.Ask);
        prompt.IsOpen.ShouldBeTrue();
        prompt.KeepCommand.Execute(null);

        (await playing).ShouldBe(MediaStart.Kept);
        tv.Received.OfType<MediaDataMessage>().ShouldBeEmpty();
        cast.TvShowingNotice.ShouldBe("Living Room is showing your screen.");
    }

    [Fact]
    public async Task WithNoTv_NothingIsSent()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();

        (await cast.PlayFileAsync(Make("a.mp4"), 0, MediaTakeover.OnlyIfFree)).ShouldBe(MediaStart.NotSent);
        cast.TvShowingNotice.ShouldBeNull("there is no coordinator, so nothing else is on the TV");
    }

    [Fact]
    public async Task ACancelledSend_IsSaidToBeCancelled()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var big = Path.Combine(folder, "big.mp4");
        await File.WriteAllBytesAsync(big, new byte[8 * 1024 * 1024], TestContext.Current.CancellationToken);

        var sending = cast.PlayFileAsync(big, 0, MediaTakeover.Ask);
        await Until(() => cast.NowPlaying.IsSending);
        cast.NowPlaying.CancelCommand.Execute(null);

        (await sending).ShouldBe(MediaStart.Cancelled);
    }

    private static MainWindowViewModel Shell() =>
        MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = System.Net.IPAddress.Loopback }));

    private string Make(string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
