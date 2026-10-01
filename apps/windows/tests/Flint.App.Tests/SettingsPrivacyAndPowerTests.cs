using System.Net;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core.Settings;
using Flint.Platform.Windows;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The parts of Settings that reach outside Flint: files, folders, the clipboard, and whether the PC
/// may sleep. Each is driven through a stand-in, so every failure Windows can hand back is exercised.
/// </summary>
public sealed class SettingsPrivacyAndPowerTests
{
    [Fact]
    public void Privacy_OpensEachFolder_AndSaysWhenWindowsWouldNot()
    {
        var folders = new RecordingFolderOpener();
        var privacy = Privacy(Service(), folders);

        privacy.OpenDataFolderCommand.Execute(null);
        privacy.OpenLogsFolderCommand.Execute(null);
        folders.Opened.ShouldBe(["data", "logs"]);
        privacy.Status.ShouldBeNull();
        privacy.DataFolder.ShouldBe("data");
        privacy.LogsFolder.ShouldBe("logs");

        folders.Succeeds = false;
        privacy.OpenLogsFolderCommand.Execute(null);
        privacy.Status.ShouldBe("Windows could not open logs.");
    }

    [Fact]
    public void Privacy_ResetAllAsksFirst_ThenRestoresEverySection()
    {
        using var service = Service(new AppSettings
        {
            General = new GeneralSettings { KeepAwake = false },
            Media = new MediaSettings { Shuffle = true },
        });
        var privacy = Privacy(service);

        privacy.ResetAll.AskCommand.Execute(null);
        service.Current.Media.Shuffle.ShouldBeTrue();
        privacy.ResetAll.ConfirmCommand.Execute(null);

        service.Current.ShouldBe(AppSettings.Default);
        privacy.Status.ShouldBe("Every setting is back to how Flint came.");
    }

    [Fact]
    public void Privacy_AsksTheViewToPickFiles()
    {
        var privacy = Privacy(Service());
        var exports = 0;
        var imports = 0;
        privacy.ExportRequested += (_, _) => exports++;
        privacy.ImportRequested += (_, _) => imports++;

        privacy.RequestExportCommand.Execute(null);
        privacy.RequestImportCommand.Execute(null);

        (exports, imports).ShouldBe((1, 1));
    }

    [Fact]
    public void Privacy_RequestsWithNoViewListening_AreHarmless()
    {
        var privacy = Privacy(Service());

        Should.NotThrow(() => privacy.RequestExportCommand.Execute(null));
        Should.NotThrow(() => privacy.RequestImportCommand.Execute(null));
    }

    [Fact]
    public void Transfer_OffersOnlySettingsFiles()
    {
        SettingsFileTransfer.JsonFiles.Name.ShouldBe("Flint settings");
        SettingsFileTransfer.JsonFiles.Patterns.ShouldBe(["*.json"]);
        SettingsFileTransfer.JsonFiles.MimeTypes.ShouldBe(["application/json"]);
    }

    [Fact]
    public void Privacy_AFileThatCannotBeWritten_IsReported()
    {
        var privacy = Privacy(Service());

        privacy.Export(new FailingStream());

        privacy.Status.ShouldBe("The settings could not be written to that file.");
    }

    [Fact]
    public void Privacy_AFileTooLargeToBeSettings_ChangesNothing()
    {
        using var service = Service();
        var privacy = Privacy(service);
        var huge = "{\"schemaVersion\":1,\"padding\":\"" + new string('x', PrivacySettingsViewModel.MaxImportBytes) + "\"}";

        privacy.Import(new MemoryStream(Encoding.UTF8.GetBytes(huge)));

        privacy.Status.ShouldBe("That file is too large to be Flint settings. Nothing was changed.");
        service.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void Privacy_AFileThatCannotBeRead_ChangesNothing()
    {
        using var service = Service();
        var privacy = Privacy(service);

        privacy.Import(new FailingStream());

        privacy.Status.ShouldBe("That file could not be read. Nothing was changed.");
        service.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void Privacy_ListsWhatItKeeps_AndRefusesMissingParts()
    {
        var privacy = Privacy(Service());

        privacy.Settings.Count.ShouldBe(5);
        privacy.KeptItems.ShouldContain("Your settings.");
        Should.Throw<ArgumentNullException>(() => new PrivacySettingsViewModel(null!, new RecordingFolderOpener(), "d", "l"));
        Should.Throw<ArgumentNullException>(() => new PrivacySettingsViewModel(Service(), null!, "d", "l"));
        Should.Throw<ArgumentException>(() => new PrivacySettingsViewModel(Service(), new RecordingFolderOpener(), " ", "l"));
        Should.Throw<ArgumentException>(() => new PrivacySettingsViewModel(Service(), new RecordingFolderOpener(), "d", " "));
        Should.Throw<ArgumentNullException>(() => privacy.Export(null!));
        Should.Throw<ArgumentNullException>(() => privacy.Import(null!));
    }

    [Fact]
    public async Task Transfer_ExportWritesTheChosenFile_AndClosesIt()
    {
        using var service = Service(new AppSettings { General = new GeneralSettings { KeepAwake = false } });
        var privacy = Privacy(service);
        var file = new KeptMemoryStream();

        await SettingsFileTransfer.ExportAsync(() => Task.FromResult<Stream?>(file), privacy);

        file.WasDisposed.ShouldBeTrue();
        AppSettingsJson.Parse(Encoding.UTF8.GetString(file.Contents)).General.KeepAwake.ShouldBeFalse();
        privacy.Status.ShouldBe("Settings exported.");
    }

    [Fact]
    public async Task Transfer_ImportReadsTheChosenFile_AndClosesIt()
    {
        using var service = Service();
        var privacy = Privacy(service);
        var file = new KeptMemoryStream(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"media\":{\"shuffle\":true}}"));

        await SettingsFileTransfer.ImportAsync(() => Task.FromResult<Stream?>(file), privacy);

        file.WasDisposed.ShouldBeTrue();
        service.Current.Media.Shuffle.ShouldBeTrue();
    }

    [Fact]
    public async Task Transfer_CancellingThePickerDoesNothing()
    {
        using var service = Service();
        var privacy = Privacy(service);

        await SettingsFileTransfer.ExportAsync(() => Task.FromResult<Stream?>(null), privacy);
        await SettingsFileTransfer.ImportAsync(() => Task.FromResult<Stream?>(null), privacy);

        privacy.Status.ShouldBeNull();
        service.Current.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public async Task Transfer_AFileThatWillNotOpen_IsReported_NotThrown()
    {
        // The view's handlers are async void: anything thrown here would end Flint.
        var privacy = Privacy(Service());

        await SettingsFileTransfer.ExportAsync(() => throw new UnauthorizedAccessException(), privacy);
        privacy.Status.ShouldBe("That file could not be opened for writing. Nothing was saved.");

        await SettingsFileTransfer.ImportAsync(() => Task.FromException<Stream?>(new IOException("gone")), privacy);
        privacy.Status.ShouldBe("That file could not be opened. Nothing was changed.");
    }

    [Fact]
    public async Task Transfer_RefusesMissingParts()
    {
        var privacy = Privacy(Service());
        await Should.ThrowAsync<ArgumentNullException>(() => SettingsFileTransfer.ExportAsync(null!, privacy));
        await Should.ThrowAsync<ArgumentNullException>(() => SettingsFileTransfer.ExportAsync(() => Task.FromResult<Stream?>(null), null!));
        await Should.ThrowAsync<ArgumentNullException>(() => SettingsFileTransfer.ImportAsync(null!, privacy));
        await Should.ThrowAsync<ArgumentNullException>(() => SettingsFileTransfer.ImportAsync(() => Task.FromResult<Stream?>(null), null!));
    }

    [Theory]
    [InlineData(true, false, false, KeepAwakeLevel.None)]
    [InlineData(true, false, true, KeepAwakeLevel.System)]
    [InlineData(true, true, false, KeepAwakeLevel.SystemAndDisplay)]
    [InlineData(true, true, true, KeepAwakeLevel.SystemAndDisplay)]
    [InlineData(false, true, true, KeepAwakeLevel.None)]
    public void KeepAwake_SharingNeedsTheDisplay_PlayingNeedsOnlyThePc(bool wanted, bool mirroring, bool playing, KeepAwakeLevel expected) =>
        KeepAwakeCoordinator.Decide(wanted, mirroring, playing).ShouldBe(expected);

    [Fact]
    public void KeepAwake_ARefusalKeepsTheLevelInForce_AndIsTriedAgainOnTheNextChange()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var granting = false;
        var asked = new List<KeepAwakeLevel>();
        using var service = Service();
        using var keepAwake = new KeepAwakeCoordinator(shell.Cast, service, level =>
        {
            asked.Add(level);
            return granting;
        });

        SetMediaPlaying(shell.Cast, true);
        keepAwake.Level.ShouldBe(KeepAwakeLevel.None, "Windows refused");

        granting = true;
        SetMirroring(shell.Cast, true);
        keepAwake.Level.ShouldBe(KeepAwakeLevel.SystemAndDisplay);
        asked.ShouldBe([KeepAwakeLevel.System, KeepAwakeLevel.SystemAndDisplay]);
        shell.Dispose();
    }

    [Fact]
    public void KeepAwake_IgnoresUnrelatedChanges_AndDisposingTwiceReleasesOnce()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var asked = new List<KeepAwakeLevel>();
        using var service = Service();
        var keepAwake = new KeepAwakeCoordinator(shell.Cast, service, level =>
        {
            asked.Add(level);
            return true;
        });
        SetMirroring(shell.Cast, true);
        shell.Cast.ManualAddress = "10.0.0.9";

        keepAwake.Dispose();
        keepAwake.Dispose();
        SetMirroring(shell.Cast, false);

        asked.ShouldBe([KeepAwakeLevel.SystemAndDisplay, KeepAwakeLevel.None]);
    }

    [Fact]
    public void KeepAwake_RefusesMissingParts()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        using var service = Service();
        Should.Throw<ArgumentNullException>(() => new KeepAwakeCoordinator(null!, service, _ => true));
        Should.Throw<ArgumentNullException>(() => new KeepAwakeCoordinator(shell.Cast, null!, _ => true));
        Should.Throw<ArgumentNullException>(() => new KeepAwakeCoordinator(shell.Cast, service, null!));
    }

    [Fact]
    public async Task NeverAsk_ArrivingAtWebWhileMirroring_LeavesTheMirrorUp()
    {
        // With the question turned off, an arrival offer would be a switch nobody asked for.
        var remote = new RecordingBrowserRemote();
        var browser = await BrowserFixtures.ReadyViewModelAsync(remote);
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        using var service = Service(new AppSettings { General = new GeneralSettings { AskBeforeSwitching = false } });
        var prompt = new SurfaceSwitchPrompt();
        var coordinator = new ModeSessionCoordinator(shell.Cast, browser, prompt, service);
        SetMirroring(shell.Cast, true);
        browser.CanNavigate.ShouldBeTrue("the browser could have been put up");
        var sent = remote.Commands.Count;

        await coordinator.OfferOnArrivalAsync(TvSurfaceKind.Browser, () => true);

        prompt.IsOpen.ShouldBeFalse();
        shell.Cast.IsMirroring.ShouldBeTrue();
        remote.Commands.Count.ShouldBe(sent);
    }

    [Fact]
    public async Task NeverAsk_ArrivingAtScreenWhileTheBrowserShows_StartsNoMirror()
    {
        await using var receiver = new LoopbackReceiver();
        var remote = new RecordingBrowserRemote();
        var browser = await BrowserFixtures.ReadyViewModelAsync(remote);
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        shell.Cast.PairingCode = "123456";
        shell.Cast.ReceiverPort = receiver.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await shell.Cast.ConnectCommand.ExecuteAsync(null);
        shell.Cast.CanStartMirrorNow.ShouldBeTrue(shell.Cast.Failure ?? "a mirror could have been started");
        using var service = Service(new AppSettings { General = new GeneralSettings { AskBeforeSwitching = false } });
        var prompt = new SurfaceSwitchPrompt();
        var coordinator = new ModeSessionCoordinator(shell.Cast, browser, prompt, service);
        browser.Address = "https://example.test/";
        await browser.NavigateCommand.ExecuteAsync(null);

        await coordinator.OfferOnArrivalAsync(TvSurfaceKind.Mirror, () => true);

        prompt.IsOpen.ShouldBeFalse();
        shell.Cast.IsMirroring.ShouldBeFalse();
        browser.HasOpenBrowserSurface.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void TheRealCopyButton_PutsTheReportOnTheClipboard()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var about = shell.Settings.Section<AboutSettingsViewModel>();
        var view = new AboutSettingsSection { DataContext = about };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        try
        {
            ButtonNamed(view, "COPY DETAILS FOR A BUG REPORT").Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            about.Status.ShouldBe("Copied. Paste it into your report.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ACopyFromASectionOutsideAWindow_SaysTheClipboardWasUnavailable()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var about = shell.Settings.Section<AboutSettingsViewModel>();
        var view = new AboutSettingsSection { DataContext = about };

        about.CopyReportCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        about.Status.ShouldNotBeNull().ShouldStartWith("Windows would not let Flint use the clipboard");
        view.DataContext = null;
        about.CopyReportCommand.Execute(null);
    }

    [AvaloniaFact]
    public void TheRealExportAndImportButtons_SurviveAWindowWithNoFilePicker()
    {
        // The headless window, like a locked-down PC, offers nowhere to save or open: nothing happens.
        using var service = Service();
        var privacy = Privacy(service);
        var view = new PrivacySettingsSection { DataContext = privacy };
        var window = new Window { Width = 800, Height = 900, Content = view };
        window.Show();
        try
        {
            ButtonNamed(view, "EXPORT SETTINGS").Command!.Execute(null);
            ButtonNamed(view, "IMPORT SETTINGS").Command!.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            privacy.Status.ShouldBeNull();
            service.Current.ShouldBe(AppSettings.Default);
        }
        finally
        {
            window.Close();
        }

        view.DataContext = null;
        privacy.RequestExportCommand.Execute(null);
    }

    private static Button ButtonNamed(Control root, string content) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, content));

    private static SettingsService Service(AppSettings? initial = null) =>
        new(new InMemoryAppSettingsStore(initial), (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

    private static PrivacySettingsViewModel Privacy(ISettingsService service, IFolderOpener? folders = null) =>
        new(service, folders ?? new RecordingFolderOpener(), "data", "logs");

    /// <summary>Sets the mirror flag and announces it, as a real mirror starting or stopping does.</summary>
    private static void SetMirroring(CastPageViewModel cast, bool value)
    {
        typeof(CastPageViewModel).GetField("_isMirroring", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cast, value);
        typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject)
            .GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string)])!
            .Invoke(cast, [nameof(CastPageViewModel.IsMirroring)]);
    }

    private static void SetMediaPlaying(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.IsMediaPlaying))!.SetValue(cast, value);

    private sealed class RecordingFolderOpener : IFolderOpener
    {
        public List<string> Opened { get; } = [];

        public bool Succeeds { get; set; } = true;

        public bool Open(string path)
        {
            Opened.Add(path);
            return Succeeds;
        }
    }

    /// <summary>A stream that remembers what it held when it was closed.</summary>
    private sealed class KeptMemoryStream : MemoryStream
    {
        public KeptMemoryStream()
        {
        }

        public KeptMemoryStream(byte[] bytes)
            : base(bytes)
        {
        }

        public bool WasDisposed { get; private set; }

        public byte[] Contents { get; private set; } = [];

        protected override void Dispose(bool disposing)
        {
            if (!WasDisposed)
            {
                Contents = ToArray();
                WasDisposed = true;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new IOException("the drive went away");

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("the drive went away");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("the drive went away");
    }
}
